using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads stock documents for the list and the detail screens.</summary>
public sealed class StockDocumentReader : IStockDocumentReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockDocumentReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockDocumentReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockDocumentSummary>> ListAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        StockDocumentType? type,
        WarehouseId? warehouseId,
        StockDocumentStatus? status,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockDocument> documents = _context.StockDocuments
            .Where(document =>
                document.FirmId == firmId && document.Date >= from && document.Date <= to);

        if (type is { } kind)
        {
            documents = documents.Where(document => document.Type == kind);
        }

        if (warehouseId is { } warehouse)
        {
            // Either end of a transfer. Somebody filtering by the shop wants the
            // transfers that brought goods into it as much as the ones that took them
            // out.
            documents = documents.Where(document =>
                document.WarehouseId == warehouse
                || document.DestinationWarehouseId == warehouse);
        }

        if (status is { } state)
        {
            documents = documents.Where(document => document.Status == state);
        }

        var rows = await documents
            .OrderByDescending(document => document.Date)
            .ThenByDescending(document => document.Number)
            .Select(document => new
            {
                document.Id,
                document.Number,
                document.Type,
                document.Date,
                document.WarehouseId,
                document.DestinationWarehouseId,
                document.ReferenceNumber,
                document.Narration,
                document.Status,
                LineCount = document.Lines.Count,
                TotalQuantity = document.Lines.Sum(line => line.StockQuantity),
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        Dictionary<WarehouseId, string> warehouses = await WarehouseNamesAsync(
            firmId, cancellationToken);

        // The value comes from the ledger rather than from the lines: an issue's line
        // carries no rate, because what it was worth was decided by the position it
        // came out of at the moment it posted.
        List<StockDocumentId> ids = [.. rows.Select(row => row.Id)];

        Dictionary<StockDocumentId, decimal> values = await _context.StockLedgerEntries
            .Where(entry => ids.Contains(entry.DocumentId) && entry.Quantity > 0m)
            .GroupBy(entry => entry.DocumentId)
            .Select(group => new
            {
                DocumentId = group.Key,
                Value = group.Sum(entry => entry.Value.Amount),
            })
            .ToDictionaryAsync(row => row.DocumentId, row => row.Value, cancellationToken);

        // Documents that only take goods out write no positive movement, so their
        // value is the absolute of what left.
        Dictionary<StockDocumentId, decimal> issued = await _context.StockLedgerEntries
            .Where(entry => ids.Contains(entry.DocumentId) && entry.Quantity < 0m)
            .GroupBy(entry => entry.DocumentId)
            .Select(group => new
            {
                DocumentId = group.Key,
                Value = group.Sum(entry => entry.Value.Amount),
            })
            .ToDictionaryAsync(row => row.DocumentId, row => row.Value, cancellationToken);

        return
        [
            .. rows.Select(row => new StockDocumentSummary(
                row.Id.Value,
                row.Number,
                row.Type,
                row.Date,
                warehouses.GetValueOrDefault(row.WarehouseId, string.Empty),
                row.DestinationWarehouseId is { } into
                    ? warehouses.GetValueOrDefault(into)
                    : null,
                row.ReferenceNumber,
                row.Narration,
                row.Status,
                row.LineCount,
                row.TotalQuantity,
                values.TryGetValue(row.Id, out decimal received) && received != 0m
                    ? received
                    : Math.Abs(issued.GetValueOrDefault(row.Id)))),
        ];
    }

    /// <inheritdoc />
    public async Task<StockDocumentDetail?> FindAsync(
        FirmId firmId,
        StockDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.StockDocuments
            .Where(row => row.Id == documentId && row.FirmId == firmId)
            .Select(row => new
            {
                row.Id,
                row.Number,
                row.Type,
                row.Date,
                row.WarehouseId,
                row.DestinationWarehouseId,
                row.ReferenceNumber,
                row.Narration,
                row.Status,
                row.CancellationReason,
                Lines = row.Lines
                    .OrderBy(line => line.LineNumber)
                    .Select(line => new
                    {
                        line.Id,
                        line.LineNumber,
                        line.ProductId,
                        line.UnitId,
                        line.Quantity,
                        line.StockQuantity,
                        line.Rate,
                        line.Remarks,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        Dictionary<WarehouseId, string> warehouses = await WarehouseNamesAsync(
            firmId, cancellationToken);

        List<ProductId> productIds = [.. document.Lines.Select(line => line.ProductId)];

        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.StockUnitId,
                product.Currency,
            })
            .ToListAsync(cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        var byProduct = products.ToDictionary(product => product.Id);

        // What the document actually did, alongside what it says. On a cancelled
        // document this holds the reversals beside the originals, which is the whole
        // point of reversing rather than deleting.
        var movements = await _context.StockLedgerEntries
            .Where(entry => entry.DocumentId == documentId)
            .OrderBy(entry => entry.PostedAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.ProductId,
                entry.WarehouseId,
                entry.Quantity,
                entry.UnitCost,
                Value = entry.Value.Amount,
                entry.BalanceQuantity,
                entry.BalanceAverageCost,
            })
            .ToListAsync(cancellationToken);

        return new StockDocumentDetail(
            document.Id.Value,
            document.Number,
            document.Type,
            document.Date,
            document.WarehouseId.Value,
            warehouses.GetValueOrDefault(document.WarehouseId, string.Empty),
            document.DestinationWarehouseId?.Value,
            document.DestinationWarehouseId is { } into
                ? warehouses.GetValueOrDefault(into)
                : null,
            document.ReferenceNumber,
            document.Narration,
            document.Status,
            products.Count > 0 ? products[0].Currency.Code : string.Empty,
            document.CancellationReason,
            [
                .. document.Lines.Select(line => new StockDocumentLineView(
                    line.Id.Value,
                    line.LineNumber,
                    line.ProductId.Value,
                    byProduct.TryGetValue(line.ProductId, out var product)
                        ? product.Code
                        : string.Empty,
                    product is not null ? product.Description : string.Empty,
                    line.UnitId.Value,
                    units.GetValueOrDefault(line.UnitId, string.Empty),
                    line.Quantity,
                    line.StockQuantity,
                    product is not null
                        ? units.GetValueOrDefault(product.StockUnitId, string.Empty)
                        : string.Empty,
                    line.Rate,
                    line.Remarks)),
            ],
            [
                .. movements.Select(entry => new StockMovementView(
                    byProduct.TryGetValue(entry.ProductId, out var moved)
                        ? moved.Code
                        : string.Empty,
                    warehouses.GetValueOrDefault(entry.WarehouseId, string.Empty),
                    entry.Quantity,
                    entry.UnitCost,
                    entry.Value,
                    entry.BalanceQuantity,
                    entry.BalanceAverageCost)),
            ]);
    }

    private async Task<Dictionary<WarehouseId, string>> WarehouseNamesAsync(
        FirmId firmId,
        CancellationToken cancellationToken) =>
        await _context.Warehouses
            .Where(warehouse => warehouse.FirmId == firmId)
            .ToDictionaryAsync(
                warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);
}

/// <summary>Reads the stock valuation, the stock ledger, and item movement.</summary>
/// <remarks>
/// The valuation reads the positions; the other two read the ledger. That split is
/// the design: the position is the running answer and the ledger is the history, and
/// asking either of them the other's question would be slower and could only agree.
/// </remarks>
public sealed class StockReportReader : IStockReportReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockReportReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockReportReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<StockValuationReport> ValuationAsync(
        FirmId firmId,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        bool includeZero,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockBalance> balances = _context.StockBalances
            .Where(balance => balance.FirmId == firmId);

        if (warehouseId is { } warehouse)
        {
            balances = balances.Where(balance => balance.WarehouseId == warehouse);
        }

        if (!includeZero)
        {
            balances = balances.Where(balance => balance.Quantity != 0m);
        }

        var rows = await balances
            .Join(
                _context.Products,
                balance => balance.ProductId,
                product => product.Id,
                (balance, product) => new { balance, product })
            .Where(pair => categoryId == null || pair.product.CategoryId == categoryId)
            .Select(pair => new
            {
                pair.balance.ProductId,
                pair.product.Code,
                pair.product.Description,
                pair.product.CategoryId,
                pair.product.StockUnitId,
                pair.balance.WarehouseId,
                pair.balance.Quantity,
                pair.balance.AverageCost,
                pair.balance.Currency,
                ReorderLevel = pair.product.Levels.Reorder,
            })
            .OrderBy(row => row.Code)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new StockValuationReport(string.Empty, [], 0m);
        }

        Dictionary<CategoryId, string> categories = await _context.Categories
            .Where(category => category.FirmId == firmId)
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        Dictionary<WarehouseId, string> warehouses = await _context.Warehouses
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        List<StockValuationRow> valued =
        [
            .. rows.Select(row =>
            {
                // Rounded here rather than stored rounded. The average is kept to six
                // places so it does not drift; the value it produces is money, and
                // money is presented at the currency's own scale.
                decimal value = decimal.Round(
                    row.Quantity * row.AverageCost, 2, MidpointRounding.AwayFromZero);

                // The reorder level is compared against the position in this warehouse
                // rather than across the firm. A reorder level is a shelf's rule, and a
                // warehouse that has run out is out whatever another one holds.
                bool belowReorder = row.ReorderLevel > 0m && row.Quantity <= row.ReorderLevel;

                return new StockValuationRow(
                    row.ProductId.Value,
                    row.Code,
                    row.Description,
                    categories.GetValueOrDefault(row.CategoryId, string.Empty),
                    row.WarehouseId.Value,
                    warehouses.GetValueOrDefault(row.WarehouseId, string.Empty),
                    units.GetValueOrDefault(row.StockUnitId, string.Empty),
                    row.Quantity,
                    row.AverageCost,
                    value,
                    row.ReorderLevel,
                    belowReorder);
            }),
        ];

        return new StockValuationReport(
            rows[0].Currency.Code, valued, valued.Sum(row => row.Value));
    }

    /// <inheritdoc />
    public async Task<StockLedgerReport?> LedgerAsync(
        FirmId firmId,
        ProductId productId,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Where(row => row.Id == productId && row.FirmId == firmId)
            .Select(row => new { row.Code, row.Description, row.StockUnitId, row.Currency })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        IQueryable<StockLedgerEntry> entries = _context.StockLedgerEntries
            .Where(entry => entry.FirmId == firmId && entry.ProductId == productId);

        if (warehouseId is { } warehouse)
        {
            entries = entries.Where(entry => entry.WarehouseId == warehouse);
        }

        // The opening figure is the net of everything before the range rather than the
        // balance column of the last row before it. Those agree when one warehouse is
        // asked for and do not when several are - the balance column belongs to one
        // position, and a ledger across three godowns has three of them.
        decimal opening = await entries
            .Where(entry => entry.Date < from)
            .SumAsync(entry => (decimal?)entry.Quantity, cancellationToken) ?? 0m;

        var rows = await entries
            .Where(entry => entry.Date >= from && entry.Date <= to)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.PostedAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Date,
                entry.DocumentId,
                entry.DocumentType,
                entry.DocumentNumber,
                entry.WarehouseId,
                entry.Quantity,
                entry.UnitCost,
                Value = entry.Value.Amount,
                entry.BalanceQuantity,
                entry.BalanceAverageCost,
                entry.Narration,
            })
            .ToListAsync(cancellationToken);

        Dictionary<WarehouseId, string> warehouses = await _context.Warehouses
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        List<StockLedgerRow> ledger =
        [
            .. rows.Select(row => new StockLedgerRow(
                row.Date,
                row.DocumentId.Value,
                row.DocumentType,
                row.DocumentNumber,
                warehouses.GetValueOrDefault(row.WarehouseId, string.Empty),
                row.Quantity > 0m ? row.Quantity : 0m,
                row.Quantity < 0m ? -row.Quantity : 0m,
                row.UnitCost,
                row.Value,
                row.BalanceQuantity,
                row.BalanceAverageCost,
                row.Narration)),
        ];

        decimal totalIn = ledger.Sum(row => row.QuantityIn);
        decimal totalOut = ledger.Sum(row => row.QuantityOut);

        return new StockLedgerReport(
            product.Code,
            product.Description,
            units.GetValueOrDefault(product.StockUnitId, string.Empty),
            product.Currency.Code,
            opening,
            ledger,
            opening + totalIn - totalOut,
            totalIn,
            totalOut);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMovementRow>> MovementAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CategoryId? categoryId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockLedgerEntry> entries = _context.StockLedgerEntries
            .Where(entry => entry.FirmId == firmId && entry.Date >= from && entry.Date <= to);

        if (warehouseId is { } warehouse)
        {
            entries = entries.Where(entry => entry.WarehouseId == warehouse);
        }

        // Aggregated in the database rather than by reading every movement into
        // memory. A year of movements across a full product master is millions of
        // rows, and the report is a few hundred.
        var totals = await entries
            .GroupBy(entry => entry.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                QuantityIn = group.Where(entry => entry.Quantity > 0m)
                    .Sum(entry => (decimal?)entry.Quantity) ?? 0m,
                QuantityOut = group.Where(entry => entry.Quantity < 0m)
                    .Sum(entry => (decimal?)entry.Quantity) ?? 0m,
                ValueIn = group.Where(entry => entry.Quantity > 0m)
                    .Sum(entry => (decimal?)entry.Value.Amount) ?? 0m,
                ValueOut = group.Where(entry => entry.Quantity < 0m)
                    .Sum(entry => (decimal?)entry.Value.Amount) ?? 0m,
                Movements = group.Count(),
                LastMovedOn = group.Max(entry => (DateOnly?)entry.Date),
            })
            .ToListAsync(cancellationToken);

        if (totals.Count == 0)
        {
            return [];
        }

        List<ProductId> productIds = [.. totals.Select(row => row.ProductId)];

        var products = await _context.Products
            .Where(product =>
                productIds.Contains(product.Id)
                && (categoryId == null || product.CategoryId == categoryId))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.CategoryId,
                product.StockUnitId,
            })
            .ToListAsync(cancellationToken);

        Dictionary<CategoryId, string> categories = await _context.Categories
            .Where(category => category.FirmId == firmId)
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        var byProduct = totals.ToDictionary(row => row.ProductId);

        return
        [
            .. products
                .Where(product => byProduct.ContainsKey(product.Id))
                .Select(product =>
                {
                    var moved = byProduct[product.Id];

                    return new ItemMovementRow(
                        product.Id.Value,
                        product.Code,
                        product.Description,
                        categories.GetValueOrDefault(product.CategoryId, string.Empty),
                        units.GetValueOrDefault(product.StockUnitId, string.Empty),
                        moved.QuantityIn,
                        -moved.QuantityOut,
                        moved.ValueIn,
                        -moved.ValueOut,
                        moved.Movements,
                        moved.LastMovedOn);
                })
                .OrderBy(row => row.ProductCode),
        ];
    }
}
