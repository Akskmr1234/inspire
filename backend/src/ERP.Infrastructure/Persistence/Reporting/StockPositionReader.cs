using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads what is on hand off the stock ledger, at its finest grain.</summary>
public sealed class StockPositionReader : IStockPositionReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockPositionReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockPositionReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<StockPositionReport> ReadAsync(
        FirmId firmId,
        DateOnly asOn,
        StockPositionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<StockLedgerEntry> entries = Filtered(firmId, asOn, query);

        // Every product, godown and batch the ledger has moved, netted to what is left.
        var positions = await entries
            .GroupBy(entry => new { entry.ProductId, entry.WarehouseId, entry.BatchId })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.WarehouseId,
                group.Key.BatchId,
                Quantity = group.Sum(entry => entry.Quantity),
            })
            .Where(position => position.Quantity != 0m)
            .ToListAsync(cancellationToken);

        List<CurrencyCode> currencies = await _context.StockBalances.AsNoTracking()
            .Where(balance => balance.FirmId == firmId)
            .Select(balance => balance.Currency)
            .Take(1)
            .ToListAsync(cancellationToken);
        string currency = currencies.Count > 0 ? currencies[0].Code : string.Empty;

        if (positions.Count == 0)
        {
            return new StockPositionReport(asOn, currency, [], 0m, 0m);
        }

        // The average cost each product stood at in each godown on the day: the one the
        // last movement posted by then left behind.
        var lastPosted = entries
            .GroupBy(entry => new { entry.ProductId, entry.WarehouseId })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.WarehouseId,
                At = group.Max(entry => entry.PostedAtUtc),
            });

        var costed = await (
                from entry in entries
                join last in lastPosted
                    on new { entry.ProductId, entry.WarehouseId, At = entry.PostedAtUtc }
                    equals new { last.ProductId, last.WarehouseId, last.At }
                select new { entry.ProductId, entry.WarehouseId, entry.BalanceAverageCost })
            .ToListAsync(cancellationToken);

        Dictionary<(ProductId, WarehouseId), decimal> costs = [];

        foreach (var row in costed)
        {
            costs[(row.ProductId, row.WarehouseId)] = row.BalanceAverageCost;
        }

        Lookups names = await LookupsAsync(
            firmId,
            [.. positions.Select(position => position.ProductId).Distinct()],
            [.. positions.Select(position => position.BatchId).OfType<BatchId>().Distinct()],
            cancellationToken);

        List<StockPositionRow> rows = [];

        if (!query.ByReceipt)
        {
            foreach (var position in positions)
            {
                rows.Add(names.Row(
                    position.ProductId,
                    position.WarehouseId,
                    position.BatchId,
                    position.Quantity,
                    costs.GetValueOrDefault((position.ProductId, position.WarehouseId)),
                    receivedOn: null,
                    asOn));
            }
        }
        else
        {
            // What each receipt brought in, net of any part of the same document that was
            // cancelled or edited away: a purchase entered and then cancelled is no receipt.
            var receipts = await entries
                .GroupBy(entry => new { entry.ProductId, entry.WarehouseId, entry.BatchId, entry.DocumentId })
                .Select(group => new
                {
                    group.Key.ProductId,
                    group.Key.WarehouseId,
                    group.Key.BatchId,
                    Date = group.Min(entry => entry.Date),
                    PostedAt = group.Min(entry => entry.PostedAtUtc),
                    Quantity = group.Sum(entry => entry.Quantity),
                })
                .Where(receipt => receipt.Quantity > 0m)
                .ToListAsync(cancellationToken);

            var byPosition = receipts
                .GroupBy(receipt => (receipt.ProductId, receipt.WarehouseId, receipt.BatchId))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(receipt => receipt.Date)
                        .ThenByDescending(receipt => receipt.PostedAt)
                        .Select(receipt => (receipt.Date, receipt.Quantity))
                        .ToList());

            foreach (var position in positions.Where(position => position.Quantity > 0m))
            {
                decimal cost = costs.GetValueOrDefault((position.ProductId, position.WarehouseId));
                List<(DateOnly Date, decimal Quantity)> newestFirst = byPosition.GetValueOrDefault(
                    (position.ProductId, position.WarehouseId, position.BatchId)) ?? [];

                foreach ((DateOnly date, decimal quantity) in Layers(position.Quantity, newestFirst))
                {
                    rows.Add(names.Row(
                        position.ProductId, position.WarehouseId, position.BatchId, quantity, cost, date, asOn));
                }
            }
        }

        List<StockPositionRow> ordered =
        [
            .. rows
                .OrderBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.WarehouseName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.BatchNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ReceivedOn),
        ];

        return new StockPositionReport(
            asOn, currency, ordered, ordered.Sum(row => row.Quantity), ordered.Sum(row => row.Value));
    }

    /// <summary>
    /// Matches what is on hand to the receipts it is made of, newest first; receipts on the
    /// same day become one part. What no receipt accounts for - stock that only ever came
    /// in through a correction - is dated with the oldest receipt there is.
    /// </summary>
    private static IEnumerable<(DateOnly Date, decimal Quantity)> Layers(
        decimal onHand,
        List<(DateOnly Date, decimal Quantity)> newestFirst)
    {
        Dictionary<DateOnly, decimal> parts = [];
        decimal left = onHand;

        foreach ((DateOnly date, decimal quantity) in newestFirst)
        {
            if (left <= 0m)
            {
                break;
            }

            decimal taken = Math.Min(left, quantity);
            parts[date] = parts.GetValueOrDefault(date) + taken;
            left -= taken;
        }

        if (left > 0m && newestFirst.Count > 0)
        {
            DateOnly oldest = newestFirst[^1].Date;
            parts[oldest] = parts.GetValueOrDefault(oldest) + left;
        }

        return parts.OrderByDescending(part => part.Key).Select(part => (part.Key, part.Value));
    }

    private IQueryable<StockLedgerEntry> Filtered(FirmId firmId, DateOnly asOn, StockPositionQuery query)
    {
        IQueryable<StockLedgerEntry> entries = _context.StockLedgerEntries.AsNoTracking()
            .Where(entry => entry.FirmId == firmId && entry.Date <= asOn);

        if (query.WarehouseId is { } warehouse)
        {
            WarehouseId id = WarehouseId.From(warehouse);
            entries = entries.Where(entry => entry.WarehouseId == id);
        }

        if (query.BranchId is { } branch)
        {
            BranchId id = BranchId.From(branch);
            IQueryable<WarehouseId> godowns = _context.Warehouses.AsNoTracking()
                .Where(row => row.FirmId == firmId && row.BranchId == id)
                .Select(row => row.Id);
            entries = entries.Where(entry => godowns.Contains(entry.WarehouseId));
        }

        if (query.ProductId is not null || query.CategoryId is not null || query.BrandId is not null)
        {
            IQueryable<Product> products = _context.Products.AsNoTracking()
                .Where(product => product.FirmId == firmId);

            if (query.ProductId is { } only)
            {
                ProductId id = ProductId.From(only);
                products = products.Where(product => product.Id == id);
            }

            if (query.CategoryId is { } category)
            {
                CategoryId id = CategoryId.From(category);
                products = products.Where(product => product.CategoryId == id);
            }

            if (query.BrandId is { } brand)
            {
                BrandId id = BrandId.From(brand);
                products = products.Where(product => product.BrandId == id);
            }

            IQueryable<ProductId> chosen = products.Select(product => product.Id);
            entries = entries.Where(entry => chosen.Contains(entry.ProductId));
        }

        if (query.ExpiresFrom is not null || query.ExpiresTo is not null)
        {
            // Only batches carry an expiry, so asking about one leaves out what has none.
            IQueryable<Batch> batches = _context.Batches.AsNoTracking()
                .Where(batch => batch.FirmId == firmId && batch.ExpiresOn != null);

            if (query.ExpiresFrom is { } from)
            {
                batches = batches.Where(batch => batch.ExpiresOn >= from);
            }

            if (query.ExpiresTo is { } to)
            {
                batches = batches.Where(batch => batch.ExpiresOn <= to);
            }

            IQueryable<BatchId?> expiring = batches.Select(batch => (BatchId?)batch.Id);
            entries = entries.Where(entry => expiring.Contains(entry.BatchId));
        }

        return entries;
    }

    private async Task<Lookups> LookupsAsync(
        FirmId firmId,
        List<ProductId> productIds,
        List<BatchId> batchIds,
        CancellationToken cancellationToken)
    {
        var products = await _context.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.CategoryId,
                product.BrandId,
                product.StockUnitId,
            })
            .ToListAsync(cancellationToken);

        Dictionary<CategoryId, string> categories = await _context.Categories.AsNoTracking()
            .Where(category => category.FirmId == firmId)
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        Dictionary<BrandId, string> brands = await _context.Brands.AsNoTracking()
            .Where(brand => brand.FirmId == firmId)
            .ToDictionaryAsync(brand => brand.Id, brand => brand.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure.AsNoTracking()
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        var warehouses = await _context.Warehouses.AsNoTracking()
            .Where(row => row.FirmId == firmId)
            .Select(row => new { row.Id, row.Name, row.BranchId })
            .ToListAsync(cancellationToken);

        Dictionary<BranchId, string> branches = await _context.Branches.AsNoTracking()
            .Where(branch => branch.FirmId == firmId)
            .ToDictionaryAsync(branch => branch.Id, branch => branch.Name, cancellationToken);

        var batches = await _context.Batches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .Select(batch => new { batch.Id, batch.Number, batch.ExpiresOn })
            .ToListAsync(cancellationToken);

        return new Lookups(
            products.ToDictionary(
                product => product.Id,
                product => new ProductName(
                    product.Code,
                    product.Description,
                    categories.GetValueOrDefault(product.CategoryId, string.Empty),
                    product.BrandId is { } brand ? brands.GetValueOrDefault(brand, string.Empty) : string.Empty,
                    units.GetValueOrDefault(product.StockUnitId, string.Empty))),
            warehouses.ToDictionary(
                row => row.Id,
                row => new GodownName(
                    row.Name,
                    row.BranchId?.Value,
                    row.BranchId is { } branch ? branches.GetValueOrDefault(branch, string.Empty) : string.Empty)),
            batches.ToDictionary(batch => batch.Id, batch => (batch.Number, batch.ExpiresOn)));
    }

    private sealed record ProductName(string Code, string Name, string Category, string Brand, string Unit);

    private sealed record GodownName(string Name, Guid? BranchId, string Branch);

    /// <summary>The names a row is drawn with, read once for the whole report.</summary>
    private sealed record Lookups(
        Dictionary<ProductId, ProductName> Products,
        Dictionary<WarehouseId, GodownName> Godowns,
        Dictionary<BatchId, (string Number, DateOnly? ExpiresOn)> Batches)
    {
        public StockPositionRow Row(
            ProductId productId,
            WarehouseId warehouseId,
            BatchId? batchId,
            decimal quantity,
            decimal cost,
            DateOnly? receivedOn,
            DateOnly asOn)
        {
            ProductName product = Products.GetValueOrDefault(productId)
                ?? new ProductName(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            GodownName godown = Godowns.GetValueOrDefault(warehouseId)
                ?? new GodownName(string.Empty, null, string.Empty);
            (string number, DateOnly? expiresOn) = batchId is { } batch
                ? Batches.GetValueOrDefault(batch, (string.Empty, null))
                : (string.Empty, null);

            return new StockPositionRow(
                productId.Value,
                product.Code,
                product.Name,
                number,
                expiresOn,
                product.Category,
                product.Brand,
                warehouseId.Value,
                godown.Name,
                godown.BranchId,
                godown.Branch,
                product.Unit,
                quantity,
                cost,
                decimal.Round(quantity * cost, 2, MidpointRounding.AwayFromZero),
                receivedOn,
                receivedOn is { } date ? asOn.DayNumber - date.DayNumber : null);
        }
    }
}
