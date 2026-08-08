using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Stock;

// -------------------------------------------------------------------------- writing

/// <summary>Corrects the dates recorded against a batch.</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="ManufacturedOn">When the goods were produced, or null to clear it.</param>
/// <param name="ExpiresOn">When they expire, or null to clear it.</param>
/// <remarks>
/// Its own operation rather than a field on every document that touches the batch. An
/// expiry date is transcribed off a carton once and read by everything afterwards, so
/// a line of an unrelated issue quietly restating it would change the shelf life of
/// goods it had nothing to do with.
/// </remarks>
public sealed record CorrectBatchDatesCommand(
    Guid BatchId,
    DateOnly? ManufacturedOn,
    DateOnly? ExpiresOn) : ICommand;

/// <summary>Validates a <see cref="CorrectBatchDatesCommand"/>.</summary>
public sealed class CorrectBatchDatesCommandValidator
    : AbstractValidator<CorrectBatchDatesCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CorrectBatchDatesCommandValidator"/> class.</summary>
    public CorrectBatchDatesCommandValidator() =>
        RuleFor(command => command.BatchId).NotEqual(Guid.Empty);
}

// -------------------------------------------------------------------------- reading

/// <summary>The batches of one product that can still be picked from.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="IncludeEmpty">
/// Whether to include batches nothing is left of. Off by default: this answers "which
/// batch do I sell from", and a batch with nothing in it is not an answer.
/// </param>
/// <remarks>
/// What section 10 means by selection on sale: the batches in stock with the quantity
/// available and what each was bought at, so somebody can choose between them - and so
/// the screen can choose for them when there is only one.
/// </remarks>
public sealed record ListProductBatchesQuery(
    Guid ProductId,
    Guid? WarehouseId = null,
    bool IncludeEmpty = false) : IQuery<IReadOnlyList<BatchStockRow>>;

/// <summary>What is held of one batch in one warehouse.</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="BatchNumber">Its number.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="StockUnitCode">The unit the quantity is in.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="WarehouseName">Its name.</param>
/// <param name="Quantity">How much is on hand.</param>
/// <param name="UnitCost">What this warehouse carries one unit at.</param>
/// <param name="Value">What the quantity on hand is worth.</param>
/// <param name="PurchaseRate">What the batch was bought at.</param>
/// <param name="ManufacturedOn">When it was produced.</param>
/// <param name="ExpiresOn">When it expires.</param>
/// <param name="DaysToExpiry">
/// How many days are left, negative once past. Null where the batch never expires.
/// </param>
public sealed record BatchStockRow(
    Guid BatchId,
    string BatchNumber,
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    string StockUnitCode,
    Guid WarehouseId,
    string WarehouseName,
    decimal Quantity,
    decimal UnitCost,
    decimal Value,
    decimal PurchaseRate,
    DateOnly? ManufacturedOn,
    DateOnly? ExpiresOn,
    int? DaysToExpiry);

/// <summary>The batch-wise stock report of section 8.3.</summary>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="ProductId">One product, or null for every one.</param>
/// <param name="CategoryId">One category, or null for every one.</param>
/// <param name="IncludeZero">Whether to include batches nothing is left of.</param>
public sealed record BatchStockQuery(
    Guid? WarehouseId = null,
    Guid? ProductId = null,
    Guid? CategoryId = null,
    bool IncludeZero = false) : IQuery<BatchStockReport>;

/// <summary>The batch-wise stock.</summary>
/// <param name="Currency">The currency values are stated in.</param>
/// <param name="Rows">The positions, by product then batch then warehouse.</param>
/// <param name="TotalValue">What the whole of it is worth.</param>
/// <remarks>
/// Its total is the stock valuation's total for the same products, because every
/// batch movement moves the product position by the same quantity at the same cost.
/// Two reports that could disagree about what a shelf is worth would be worse than
/// one report.
/// </remarks>
public sealed record BatchStockReport(
    string Currency,
    IReadOnlyList<BatchStockRow> Rows,
    decimal TotalValue);

/// <summary>The expiry report of section 8.3.</summary>
/// <param name="AsOn">The date to judge expiry against.</param>
/// <param name="WithinDays">
/// How far ahead to look. Null reports only what has already expired; 90 reports that
/// and everything expiring in the next ninety days.
/// </param>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="CategoryId">One category, or null for every one.</param>
/// <remarks>
/// Reads stock rather than batches. A batch that expired last year and sold out in
/// full is not something anybody needs to act on, and listing it would bury the ones
/// that are still on a shelf.
/// </remarks>
public sealed record ExpiryReportQuery(
    DateOnly AsOn,
    int? WithinDays = null,
    Guid? WarehouseId = null,
    Guid? CategoryId = null) : IQuery<IReadOnlyList<BatchStockRow>>;

/// <summary>Reads batches and what is held of them.</summary>
public interface IBatchReader
{
    /// <summary>Reads the batches of one product that are in stock.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="includeEmpty">Whether to include emptied batches.</param>
    /// <param name="asOn">The date expiry is judged against.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batches, soonest to expire first.</returns>
    Task<IReadOnlyList<BatchStockRow>> ForProductAsync(
        FirmId firmId,
        ProductId productId,
        WarehouseId? warehouseId,
        bool includeEmpty,
        DateOnly asOn,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the batch-wise stock.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="productId">One product, or null for all.</param>
    /// <param name="categoryId">One category, or null for all.</param>
    /// <param name="includeZero">Whether to include emptied batches.</param>
    /// <param name="asOn">The date expiry is judged against.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batch-wise stock.</returns>
    Task<BatchStockReport> StockAsync(
        FirmId firmId,
        WarehouseId? warehouseId,
        ProductId? productId,
        CategoryId? categoryId,
        bool includeZero,
        DateOnly asOn,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what has expired, and what is about to.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="asOn">The date to judge expiry against.</param>
    /// <param name="withinDays">How far ahead to look, or null for expired only.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="categoryId">One category, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batches still on a shelf, soonest to expire first.</returns>
    Task<IReadOnlyList<BatchStockRow>> ExpiringAsync(
        FirmId firmId,
        DateOnly asOn,
        int? withinDays,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        CancellationToken cancellationToken = default);
}
