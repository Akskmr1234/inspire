using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Settings;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Service;

/// <summary>Handles what a return comes to: an exchange, or a refund.</summary>
/// <remarks>
/// <para>
/// An exchange is one transfer. A customer's faulty unit lands in the return's godown and a
/// good one leaves the shelf for them, which for the books is the same product moving from
/// the shelf to the returns godown; a supplier's replacement is the reverse. Where the return
/// is kept on the shelf itself, nothing moves at all.
/// </para>
/// <para>
/// A refund is a draft credit note to the customer, or debit note to the supplier, priced
/// from the invoice the goods came on where the return names one. Posting it - after
/// somebody has checked it - is what brings the goods back or sends them out and settles
/// the account, exactly as any other return.
/// </para>
/// </remarks>
public sealed class RmaOutcomeHandlers
    : ICommandHandler<ReplaceRmaCommand, RmaOutcomeResponse>,
      ICommandHandler<RefundRmaCommand, RmaOutcomeResponse>
{
    private readonly IRmaRepository _rmas;
    private readonly ISalesInvoiceRepository _sales;
    private readonly IPurchaseInvoiceRepository _purchases;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ITaxSlabRepository _taxSlabs;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStockBalanceRepository _balances;
    private readonly IFirmPolicyReader? _policies;
    private readonly StockDocumentWriter _writer;

    /// <summary>Initialises a new instance of the <see cref="RmaOutcomeHandlers"/> class.</summary>
    /// <param name="rmas">The return repository.</param>
    /// <param name="sales">The sales invoice repository.</param>
    /// <param name="purchases">The purchase invoice repository.</param>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="taxSlabs">The tax rate repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="accounts">The inventory account map repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="policies">The firm's policies, which decide whether stock may go below zero.</param>
    public RmaOutcomeHandlers(
        IRmaRepository rmas,
        ISalesInvoiceRepository sales,
        IPurchaseInvoiceRepository purchases,
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        ITaxSlabRepository taxSlabs,
        ILedgerRepository ledgers,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        IInventoryAccountMapRepository accounts,
        IVoucherRepository vouchers,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork,
        IFirmPolicyReader? policies = null)
    {
        _rmas = rmas;
        _sales = sales;
        _purchases = purchases;
        _masters = masters;
        _products = products;
        _batches = batches;
        _taxSlabs = taxSlabs;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _balances = balances;
        _policies = policies;
        _writer = new StockDocumentWriter(
            documents, masters, products, batches, serials, balances, batchBalances, ledger,
            financialYears, numbering, accounts, vouchers, firms, tenantContext, currentUser,
            clock);
    }

    /// <inheritdoc />
    public async Task<Result<RmaOutcomeResponse>> Handle(
        ReplaceRmaCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<RmaOutcomeResponse>(RmaAccess.NoFirm());
        }

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(found.Error);
        }

        Rma rma = found.Value;

        if (!rma.AwaitsOutcome)
        {
            return Result.Failure<RmaOutcomeResponse>(Error.BusinessRule(
                "Rma.WrongStatus", $"Return '{rma.Number}' is {rma.Status} and cannot be replaced."));
        }

        Warehouse? shelf = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.StockWarehouseId), cancellationToken);

        if (shelf is null || shelf.FirmId != rma.FirmId || shelf.IsTransit)
        {
            return Result.Failure<RmaOutcomeResponse>(Error.NotFound(
                "Rma.GodownNotFound", "No such godown in the selected firm."));
        }

        RmaActor actor = RmaAccess.Actor(_currentUser, _clock);
        StockDocument? transfer = null;

        if (shelf.Id != rma.WarehouseId)
        {
            bool fromCustomer = rma.Direction == RmaDirection.FromCustomer;
            bool allowNegative = _policies is not null
                && (await _policies.GetAsync(rma.FirmId, cancellationToken)).AllowsNegativeStock;

            Result<StockWritten> written = await _writer.WriteAsync(
                new CreateStockDocumentCommand(
                    StockDocumentType.StockTransfer,
                    request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
                    fromCustomer ? shelf.Id.Value : rma.WarehouseId.Value,
                    [
                        .. rma.Lines.OrderBy(line => line.LineNumber).Select(line => new StockDocumentLineInput(
                            line.ProductId.Value, line.Quantity, BatchId: line.BatchId?.Value)),
                    ],
                    fromCustomer ? rma.WarehouseId.Value : shelf.Id.Value,
                    rma.Number,
                    $"Exchange against return {rma.Number}"),
                rma.FirmId,
                branchId,
                allowNegative,
                throughTransferRequest: false,
                cancellationToken);

            if (written.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(written.Error);
            }

            transfer = written.Value.Document;
        }

        Result replaced = rma.Replace(transfer?.Id, actor, request.Note);

        if (replaced.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(replaced.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RmaOutcomeResponse(transfer?.Id.Value, transfer?.Number));
    }

    /// <inheritdoc />
    public async Task<Result<RmaOutcomeResponse>> Handle(
        RefundRmaCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<RmaOutcomeResponse>(RmaAccess.NoFirm());
        }

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(found.Error);
        }

        Rma rma = found.Value;
        RmaActor actor = RmaAccess.Actor(_currentUser, _clock);

        // A refund whose note was cancelled is no refund; the return can be settled again.
        if (await NoteCancelledAsync(rma, cancellationToken))
        {
            Result reopened = rma.ReopenRefund(actor);

            if (reopened.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(reopened.Error);
            }
        }

        if (!rma.AwaitsOutcome)
        {
            return Result.Failure<RmaOutcomeResponse>(Error.BusinessRule(
                "Rma.WrongStatus", $"Return '{rma.Number}' is {rma.Status} and cannot be refunded."));
        }

        Firm? firm = await _firms.FindAsync(rma.FirmId, cancellationToken);
        DateOnly date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        FinancialYear? year = await _financialYears.FindContainingAsync(rma.FirmId, date, cancellationToken);
        Ledger? party = await _ledgers.FindAsync(rma.PartyLedgerId, cancellationToken);
        Warehouse? warehouse = await _masters.FindWarehouseAsync(rma.WarehouseId, cancellationToken);

        if (firm is null || year is null || party is null || warehouse is null)
        {
            return Result.Failure<RmaOutcomeResponse>(Error.BusinessRule(
                "Rma.CannotRefund",
                $"No financial year covers {date:yyyy-MM-dd}, or the return's party or godown no longer exists."));
        }

        Context context = new(
            firm,
            year,
            party,
            warehouse,
            branchId,
            date,
            await _products.GetManyAsync([.. rma.Lines.Select(line => line.ProductId).Distinct()], cancellationToken),
            (await _taxSlabs.ListAsync(rma.FirmId, true, cancellationToken))
                .ToDictionary(slab => slab.Id, slab => slab.Percentage));

        Result<RmaOutcomeResponse> raised = rma.Direction == RmaDirection.FromCustomer
            ? await CreditAsync(rma, context, actor, request.Note, cancellationToken)
            : await DebitAsync(rma, context, actor, request.Note, cancellationToken);

        if (raised.IsFailure)
        {
            return raised;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return raised;
    }

    private static string Narration(Rma rma)
    {
        string text = $"Return {rma.Number}: {rma.Reason}";

        return text.Length <= SalesInvoice.MaximumNarrationLength ? text : text[..SalesInvoice.MaximumNarrationLength];
    }

    private async Task<bool> NoteCancelledAsync(Rma rma, CancellationToken cancellationToken)
    {
        if (rma.CreditNoteId is { } credit)
        {
            return await _sales.FindAsync(credit, cancellationToken) is { Status: SalesInvoiceStatus.Cancelled };
        }

        return rma.DebitNoteId is { } debit
            && await _purchases.FindAsync(debit, cancellationToken) is { Status: PurchaseInvoiceStatus.Cancelled };
    }

    private static Result<TaxRate> TaxRateFor(Product product, Context context) =>
        TaxRate.Create(product.TaxSlabId is { } slab ? context.Rates.GetValueOrDefault(slab) : 0m);

    private async Task<Result<RmaOutcomeResponse>> CreditAsync(
        Rma rma,
        Context context,
        RmaActor actor,
        string? note,
        CancellationToken cancellationToken)
    {
        SalesInvoice? original = rma.SalesInvoiceId is { } saleId
            ? await _sales.FindAsync(saleId, cancellationToken)
            : null;

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [
                .. rma.Lines.Select(line => line.BatchId)
                    .Concat(original?.Lines.Select(line => line.BatchId) ?? [])
                    .OfType<BatchId>()
                    .Distinct(),
            ],
            cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units = await _masters.GetUnitsAsync(
            [.. context.Products.Values.Select(product => product.StockUnitId).Distinct()], cancellationToken);

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.SalesReturn, rma.FirmId, context.BranchId,
            context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(number.Error);
        }

        TaxMode mode = context.Firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            _tenantContext.TenantId, rma.FirmId, context.BranchId, context.Year, number.Value, context.Date,
            context.Party, context.Warehouse, mode, context.Firm.BaseCurrency, SalesDocumentKind.Return,
            original?.Id);

        if (draft.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(draft.Error);
        }

        SalesInvoice credit = draft.Value;

        foreach (RmaLine line in rma.Lines.OrderBy(line => line.LineNumber))
        {
            Product product = context.Products[line.ProductId];

            // What the customer paid for one, before tax, where the sale is known: a refund
            // gives back what was charged, not what the goods sell for today.
            SalesInvoiceLine? sold = original?.Lines
                .Where(candidate => candidate.ProductId == line.ProductId && candidate.StockQuantity > 0m)
                .OrderByDescending(candidate => line.BatchId is not null && candidate.BatchId == line.BatchId)
                .FirstOrDefault();

            decimal rate = sold is null
                ? product.RetailRate.Amount
                : decimal.Round(sold.TaxableAmount.Amount / sold.StockQuantity, 4);

            Result<TaxRate> taxRate = TaxRateFor(product, context);

            if (taxRate.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(taxRate.Error);
            }

            TaxAssessment assessment = TaxCalculator.Calculate(
                Money.Of(line.Quantity * rate, credit.Currency),
                taxRate.Value,
                SalesTaxContext.For(context.Firm, context.Party, mode));

            BatchId? batchId = line.BatchId ?? sold?.BatchId;

            Result<SalesInvoiceLine> added = credit.AddLine(
                product,
                units[product.StockUnitId],
                line.Quantity,
                line.Quantity,
                rate,
                assessment,
                batchId is { } batch ? batches.GetValueOrDefault(batch) : null);

            if (added.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(added.Error);
            }
        }

        Result details = credit.SetDetails(rma.InvoiceReference ?? rma.Number, Narration(rma));

        if (details.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(details.Error);
        }

        Result refunded = rma.Refund(credit.Id, actor, note);

        if (refunded.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(refunded.Error);
        }

        _sales.Add(credit);

        return Result.Success(new RmaOutcomeResponse(credit.Id.Value, credit.Number));
    }

    private async Task<Result<RmaOutcomeResponse>> DebitAsync(
        Rma rma,
        Context context,
        RmaActor actor,
        string? note,
        CancellationToken cancellationToken)
    {
        PurchaseInvoice? original = rma.PurchaseInvoiceId is { } purchaseId
            ? await _purchases.FindAsync(purchaseId, cancellationToken)
            : null;

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [.. rma.Lines.Select(line => line.BatchId).OfType<BatchId>().Distinct()], cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units = await _masters.GetUnitsAsync(
            [.. context.Products.Values.Select(product => product.StockUnitId).Distinct()], cancellationToken);

        // Where the purchase is not known, the goods go back at what they stand in the books
        // at, which is what sending them out takes off the stock.
        IReadOnlyDictionary<ProductId, StockBalance> held = await _balances.GetPositionsAsync(
            rma.FirmId, rma.WarehouseId, [.. context.Products.Keys], cancellationToken);

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.PurchaseReturn, rma.FirmId, context.BranchId,
            context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(number.Error);
        }

        TaxMode mode = context.Firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

        Result<PurchaseInvoice> draft = PurchaseInvoice.CreateDraft(
            _tenantContext.TenantId, rma.FirmId, context.BranchId, context.Year, number.Value, context.Date,
            context.Party, context.Warehouse, mode, context.Firm.BaseCurrency, PurchaseDocumentKind.Return,
            original?.Id);

        if (draft.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(draft.Error);
        }

        PurchaseInvoice debit = draft.Value;

        foreach (RmaLine line in rma.Lines.OrderBy(line => line.LineNumber))
        {
            Product product = context.Products[line.ProductId];
            Batch? batch = line.BatchId is { } batchId ? batches.GetValueOrDefault(batchId) : null;

            // What the firm paid for one, before tax, where the purchase is known.
            PurchaseInvoiceLine? bought = original?.Lines
                .Where(candidate => candidate.ProductId == line.ProductId && candidate.StockQuantity > 0m)
                .OrderByDescending(candidate => batch is not null && candidate.BatchNumber == batch.Number)
                .FirstOrDefault();

            decimal rate = bought is null
                ? decimal.Round(held.GetValueOrDefault(line.ProductId)?.AverageCost ?? product.Rates.Cost, 4)
                : decimal.Round(bought.TaxableAmount.Amount / bought.StockQuantity, 4);

            Result<TaxRate> taxRate = TaxRateFor(product, context);

            if (taxRate.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(taxRate.Error);
            }

            TaxAssessment assessment = TaxCalculator.Calculate(
                Money.Of(line.Quantity * rate, debit.Currency),
                taxRate.Value,
                PurchaseTaxContext.For(context.Firm, context.Party, mode));

            Result<PurchaseInvoiceLine> added = debit.AddLine(
                product,
                units[product.StockUnitId],
                line.Quantity,
                line.Quantity,
                rate,
                assessment,
                batch?.Number ?? bought?.BatchNumber,
                batch?.ExpiresOn ?? bought?.ExpiresOn);

            if (added.IsFailure)
            {
                return Result.Failure<RmaOutcomeResponse>(added.Error);
            }
        }

        Result details = debit.SetSupplierDocument(rma.InvoiceReference ?? rma.Number, null, Narration(rma));

        if (details.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(details.Error);
        }

        Result refunded = rma.Refund(debit.Id, actor, note);

        if (refunded.IsFailure)
        {
            return Result.Failure<RmaOutcomeResponse>(refunded.Error);
        }

        _purchases.Add(debit);

        return Result.Success(new RmaOutcomeResponse(debit.Id.Value, debit.Number));
    }

    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Party,
        Warehouse Warehouse,
        BranchId BranchId,
        DateOnly Date,
        IReadOnlyDictionary<ProductId, Product> Products,
        Dictionary<TaxSlabId, decimal> Rates);
}
