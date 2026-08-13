using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>Cancels a posted invoice, putting back everything posting it moved.</summary>
/// <param name="SalesInvoiceId">The invoice.</param>
/// <param name="Reason">Why. Required, and kept on the invoice.</param>
/// <remarks>
/// For an invoice that should never have been raised. Goods a customer has actually taken
/// away come back as a sales return instead, which is a document of its own - the
/// difference being whether anything really happened, and a stock ledger that cannot tell
/// the two apart is one nobody can reconcile against a shelf.
/// </remarks>
public sealed record CancelSalesInvoiceCommand(Guid SalesInvoiceId, string Reason)
    : ICommand, ITransactional;

/// <summary>Validates <see cref="CancelSalesInvoiceCommand"/>.</summary>
public sealed class CancelSalesInvoiceCommandValidator
    : AbstractValidator<CancelSalesInvoiceCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelSalesInvoiceCommandValidator"/> class.</summary>
    public CancelSalesInvoiceCommandValidator()
    {
        RuleFor(c => c.SalesInvoiceId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("A reason is required when cancelling an invoice.")
            .MaximumLength(500);
    }
}

/// <summary>Handles <see cref="CancelSalesInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// The mirror of posting, and the same rule: one transaction, or nothing. An invoice whose
/// goods went back on the shelf but whose bill stayed outstanding would be a debt for
/// stock the firm still holds, and nobody would find it until a customer disputed it.
/// </para>
/// <para>
/// Both journals are cancelled rather than reversed by contras. A voucher's own
/// cancellation already keeps its number and its lines and takes it out of the balances,
/// which is how every other cancelled voucher here behaves; a contra would say the same
/// thing twice and leave a day book with two entries for one mistake.
/// </para>
/// </remarks>
public sealed class CancelSalesInvoiceCommandHandler
    : ICommandHandler<CancelSalesInvoiceCommand>
{
    private readonly ISalesInvoiceRepository _invoices;
    private readonly ISalesOrderRepository _orders;
    private readonly IStockDocumentRepository _documents;
    private readonly IStockLedgerRepository _stockLedger;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IBillRepository _bills;
    private readonly IVoucherRepository _vouchers;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;
    private readonly StockJournalPoster _stockJournal;

    /// <summary>Initialises a new instance of the <see cref="CancelSalesInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="stockLedger">The stock ledger repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="accounts">The inventory account map repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CancelSalesInvoiceCommandHandler(
        ISalesInvoiceRepository invoices,
        ISalesOrderRepository orders,
        IStockDocumentRepository documents,
        IStockLedgerRepository stockLedger,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IInventoryAccountMapRepository accounts,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IBillRepository bills,
        IVoucherRepository vouchers,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _orders = orders;
        _documents = documents;
        _stockLedger = stockLedger;
        _products = products;
        _batches = batches;
        _serials = serials;
        _bills = bills;
        _vouchers = vouchers;
        _firms = firms;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, stockLedger);
        _stockJournal = new StockJournalPoster(accounts, vouchers, numbering, financialYears);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        CancelSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "SalesInvoice.NoFirmSelected",
                "A firm must be selected to cancel an invoice."));
        }

        SalesInvoice? invoice = await _invoices.FindAsync(
            SalesInvoiceId.From(request.SalesInvoiceId), cancellationToken);

        if (invoice is null || invoice.FirmId != firmId)
        {
            return Result.Failure(Error.NotFound(
                "SalesInvoice.NotFound", "That invoice does not exist in the selected firm."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        // Every refusal has its say before anything is touched. The transaction would
        // roll back a half-done cancellation anyway, but an aggregate left cancelled in
        // memory after a refusal is a trap for whatever reads it next - and the check
        // that matters most here, whether the customer has already paid, is a read.
        Result<Bill?> bill = await FindBillAsync(invoice, cancellationToken);

        if (bill.IsFailure)
        {
            return Result.Failure(bill.Error);
        }

        if (bill.Value is { } outstanding && !outstanding.SettledAmount.IsZero)
        {
            return Result.Failure(Error.BusinessRule(
                "Bill.PartlySettled",
                $"Bill '{outstanding.BillNumber}' has {outstanding.SettledAmount} allocated "
                + "against it and cannot be withdrawn. Raise a credit note instead, which "
                + "leaves the payment where it was made."));
        }

        Result cancelled = invoice.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        Result withdrawn = bill.Value?.Cancel() ?? Result.Success();

        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        Result released = await ReleaseCreditAsync(invoice, cancellationToken);

        if (released.IsFailure)
        {
            return released;
        }

        Result reopened = await ReleaseOrderAsync(invoice, cancellationToken);

        if (reopened.IsFailure)
        {
            return reopened;
        }

        Result returned = await ReturnGoodsAsync(invoice, firm, request.Reason, cancellationToken);

        if (returned.IsFailure)
        {
            return returned;
        }

        Result reversed = await CancelJournalAsync(invoice, request.Reason, cancellationToken);

        if (reversed.IsFailure)
        {
            return reversed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Finds the debt the invoice raised, if it raised one.</summary>
    private async Task<Result<Bill?>> FindBillAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        if (invoice.BillId is not { } billId)
        {
            return Result.Success<Bill?>(null);
        }

        IReadOnlyDictionary<BillId, Bill> found = await _bills.GetManyAsync(
            [billId], cancellationToken);

        return found.TryGetValue(billId, out Bill? bill)
            ? Result.Success<Bill?>(bill)
            : Result.Failure<Bill?>(Error.NotFound(
                "SalesInvoice.BillMissing",
                $"The bill invoice '{invoice.Number}' raised no longer exists."));
    }

    /// <summary>Puts back what a cancelled return had set against the sale's bill.</summary>
    /// <remarks>
    /// A return raises no bill of its own - it allocates a credit against the bill of the
    /// sale it names. Cancelling the journal takes the customer's ledger back, but the
    /// allocation is a fact about a bill and nothing else removes it: left behind, the
    /// sale would read as settled by a credit note that no longer exists, and the debtors
    /// report would understate what is owed.
    /// <para>
    /// Missing until 2026-08-13, and found while building the purchase side's mirror of
    /// it. The two now do the same thing, which is the point of them being mirrors.
    /// </para>
    /// </remarks>
    private async Task<Result> ReleaseCreditAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        if (!invoice.IsReturn
            || invoice.JournalVoucherId is not { } journalId
            || invoice.ReturnsInvoiceId is not { } originalId)
        {
            return Result.Success();
        }

        SalesInvoice? original = await _invoices.FindAsync(originalId, cancellationToken);

        if (original?.BillId is not { } billId)
        {
            return Result.Success();
        }

        IReadOnlyDictionary<BillId, Bill> found = await _bills.GetManyAsync(
            [billId], cancellationToken);

        if (found.TryGetValue(billId, out Bill? bill))
        {
            bill.ReleaseAllocationsFrom(journalId);
        }

        return Result.Success();
    }

    /// <summary>Puts back on the order what a cancelled invoice had taken off it.</summary>
    /// <remarks>
    /// The goods are owed again, so a completed order reopens and a part-filled one gets
    /// its outstanding quantity back. Matched by product rather than by line, because an
    /// invoice line does not carry the order line it came from - the products on a
    /// converted invoice are the order's own, and where an order lists a product twice the
    /// quantity is put back against the lines in turn.
    /// <para>
    /// An order somebody has since closed deliberately stays closed. Reopening it would
    /// put work back in front of a warehouse that was told to stop, and the refusal is
    /// swallowed here on purpose: the cancellation is about the invoice, and an order
    /// nobody will fill again is not a reason to refuse it.
    /// </para>
    /// </remarks>
    private async Task<Result> ReleaseOrderAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        if (invoice.SalesOrderId is not { } orderId)
        {
            return Result.Success();
        }

        SalesOrder? order = await _orders.FindAsync(orderId, cancellationToken);

        if (order is null)
        {
            return Result.Success();
        }

        Dictionary<SalesOrderLineId, decimal> released = [];

        foreach (SalesInvoiceLine line in invoice.Lines)
        {
            decimal remaining = line.Quantity;

            foreach (SalesOrderLine candidate in order.Lines
                .Where(candidate => candidate.ProductId == line.ProductId
                    && candidate.InvoicedQuantity > 0m))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                decimal taken = Math.Min(remaining, candidate.InvoicedQuantity);

                released[candidate.Id] = released.GetValueOrDefault(candidate.Id) + taken;
                remaining -= taken;
            }
        }

        order.ReleaseInvoiced(released);

        return Result.Success();
    }

    /// <summary>Puts the goods back on the shelf, at what they left at.</summary>
    /// <remarks>
    /// Through the stock document's own cancellation, so the reversal is valued at the
    /// cost each movement was made at rather than at today's average - which is what
    /// keeps a cancellation from quietly restating the value of everything else on the
    /// shelf. The issue's journal goes with it.
    /// </remarks>
    private async Task<Result> ReturnGoodsAsync(
        SalesInvoice invoice,
        Firm firm,
        string reason,
        CancellationToken cancellationToken)
    {
        if (invoice.StockDocumentId is not { } documentId)
        {
            return Result.Success();
        }

        StockDocument? document = await _documents.FindAsync(documentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(Error.NotFound(
                "SalesInvoice.IssueMissing",
                $"The issue invoice '{invoice.Number}' raised no longer exists."));
        }

        Result cancelled = document.Cancel($"Sales invoice {invoice.Number} cancelled: {reason}");

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        IReadOnlyList<StockLedgerEntry> movements =
            await _stockLedger.ForDocumentAsync(document.Id, cancellationToken);

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. document.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await BatchResolver.ForDocumentAsync(
            document, _batches, cancellationToken);

        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials =
            await _serials.ForDocumentAsync(document.Id, cancellationToken);

        Result reversed = await _poster.ReverseAsync(
            document, movements, products, batches, serials, firm.BaseCurrency,
            _clock.UtcNow, cancellationToken);

        return reversed.IsFailure
            ? reversed
            : await _stockJournal.WithdrawAsync(document, reason, cancellationToken);
    }

    /// <summary>Takes the sale back out of the nominal ledger.</summary>
    private async Task<Result> CancelJournalAsync(
        SalesInvoice invoice,
        string reason,
        CancellationToken cancellationToken)
    {
        if (invoice.JournalVoucherId is not { } journalId)
        {
            return Result.Success();
        }

        Voucher? journal = await _vouchers.FindAsync(journalId, cancellationToken);

        if (journal is null)
        {
            return Result.Failure(Error.NotFound(
                "SalesInvoice.JournalMissing",
                $"The journal invoice '{invoice.Number}' raised no longer exists."));
        }

        return journal.Status == VoucherStatus.Cancelled
            ? Result.Success()
            : journal.Cancel($"Sales invoice {invoice.Number} cancelled: {reason}");
    }
}
