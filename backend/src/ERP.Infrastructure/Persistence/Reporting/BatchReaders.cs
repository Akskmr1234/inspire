using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads batches, what is held of them, and what is about to expire.</summary>
/// <remarks>
/// All three questions are the same query with different filters: the batch positions,
/// joined to the batch for its dates and to the product for what it is called. They
/// share one shape rather than three, so a batch cannot appear on the sales screen
/// with one quantity and on the report with another.
/// </remarks>
public sealed class BatchReader : IBatchReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="BatchReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public BatchReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BatchStockRow>> ForProductAsync(
        FirmId firmId,
        ProductId productId,
        WarehouseId? warehouseId,
        bool includeEmpty,
        DateOnly asOn,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BatchBalance> positions = _context.BatchBalances
            .Where(balance => balance.FirmId == firmId && balance.ProductId == productId);

        if (warehouseId is { } warehouse)
        {
            positions = positions.Where(balance => balance.WarehouseId == warehouse);
        }

        if (!includeEmpty)
        {
            positions = positions.Where(balance => balance.Quantity != 0m);
        }

        return await ReadAsync(firmId, positions, null, asOn, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BatchStockReport> StockAsync(
        FirmId firmId,
        WarehouseId? warehouseId,
        ProductId? productId,
        CategoryId? categoryId,
        bool includeZero,
        DateOnly asOn,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BatchBalance> positions = _context.BatchBalances
            .Where(balance => balance.FirmId == firmId);

        if (warehouseId is { } warehouse)
        {
            positions = positions.Where(balance => balance.WarehouseId == warehouse);
        }

        if (productId is { } product)
        {
            positions = positions.Where(balance => balance.ProductId == product);
        }

        if (!includeZero)
        {
            positions = positions.Where(balance => balance.Quantity != 0m);
        }

        IReadOnlyList<BatchStockRow> rows = await ReadAsync(
            firmId, positions, categoryId, asOn, cancellationToken);

        string currency = await CurrencyAsync(firmId, cancellationToken);

        return new BatchStockReport(currency, rows, rows.Sum(row => row.Value));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BatchStockRow>> ExpiringAsync(
        FirmId firmId,
        DateOnly asOn,
        int? withinDays,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        CancellationToken cancellationToken = default)
    {
        // The horizon, inclusive on both ends: everything already past its date, and
        // everything reaching it within the window. A batch with no expiry date is
        // excluded by the join condition rather than by an assumption about blanks.
        DateOnly horizon = asOn.AddDays(withinDays ?? 0);

        List<BatchId> expiring = await _context.Batches
            .Where(batch =>
                batch.FirmId == firmId
                && batch.ExpiresOn != null
                && batch.ExpiresOn <= horizon)
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);

        if (expiring.Count == 0)
        {
            return [];
        }

        // Only what is still on a shelf. A batch that expired last year and sold out
        // in full is not something anybody can act on, and listing it would bury the
        // ones that are still there.
        IQueryable<BatchBalance> positions = _context.BatchBalances
            .Where(balance =>
                balance.FirmId == firmId
                && balance.Quantity > 0m
                && expiring.Contains(balance.BatchId));

        if (warehouseId is { } warehouse)
        {
            positions = positions.Where(balance => balance.WarehouseId == warehouse);
        }

        return await ReadAsync(firmId, positions, categoryId, asOn, cancellationToken);
    }

    private async Task<IReadOnlyList<BatchStockRow>> ReadAsync(
        FirmId firmId,
        IQueryable<BatchBalance> positions,
        CategoryId? categoryId,
        DateOnly asOn,
        CancellationToken cancellationToken)
    {
        var rows = await positions
            .Join(
                _context.Batches,
                balance => balance.BatchId,
                batch => batch.Id,
                (balance, batch) => new { balance, batch })
            .Join(
                _context.Products,
                pair => pair.balance.ProductId,
                product => product.Id,
                (pair, product) => new { pair.balance, pair.batch, product })
            .Where(row => categoryId == null || row.product.CategoryId == categoryId)
            .Select(row => new
            {
                row.balance.BatchId,
                row.batch.Number,
                row.balance.ProductId,
                row.product.Code,
                row.product.Description,
                row.product.StockUnitId,
                row.balance.WarehouseId,
                row.balance.Quantity,
                row.balance.UnitCost,
                row.batch.PurchaseRate,
                row.batch.ManufacturedOn,
                row.batch.ExpiresOn,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        Dictionary<WarehouseId, string> warehouses = await _context.Warehouses
            .Where(warehouse => warehouse.FirmId == firmId)
            .ToDictionaryAsync(
                warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        return
        [
            // Soonest to expire first, and the batches that never expire after all of
            // them. That is the order somebody picking stock wants: the lot that has
            // to move is at the top, and a lot with no expiry date is never urgent.
            .. rows
                .OrderBy(row => row.Code)
                .ThenBy(row => row.ExpiresOn is null)
                .ThenBy(row => row.ExpiresOn)
                .ThenBy(row => row.Number)
                .Select(row => new BatchStockRow(
                    row.BatchId.Value,
                    row.Number,
                    row.ProductId.Value,
                    row.Code,
                    row.Description,
                    units.GetValueOrDefault(row.StockUnitId, string.Empty),
                    row.WarehouseId.Value,
                    warehouses.GetValueOrDefault(row.WarehouseId, string.Empty),
                    row.Quantity,
                    row.UnitCost,
                    decimal.Round(
                        row.Quantity * row.UnitCost, 2, MidpointRounding.AwayFromZero),
                    row.PurchaseRate,
                    row.ManufacturedOn,
                    row.ExpiresOn,
                    row.ExpiresOn is { } expiry ? expiry.DayNumber - asOn.DayNumber : null)),
        ];
    }

    /// <summary>The currency the firm states values in.</summary>
    /// <remarks>
    /// Read from the firm rather than from the rows, so a report with nothing in it
    /// still says what its empty total is denominated in.
    /// </remarks>
    private async Task<string> CurrencyAsync(FirmId firmId, CancellationToken cancellationToken)
    {
        var firm = await _context.Firms
            .Where(row => row.Id == firmId)
            .Select(row => new { row.BaseCurrency })
            .FirstOrDefaultAsync(cancellationToken);

        return firm is null ? string.Empty : firm.BaseCurrency.Code;
    }
}
