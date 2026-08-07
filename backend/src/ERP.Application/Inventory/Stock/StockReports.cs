using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>What is on hand, and what it is worth.</summary>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="CategoryId">One category, or null for every one.</param>
/// <param name="IncludeZero">
/// Whether to include products the firm no longer holds any of. Off by default: a
/// valuation is read for what it totals, and rows worth nothing are noise in it.
/// </param>
/// <remarks>
/// Read from the positions rather than by summing the ledger. The positions are the
/// running answer, maintained on every movement; summing several years of ledger to
/// reproduce it would be slower and could only ever agree.
/// </remarks>
public sealed record StockValuationQuery(
    Guid? WarehouseId = null,
    Guid? CategoryId = null,
    bool IncludeZero = false) : IQuery<StockValuationReport>;

/// <summary>One product's position in one warehouse.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="CategoryName">The category it reports under.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="WarehouseName">Its name.</param>
/// <param name="StockUnitCode">The unit the quantity is in.</param>
/// <param name="Quantity">How much is on hand.</param>
/// <param name="AverageCost">The weighted average cost of one unit.</param>
/// <param name="Value">What the quantity on hand is worth.</param>
/// <param name="ReorderLevel">The level at which more is ordered.</param>
/// <param name="IsBelowReorderLevel">Whether the position has fallen to it.</param>
public sealed record StockValuationRow(
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    string CategoryName,
    Guid WarehouseId,
    string WarehouseName,
    string StockUnitCode,
    decimal Quantity,
    decimal AverageCost,
    decimal Value,
    decimal ReorderLevel,
    bool IsBelowReorderLevel);

/// <summary>The stock valuation.</summary>
/// <param name="Currency">The currency values are stated in.</param>
/// <param name="Rows">The positions, by product then warehouse.</param>
/// <param name="TotalValue">What the whole of it is worth.</param>
public sealed record StockValuationReport(
    string Currency,
    IReadOnlyList<StockValuationRow> Rows,
    decimal TotalValue);

/// <summary>The movements of one product, in order, with the running position.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="From">The earliest date.</param>
/// <param name="To">The latest date.</param>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <remarks>
/// One product at a time, deliberately. A stock ledger across every product is a
/// stock ledger nobody reads: the running balance column only means anything within
/// one product and one warehouse, and a report that mixes them shows a column of
/// numbers that appear to jump about.
/// </remarks>
public sealed record StockLedgerQuery(
    Guid ProductId,
    DateOnly From,
    DateOnly To,
    Guid? WarehouseId = null) : IQuery<StockLedgerReport>;

/// <summary>One movement, as the stock ledger shows it.</summary>
/// <param name="Date">The document date.</param>
/// <param name="DocumentId">The document.</param>
/// <param name="DocumentType">The kind of operation.</param>
/// <param name="DocumentNumber">Its number.</param>
/// <param name="WarehouseName">Where the goods moved.</param>
/// <param name="QuantityIn">How much came in, or zero.</param>
/// <param name="QuantityOut">How much went out, or zero.</param>
/// <param name="UnitCost">What one unit was valued at.</param>
/// <param name="Value">The signed value of the movement.</param>
/// <param name="BalanceQuantity">What was on hand afterwards.</param>
/// <param name="BalanceAverageCost">The average cost afterwards.</param>
/// <param name="Narration">What was written on it.</param>
public sealed record StockLedgerRow(
    DateOnly Date,
    Guid DocumentId,
    StockDocumentType DocumentType,
    string DocumentNumber,
    string WarehouseName,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal UnitCost,
    decimal Value,
    decimal BalanceQuantity,
    decimal BalanceAverageCost,
    string? Narration);

/// <summary>The stock ledger of one product.</summary>
/// <param name="ProductCode">The product's code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="StockUnitCode">The unit quantities are in.</param>
/// <param name="Currency">The currency values are in.</param>
/// <param name="OpeningQuantity">
/// What was on hand when the range opened, from the movement before it.
/// </param>
/// <param name="Rows">The movements in the range, oldest first.</param>
/// <param name="ClosingQuantity">What was on hand when it closed.</param>
/// <param name="TotalIn">The quantity that came in during the range.</param>
/// <param name="TotalOut">The quantity that went out.</param>
public sealed record StockLedgerReport(
    string ProductCode,
    string ProductDescription,
    string StockUnitCode,
    string Currency,
    decimal OpeningQuantity,
    IReadOnlyList<StockLedgerRow> Rows,
    decimal ClosingQuantity,
    decimal TotalIn,
    decimal TotalOut);

/// <summary>What moved, and how much of it, over a period.</summary>
/// <param name="From">The earliest date.</param>
/// <param name="To">The latest date.</param>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="CategoryId">One category, or null for every one.</param>
/// <remarks>
/// The specification's Item Movement report. Answers which products turn over and
/// which sit still, which is the question behind the movement classification on the
/// product master - and the reason that field is worth setting from a report rather
/// than from memory.
/// </remarks>
public sealed record ItemMovementQuery(
    DateOnly From,
    DateOnly To,
    Guid? WarehouseId = null,
    Guid? CategoryId = null) : IQuery<IReadOnlyList<ItemMovementRow>>;

/// <summary>One product's movement over a period.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="CategoryName">The category it reports under.</param>
/// <param name="StockUnitCode">The unit quantities are in.</param>
/// <param name="QuantityIn">How much came in.</param>
/// <param name="QuantityOut">How much went out.</param>
/// <param name="ValueIn">What came in was worth.</param>
/// <param name="ValueOut">What went out was worth.</param>
/// <param name="Movements">How many times it moved.</param>
/// <param name="LastMovedOn">When it last moved.</param>
public sealed record ItemMovementRow(
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    string CategoryName,
    string StockUnitCode,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal ValueIn,
    decimal ValueOut,
    int Movements,
    DateOnly? LastMovedOn);

/// <summary>Reads the stock reports.</summary>
public interface IStockReportReader
{
    /// <summary>Reads the positions, valued at their average cost.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="categoryId">One category, or null for all.</param>
    /// <param name="includeZero">Whether to include emptied positions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The valuation.</returns>
    Task<StockValuationReport> ValuationAsync(
        FirmId firmId,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        bool includeZero,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one product's movements, with the position each left behind.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledger, or <see langword="null"/> when the product is not there.</returns>
    Task<StockLedgerReport?> LedgerAsync(
        FirmId firmId,
        ProductId productId,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what moved over a period.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="categoryId">One category, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movement of each product that moved.</returns>
    Task<IReadOnlyList<ItemMovementRow>> MovementAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles the three stock reports.</summary>
public sealed class StockReportQueryHandler
    : IQueryHandler<StockValuationQuery, StockValuationReport>,
      IQueryHandler<StockLedgerQuery, StockLedgerReport>,
      IQueryHandler<ItemMovementQuery, IReadOnlyList<ItemMovementRow>>
{
    private readonly IStockReportReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="StockReportQueryHandler"/> class.</summary>
    /// <param name="reader">The stock report reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public StockReportQueryHandler(IStockReportReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<StockValuationReport>> Handle(
        StockValuationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockValuationReport>(NoFirm);
        }

        return Result.Success(await _reader.ValuationAsync(
            firmId,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.CategoryId is { } category ? CategoryId.From(category) : null,
            request.IncludeZero,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<StockLedgerReport>> Handle(
        StockLedgerQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockLedgerReport>(NoFirm);
        }

        if (request.To < request.From)
        {
            return Result.Failure<StockLedgerReport>(InvertedRange);
        }

        StockLedgerReport? report = await _reader.LedgerAsync(
            firmId,
            ProductId.From(request.ProductId),
            request.From,
            request.To,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            cancellationToken);

        return report is null
            ? Result.Failure<StockLedgerReport>(Error.NotFound(
                "Product.NotFound", "No such product in the selected firm."))
            : Result.Success(report);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ItemMovementRow>>> Handle(
        ItemMovementQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<ItemMovementRow>>(NoFirm);
        }

        if (request.To < request.From)
        {
            return Result.Failure<IReadOnlyList<ItemMovementRow>>(InvertedRange);
        }

        return Result.Success(await _reader.MovementAsync(
            firmId,
            request.From,
            request.To,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.CategoryId is { } category ? CategoryId.From(category) : null,
            cancellationToken));
    }

    private static Error NoFirm => Error.Forbidden(
        "Stock.NoFirmSelected", "A firm must be selected to read stock.");

    private static Error InvertedRange => Error.Validation(
        "Stock.RangeInverted", "The end of the range falls before its start.");
}
