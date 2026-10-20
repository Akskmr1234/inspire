using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>A stock document as written, and what its posting moved.</summary>
/// <param name="Document">The document, added to its repository.</param>
/// <param name="Movements">The stock ledger entries it wrote; none for a draft.</param>
internal sealed record StockWritten(
    StockDocument Document,
    IReadOnlyList<StockLedgerEntry> Movements);

/// <summary>
/// Enters a stock document: loads what it names, numbers it, builds it and, where asked,
/// posts it with its journal.
/// </summary>
/// <remarks>
/// Shared by the stock screen and by the transfer requests, whose dispatch and receipt
/// are ordinary transfers. One path means a dispatched batch or serial number is checked,
/// costed and journalled exactly as on any other transfer, rather than by a second copy
/// of the same rules that would drift.
/// </remarks>
internal sealed class StockDocumentWriter
{
    private readonly IStockDocumentRepository _documents;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IFinancialYearRepository _financialYears;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILedgerRepository? _ledgers;
    private readonly StockPoster _poster;
    private readonly StockJournalPoster _journal;

    internal StockDocumentWriter(
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        IInventoryAccountMapRepository accounts,
        IVoucherRepository vouchers,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        ILedgerRepository? ledgers = null)
    {
        _ledgers = ledgers;
        _documents = documents;
        _masters = masters;
        _products = products;
        _batches = batches;
        _serials = serials;
        _financialYears = financialYears;
        _numbering = numbering;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _poster = new StockPoster(balances, batchBalances, ledger);
        _journal = new StockJournalPoster(accounts, vouchers, numbering, financialYears);
    }

    /// <summary>Writes the document, posting it where the request asks.</summary>
    /// <param name="request">The document as entered.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch its journal belongs to.</param>
    /// <param name="allowNegative">Whether goods may leave a position below zero.</param>
    /// <param name="throughTransferRequest">
    /// Whether a transfer request is moving the goods, which is the only thing allowed to
    /// move them into or out of goods in transit.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document and its movements, or the first refusal.</returns>
    internal async Task<Result<StockWritten>> WriteAsync(
        CreateStockDocumentCommand request,
        FirmId firmId,
        BranchId branchId,
        bool allowNegative,
        bool throughTransferRequest,
        CancellationToken cancellationToken)
    {
        Result<StockContext> context = await StockLoader.LoadAsync(
            request, firmId, _firms, _financialYears, _masters, _products, cancellationToken,
            _ledgers);

        if (context.IsFailure)
        {
            return Result.Failure<StockWritten>(context.Error);
        }

        if (!throughTransferRequest
            && (context.Value.Warehouse.IsTransit || context.Value.Destination?.IsTransit == true))
        {
            return Result.Failure<StockWritten>(Error.BusinessRule(
                "StockDocument.TransitByRequestOnly",
                "Goods in transit move only by dispatching or receiving a transfer request."));
        }

        Result<string> number = await ReserveNumberAsync(
            request.Type, firmId, branchId, context.Value.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<StockWritten>(number.Error);
        }

        Result<StockAssembly> built = await StockLoader.BuildAsync(
            request, _tenantContext.TenantId, firmId, number.Value, context.Value, _batches,
            _serials, cancellationToken);

        if (built.IsFailure)
        {
            return Result.Failure<StockWritten>(built.Error);
        }

        StockDocument document = built.Value.Document;
        IReadOnlyList<StockLedgerEntry> movements = [];

        if (request.PostImmediately)
        {
            Result posted = document.Post(_currentUser.UserId, _clock.UtcNow);

            if (posted.IsFailure)
            {
                return Result.Failure<StockWritten>(posted.Error);
            }

            // The command is transactional, so a line that cannot move rolls the whole
            // document back - including the number it reserved, which is why a refused
            // posting leaves no gap in the sequence.
            Result<IReadOnlyList<StockLedgerEntry>> applied = await _poster.ApplyAsync(
                document,
                context.Value.Products,
                built.Value.Batches,
                built.Value.Serials,
                context.Value.Firm.BaseCurrency,
                _clock.UtcNow,
                cancellationToken,
                allowNegative);

            if (applied.IsFailure)
            {
                return Result.Failure<StockWritten>(applied.Error);
            }

            movements = applied.Value;

            // The journal is part of the posting, not a consequence of it. A document
            // whose accounts nobody has chosen does not post at all, which is the only
            // arrangement under which the stock ledger and the nominal ledger keep
            // describing the same firm.
            Result journalled = await _journal.RaiseAsync(
                document,
                movements,
                context.Value.Firm,
                branchId,
                _currentUser.UserId,
                _clock.UtcNow,
                cancellationToken);

            if (journalled.IsFailure)
            {
                return Result.Failure<StockWritten>(journalled.Error);
            }
        }

        _documents.Add(document);

        return Result.Success(new StockWritten(document, movements));
    }

    /// <summary>
    /// Takes the next number, creating the series if the firm has none configured.
    /// </summary>
    /// <remarks>
    /// Auto-created for the same reason vouchers auto-create theirs: refusing to
    /// record a receipt until somebody visits a settings screen makes a fresh
    /// installation look broken.
    /// </remarks>
    private async Task<Result<string>> ReserveNumberAsync(
        StockDocumentType type,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken) =>
        await ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.ForStockDocument(type), firmId,
            branchId, year, cancellationToken);

    /// <summary>Takes the next number of a series, creating the series where there is none.</summary>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="documentType">The series' document type.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="year">The financial year.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number, or the reason none could be issued.</returns>
    internal static async Task<Result<string>> ReserveAsync(
        INumberingSeriesRepository numbering,
        TenantId tenantId,
        string documentType,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        NumberingSeries? series = await numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                tenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            NumberingDefaults.Apply(series, year.Code);

            numbering.Add(series);
        }

        return series.Reserve();
    }
}
