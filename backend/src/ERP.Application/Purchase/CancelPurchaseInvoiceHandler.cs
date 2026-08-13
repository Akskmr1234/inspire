using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using FluentValidation;

namespace ERP.Application.Purchase;

/// <summary>Cancels a posted purchase, putting back everything posting it moved.</summary>
/// <param name="PurchaseInvoiceId">The document.</param>
/// <param name="Reason">Why. Required, and kept on the document.</param>
/// <remarks>
/// For a purchase that should never have been entered - the supplier's invoice keyed
/// twice, or against the wrong supplier. Goods the firm has accepted and is sending back
/// go on a purchase return instead, which is a document of its own: the difference is
/// whether anything really happened, and a stock ledger that cannot tell the two apart is
/// one nobody can reconcile against a shelf.
/// </remarks>
public sealed record CancelPurchaseInvoiceCommand(Guid PurchaseInvoiceId, string Reason)
    : ICommand, ITransactional;

/// <summary>Validates <see cref="CancelPurchaseInvoiceCommand"/>.</summary>
public sealed class CancelPurchaseInvoiceCommandValidator
    : AbstractValidator<CancelPurchaseInvoiceCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelPurchaseInvoiceCommandValidator"/> class.</summary>
    public CancelPurchaseInvoiceCommandValidator()
    {
        RuleFor(c => c.PurchaseInvoiceId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("A reason is required when cancelling a purchase.")
            .MaximumLength(500);
    }
}

/// <summary>Handles <see cref="CancelPurchaseInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// The mirror of posting, and the same rule: one transaction, or nothing. A purchase whose
/// goods came off the shelf but whose bill stayed outstanding would be a debt for stock the
/// firm no longer holds.
/// </para>
/// <para>
/// It can fail where a sale's cancellation cannot, and the reason is worth knowing: taking
/// a receipt back removes goods from a shelf, and if they have since been sold or issued
/// there is nothing left to remove. The stock document's own reversal refuses that, which
/// is the right answer - what the firm has is a purchase whose goods are gone, and that is
/// a return or a write-off rather than a cancellation.
/// </para>
/// <para>
/// Both journals are cancelled rather than reversed by contras, as on the sales side: a
/// voucher's own cancellation keeps its number and its lines and takes it out of the
/// balances, and a contra would say the same thing twice.
/// </para>
/// </remarks>
public sealed class CancelPurchaseInvoiceCommandHandler
    : ICommandHandler<CancelPurchaseInvoiceCommand>
{
    private readonly IPurchaseInvoiceRepository _invoices;
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

    /// <summary>Initialises a new instance of the <see cref="CancelPurchaseInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The purchase invoice repository.</param>
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
    public CancelPurchaseInvoiceCommandHandler(
        IPurchaseInvoiceRepository invoices,
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
        CancelPurchaseInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "PurchaseInvoice.NoFirmSelected",
                "A firm must be selected to cancel a purchase."));
        }

        PurchaseInvoice? invoice = await _invoices.FindAsync(
            PurchaseInvoiceId.From(request.PurchaseInvoiceId), cancellationToken);

        if (invoice is null || invoice.FirmId != firmId)
        {
            return Result.Failure(Error.NotFound(
                "PurchaseInvoice.NotFound",
                "That purchase does not exist in the selected firm."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        // Every refusal has its say before anything is touched. The transaction would roll
        // back a half-done cancellation anyway, but an aggregate left cancelled in memory
        // after a refusal is a trap for whatever reads it next - and the check that
        // matters most here, whether the firm has already paid, is a read.
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
                + "against it and cannot be withdrawn. Raise a purchase return instead, "
                + "which leaves the payment where it was made."));
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

        Result released = await ReleaseDebitAsync(invoice, cancellationToken);

        if (released.IsFailure)
        {
            return released;
        }

        Result taken = await TakeGoodsBackAsync(invoice, firm, request.Reason, cancellationToken);

        if (taken.IsFailure)
        {
            return taken;
        }

        Result reversed = await CancelJournalAsync(invoice, request.Reason, cancellationToken);

        if (reversed.IsFailure)
        {
            return reversed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Finds the debt the purchase raised, if it raised one.</summary>
    private async Task<Result<Bill?>> FindBillAsync(
        PurchaseInvoice invoice,
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
                "PurchaseInvoice.BillMissing",
                $"The bill purchase '{invoice.Number}' raised no longer exists."));
    }

    /// <summary>Puts back what a cancelled return had set against the purchase's bill.</summary>
    /// <remarks>
    /// A return raises no bill of its own - it allocates a debit against the bill of the
    /// purchase it names. Cancelling the journal takes the supplier's ledger back, but the
    /// allocation is a fact about a bill and nothing else removes it: left behind, the
    /// purchase would read as settled by a debit note that no longer exists, and the
    /// creditors report would understate what the firm owes.
    /// </remarks>
    private async Task<Result> ReleaseDebitAsync(
        PurchaseInvoice invoice,
        CancellationToken cancellationToken)
    {
        if (!invoice.IsReturn
            || invoice.JournalVoucherId is not { } journalId
            || invoice.ReturnsInvoiceId is not { } originalId)
        {
            return Result.Success();
        }

        PurchaseInvoice? original = await _invoices.FindAsync(originalId, cancellationToken);

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

    /// <summary>Takes the goods back off the shelf, at what they arrived at.</summary>
    /// <remarks>
    /// Through the stock document's own cancellation, so the reversal is valued at the
    /// cost each movement was made at rather than at today's average - which is what keeps
    /// a cancellation from quietly restating the value of everything else on the shelf.
    /// The receipt's journal goes with it.
    /// </remarks>
    private async Task<Result> TakeGoodsBackAsync(
        PurchaseInvoice invoice,
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
                "PurchaseInvoice.ReceiptMissing",
                $"The receipt purchase '{invoice.Number}' raised no longer exists."));
        }

        Result cancelled = document.Cancel($"Purchase {invoice.Number} cancelled: {reason}");

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

    /// <summary>Takes the purchase back out of the nominal ledger.</summary>
    private async Task<Result> CancelJournalAsync(
        PurchaseInvoice invoice,
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
                "PurchaseInvoice.JournalMissing",
                $"The journal purchase '{invoice.Number}' raised no longer exists."));
        }

        return journal.Status == VoucherStatus.Cancelled
            ? Result.Success()
            : journal.Cancel($"Purchase {invoice.Number} cancelled: {reason}");
    }
}
