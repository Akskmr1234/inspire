using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Sales;

/// <summary>Handles <see cref="PostSalesInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// The step that makes a sale real, and the only place in the system where four
/// aggregates move together: the invoice posts, an issue takes the goods off the shelf,
/// a bill puts the debt into the customer's outstanding, and a journal states all of it
/// in the nominal ledger. One transaction, because any subset of those four is a
/// discrepancy somebody would have to find by hand.
/// </para>
/// <para>
/// The stock goes out through an ordinary issue rather than by reaching into the
/// positions. Everything a stock document already enforces - average costing, batch
/// positions, serial transitions, refusing to go below zero - therefore applies to a sale
/// unchanged, and the refusal a customer's shortfall produces is the same refusal a
/// department's would be.
/// </para>
/// </remarks>
public sealed class PostSalesInvoiceCommandHandler
    : ICommandHandler<PostSalesInvoiceCommand, PostSalesInvoiceResponse>
{
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IStockDocumentRepository _documents;
    private readonly IStockLedgerRepository _stockLedger;
    private readonly IStockBalanceRepository _balances;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IInventoryAccountMapRepository _accounts;
    private readonly ITaxAccountMapRepository _taxAccounts;
    private readonly ILedgerRepository _ledgers;
    private readonly IBillRepository _bills;
    private readonly IVoucherRepository _vouchers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;
    private readonly StockJournalPoster _stockJournal;

    /// <summary>Initialises a new instance of the <see cref="PostSalesInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="accounts">The inventory account map repository.</param>
    /// <param name="taxAccounts">The tax account map repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PostSalesInvoiceCommandHandler(
        ISalesInvoiceRepository invoices,
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IInventoryAccountMapRepository accounts,
        ITaxAccountMapRepository taxAccounts,
        ILedgerRepository ledgers,
        IBillRepository bills,
        IVoucherRepository vouchers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _documents = documents;
        _stockLedger = ledger;
        _balances = balances;
        _masters = masters;
        _products = products;
        _batches = batches;
        _serials = serials;
        _accounts = accounts;
        _taxAccounts = taxAccounts;
        _ledgers = ledgers;
        _bills = bills;
        _vouchers = vouchers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, ledger);
        _stockJournal = new StockJournalPoster(accounts, vouchers, numbering, financialYears);
    }

    /// <inheritdoc />
    public async Task<Result<PostSalesInvoiceResponse>> Handle(
        PostSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<PostSalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before posting an invoice."));
        }

        Result<SalesInvoice> found = await ResolveAsync(request, firmId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(found.Error);
        }

        SalesInvoice invoice = found.Value;

        Result<Context> loaded = await LoadAsync(invoice, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(loaded.Error);
        }

        // The invoice's own gate first. It is the cheapest of the four checks and the one
        // whose failure means nothing should have been attempted at all.
        Result posted = invoice.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(posted.Error);
        }

        Result<StockDocument> issued = await IssueAsync(
            invoice, loaded.Value, branchId, cancellationToken);

        if (issued.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(issued.Error);
        }

        Result<Voucher> journalled = await JournalAsync(
            invoice, loaded.Value, branchId, cancellationToken);

        if (journalled.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(journalled.Error);
        }

        Result<Bill?> billed = await SettleAsync(
            invoice, loaded.Value, journalled.Value, request.CreditDays, cancellationToken);

        if (billed.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(billed.Error);
        }

        Result recorded = invoice.RecordPosting(
            issued.Value.Id, billed.Value?.Id, journalled.Value.Id);

        if (recorded.IsFailure)
        {
            return Result.Failure<PostSalesInvoiceResponse>(recorded.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PostSalesInvoiceResponse(
            invoice.Id.Value,
            invoice.Number,
            issued.Value.Id.Value,
            issued.Value.Number,
            billed.Value?.Id.Value,
            journalled.Value.Id.Value,
            invoice.Total.Amount));
    }

    /// <summary>Finds the invoice, and refuses one belonging to another firm.</summary>
    /// <remarks>
    /// The firm is checked rather than trusted to the query filter. Tenant isolation is
    /// enforced in two places already, but a firm is a division within a tenant and
    /// nothing in the database stops one firm's user naming another's invoice.
    /// </remarks>
    private async Task<Result<SalesInvoice>> ResolveAsync(
        PostSalesInvoiceCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        SalesInvoice? invoice = await _invoices.FindAsync(
            SalesInvoiceId.From(request.SalesInvoiceId), cancellationToken);

        return invoice is null || invoice.FirmId != firmId
            ? Result.Failure<SalesInvoice>(Error.NotFound(
                "SalesInvoice.NotFound", "That invoice does not exist in the selected firm."))
            : Result.Success(invoice);
    }

    /// <summary>Loads everything the four movements will need, before any of them runs.</summary>
    private async Task<Result<Context>> LoadAsync(
        SalesInvoice invoice,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, invoice.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {invoice.Date:yyyy-MM-dd}."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            invoice.WarehouseId, cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.WarehouseNotFound",
                "The warehouse this invoice ships from is not in the selected firm."));
        }

        Ledger? customer = await _ledgers.FindAsync(invoice.CustomerLedgerId, cancellationToken);

        if (customer is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.CustomerNotFound", "The customer account no longer exists."));
        }

        InventoryAccountMap? accounts = await _accounts.FindAsync(firmId, cancellationToken);

        if (accounts is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "InventoryAccounts.NotConfigured",
                "This firm has not chosen which accounts stock and sales post to."));
        }

        TaxAccountMap? taxAccounts = await _taxAccounts.FindAsync(firmId, cancellationToken);

        if (taxAccounts is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "TaxAccounts.NotConfigured",
                "This firm has not chosen which accounts its tax heads post to."));
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. invoice.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await _masters.GetUnitsAsync(
                [
                    .. invoice.Lines.Select(line => line.UnitId)
                        .Concat(products.Values.Select(product => product.StockUnitId))
                        .Distinct(),
                ],
                cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [
                .. invoice.Lines.Where(line => line.BatchId is not null)
                    .Select(line => line.BatchId!.Value).Distinct(),
            ],
            cancellationToken);

        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials = await _serials.GetManyAsync(
            [
                .. invoice.Lines.SelectMany(line => line.Serials)
                    .Select(serial => serial.SerialNumberId).Distinct(),
            ],
            cancellationToken);

        return Result.Success(new Context(
            firm, year, warehouse, customer, accounts, taxAccounts, products, units, batches,
            serials));
    }

    /// <summary>Raises the issue that takes the goods off the shelf, and posts it.</summary>
    /// <remarks>
    /// Numbered from a series of its own so a stock ledger distinguishes goods that went
    /// to a customer from goods that went to a department. Its journal - inventory
    /// credited, cost of goods sold debited - is raised by the same poster every other
    /// stock document uses, which is why this handler never mentions the cost of a sale.
    /// </remarks>
    private async Task<Result<StockDocument>> IssueAsync(
        SalesInvoice invoice,
        Context context,
        BranchId branchId,
        CancellationToken cancellationToken)
    {
        Result<string> number = await ReserveIssueNumberAsync(
            invoice, branchId, context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<StockDocument>(number.Error);
        }

        Result<IReadOnlyDictionary<ProductId, decimal>> costs = await ReturnCostsAsync(
            invoice, cancellationToken);

        if (costs.IsFailure)
        {
            return Result.Failure<StockDocument>(costs.Error);
        }

        Result<StockDocument> built = SalesIssue.Build(
            invoice, context.Warehouse, context.Products, context.Units, context.Batches,
            context.Serials, context.Year, number.Value, costs.Value);

        if (built.IsFailure)
        {
            return built;
        }

        StockDocument document = built.Value;

        Result posted = document.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<StockDocument>(posted.Error);
        }

        Result<IReadOnlyList<StockLedgerEntry>> applied = await _poster.ApplyAsync(
            document, context.Products, context.Batches, context.Serials,
            context.Firm.BaseCurrency, _clock.UtcNow, cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<StockDocument>(applied.Error);
        }

        Result journalled = await _stockJournal.RaiseAsync(
            document, applied.Value, context.Firm, branchId, _currentUser.UserId,
            _clock.UtcNow, cancellationToken);

        if (journalled.IsFailure)
        {
            return Result.Failure<StockDocument>(journalled.Error);
        }

        _documents.Add(document);

        return Result.Success(document);
    }

    /// <summary>Raises and posts the journal the sale owes the nominal ledger.</summary>
    private async Task<Result<Voucher>> JournalAsync(
        SalesInvoice invoice,
        Context context,
        BranchId branchId,
        CancellationToken cancellationToken)
    {
        Result<string> number = await JournalNumbering.ReserveAsync(
            _numbering, invoice.TenantId, invoice.FirmId, branchId, context.Year,
            cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<Voucher>(number.Error);
        }

        Result<Voucher> raised = SalesJournal.Raise(
            invoice, context.Accounts, context.TaxAccounts, context.Firm, context.Year,
            number.Value);

        if (raised.IsFailure)
        {
            return raised;
        }

        Result posted = raised.Value.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<Voucher>(posted.Error);
        }

        _vouchers.Add(raised.Value);

        return raised;
    }

    /// <summary>Works out what returned goods come back onto the shelf at.</summary>
    /// <remarks>
    /// The business's answer of 2026-08-12, and it has two halves because the question
    /// has two answers. Where the return names the invoice it is against, the goods come
    /// back at the cost they actually left at, read from that sale's own movements - so
    /// the cost-of-goods-sold reversal matches the original to the fils, however far the
    /// average has moved since. Where it names none there is no such cost, and the
    /// current average is used: receiving at the average cannot move the average, so the
    /// rest of the shelf is left alone.
    /// <para>
    /// A product the original sale did not carry falls back to the average too. It is a
    /// return of something that invoice never sold, which is a mistake worth allowing and
    /// reporting rather than a reason to refuse goods somebody is holding.
    /// </para>
    /// </remarks>
    private async Task<Result<IReadOnlyDictionary<ProductId, decimal>>> ReturnCostsAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        Dictionary<ProductId, decimal> costs = [];

        if (!invoice.IsReturn)
        {
            return Result.Success<IReadOnlyDictionary<ProductId, decimal>>(costs);
        }

        List<ProductId> productIds = [.. invoice.Lines.Select(line => line.ProductId).Distinct()];

        if (invoice.ReturnsInvoiceId is { } originalId)
        {
            SalesInvoice? original = await _invoices.FindAsync(originalId, cancellationToken);

            if (original is null || original.FirmId != invoice.FirmId)
            {
                return Result.Failure<IReadOnlyDictionary<ProductId, decimal>>(Error.NotFound(
                    "SalesReturn.InvoiceNotFound",
                    "The invoice this return is against does not exist in the selected firm."));
            }

            if (original.StockDocumentId is { } issuedOn)
            {
                IReadOnlyList<StockLedgerEntry> movements =
                    await _stockLedger.ForDocumentAsync(issuedOn, cancellationToken);

                foreach (StockLedgerEntry movement in movements)
                {
                    costs[movement.ProductId] = movement.UnitCost;
                }
            }
        }

        List<ProductId> unpriced = [.. productIds.Where(id => !costs.ContainsKey(id))];

        if (unpriced.Count > 0)
        {
            IReadOnlyDictionary<ProductId, StockBalance> positions =
                await _balances.GetPositionsAsync(
                    invoice.FirmId, invoice.WarehouseId, unpriced, cancellationToken);

            foreach (ProductId id in unpriced)
            {
                costs[id] = positions.TryGetValue(id, out StockBalance? position)
                    ? position.AverageCost
                    : 0m;
            }
        }

        return Result.Success<IReadOnlyDictionary<ProductId, decimal>>(costs);
    }

    /// <summary>Puts what the customer owes into their outstanding.</summary>
    /// <remarks>
    /// Against the journal rather than the invoice, because that is what every other bill
    /// in this system points at and what the settlement machinery already understands: a
    /// receipt allocates against bills, and a bill names the voucher that raised it.
    /// <para>
    /// The terms come from the customer's own ledger unless the caller states them. A
    /// figure typed on one invoice is an exception; the ledger is where a firm records
    /// what it agreed with a customer.
    /// </para>
    /// </remarks>
    private async Task<Result<Bill?>> SettleAsync(
        SalesInvoice invoice,
        Context context,
        Voucher journal,
        int? creditDays,
        CancellationToken cancellationToken)
    {
        if (invoice.IsReturn)
        {
            // The allocation happens on the original sale's bill, and the return records
            // none of its own: a bill is something a document raised, and a return
            // raises nothing. Which bill it settled is one hop away through the invoice
            // it names, and recording it here would be a second path to the same fact
            // and a second answer to "which document raised this bill".
            Result credited = await CreditAsync(invoice, journal, cancellationToken);

            return credited.IsFailure
                ? Result.Failure<Bill?>(credited.Error)
                : Result.Success<Bill?>(null);
        }

        Result<Bill> raised = Raise(invoice, context, journal, creditDays);

        return raised.IsFailure
            ? Result.Failure<Bill?>(raised.Error)
            : Result.Success<Bill?>(raised.Value);
    }

    /// <summary>Allocates a return's credit against the bill the sale raised.</summary>
    /// <remarks>
    /// The business's answer of 2026-08-12. Where the return names an invoice, its credit
    /// is allocated against that sale's bill exactly as a receipt would be - which is
    /// what makes the bill-wise reports show the sale as settled by the goods coming
    /// back rather than as still owing.
    /// <para>
    /// Whatever cannot be matched is left as a credit on the customer's account: a return
    /// naming no invoice, or crediting more than the bill still owes because part of it
    /// was already paid. The journal has credited the ledger either way, so the customer's
    /// balance is right immediately; what is missing is only the link to a document, and
    /// refusing the return over that would turn a bookkeeping detail into somebody
    /// standing at a counter with goods nobody will take back.
    /// </para>
    /// </remarks>
    private async Task<Result> CreditAsync(
        SalesInvoice invoice,
        Voucher journal,
        CancellationToken cancellationToken)
    {
        if (invoice.ReturnsInvoiceId is not { } originalId)
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

        if (!found.TryGetValue(billId, out Bill? bill)
            || bill.Status == BillStatus.Cancelled)
        {
            return Result.Success();
        }

        // Capped at what is still owing rather than refused for exceeding it. A return
        // worth more than the bill has left to settle is an ordinary thing once a
        // customer has part-paid, and the excess is a credit on their account.
        Money allocatable = invoice.Total < bill.OutstandingAmount
            ? invoice.Total
            : bill.OutstandingAmount;

        if (!allocatable.IsPositive)
        {
            return Result.Success<Bill?>(bill);
        }

        Result allocated = bill.Allocate(journal.Id, allocatable, invoice.Date);

        return allocated.IsFailure
            ? Result.Failure<Bill?>(allocated.Error)
            : Result.Success<Bill?>(bill);
    }

    /// <summary>Raises the debt a sale creates.</summary>
    private Result<Bill> Raise(
        SalesInvoice invoice,
        Context context,
        Voucher journal,
        int? creditDays)
    {
        Result<Bill> raised = Bill.Raise(
            invoice.TenantId,
            invoice.FirmId,
            invoice.CustomerLedgerId,
            journal.Id,
            BillType.Receivable,
            invoice.Number,
            invoice.Date,
            creditDays ?? context.Customer.CreditDays ?? 0,
            invoice.Total);

        if (raised.IsSuccess)
        {
            _bills.Add(raised.Value);
        }

        return raised;
    }

    /// <summary>Takes the next number for the issue, creating the series if there is none.</summary>
    private async Task<Result<string>> ReserveIssueNumberAsync(
        SalesInvoice invoice,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        StockDocumentType kind = invoice.IsReturn
            ? StockDocumentType.SalesReturn
            : StockDocumentType.SalesIssue;

        string documentType = DocumentTypes.ForStockDocument(kind);

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, invoice.FirmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                invoice.TenantId, invoice.FirmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: invoice.IsReturn ? "SR" : "SI",
                suffix: null,
                separator: "/",
                financialYearLabel: year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>Everything the posting needs, loaded once before any of it moves.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Warehouse Warehouse,
        Ledger Customer,
        InventoryAccountMap Accounts,
        TaxAccountMap TaxAccounts,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<BatchId, Batch> Batches,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> Serials);
}
