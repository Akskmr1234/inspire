using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Which corrections a history lists.</summary>
public enum StockHistoryKind
{
    /// <summary>Counts and adjustments: what the system held, what was found, and the difference.</summary>
    Adjustments = 1,

    /// <summary>Damaged stock written off.</summary>
    Damaged = 2,
}

/// <summary>One product on one correction, with the stock either side of it.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">Its date.</param>
/// <param name="Type">Whether it was a count, an adjustment or a write-off.</param>
/// <param name="Status">Whether it still stands or was cancelled.</param>
/// <param name="WarehouseName">Where.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="StockUnitCode">The unit the quantities are in.</param>
/// <param name="BatchNumber">The batch, where the product is tracked in batches.</param>
/// <param name="PreviousQuantity">What the system held before.</param>
/// <param name="ActualQuantity">What it held after: on a count, what was found.</param>
/// <param name="AdjustedQuantity">The difference, up or down.</param>
/// <param name="Value">What the difference was worth, signed.</param>
/// <param name="Reason">The line's remark, or else the document's narration.</param>
public sealed record StockHistoryRow(
    Guid DocumentId,
    string Number,
    DateOnly Date,
    StockDocumentType Type,
    StockDocumentStatus Status,
    string WarehouseName,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string StockUnitCode,
    string? BatchNumber,
    decimal PreviousQuantity,
    decimal ActualQuantity,
    decimal AdjustedQuantity,
    decimal Value,
    string? Reason);

/// <summary>Lists the corrections made to stock, line by line.</summary>
/// <param name="Kind">Counts and adjustments, or damaged stock.</param>
/// <param name="From">The earliest document date.</param>
/// <param name="To">The latest document date.</param>
/// <param name="WarehouseId">One warehouse, or null for all.</param>
public sealed record ListStockHistoryQuery(
    StockHistoryKind Kind,
    DateOnly From,
    DateOnly To,
    Guid? WarehouseId = null) : IQuery<IReadOnlyList<StockHistoryRow>>;

/// <summary>Reads the history of stock corrections.</summary>
public interface IStockHistoryReader
{
    /// <summary>Lists the lines of the corrections in a date range.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="types">The document types to include.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lines, newest document first.</returns>
    Task<IReadOnlyList<StockHistoryRow>> ListAsync(
        FirmId firmId,
        IReadOnlyCollection<StockDocumentType> types,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="ListStockHistoryQuery"/>.</summary>
public sealed class ListStockHistoryQueryHandler
    : IQueryHandler<ListStockHistoryQuery, IReadOnlyList<StockHistoryRow>>
{
    private readonly IStockHistoryReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListStockHistoryQueryHandler"/> class.</summary>
    /// <param name="reader">The history reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListStockHistoryQueryHandler(IStockHistoryReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockHistoryRow>>> Handle(
        ListStockHistoryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<StockHistoryRow>>(Error.Forbidden(
                "StockDocument.NoFirmSelected", "A firm must be selected to read stock history."));
        }

        if (request.To < request.From)
        {
            return Result.Failure<IReadOnlyList<StockHistoryRow>>(Error.Validation(
                "StockDocument.RangeInverted", "The end of the range falls before its start."));
        }

        StockDocumentType[] types = request.Kind == StockHistoryKind.Damaged
            ? [StockDocumentType.DamagedStock]
            : [StockDocumentType.PhysicalVerification, StockDocumentType.StockAdjustment];

        return Result.Success(await _reader.ListAsync(
            firmId,
            types,
            request.From,
            request.To,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            cancellationToken));
    }
}
