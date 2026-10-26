using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the stock summary off the stock ledger, added up in the database.</summary>
public sealed class StockSummaryReader : IStockSummaryReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockSummaryReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockSummaryReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<StockSummaryReport> ReadAsync(
        FirmId firmId,
        StockSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<StockLedgerEntry> entries = _context.StockLedgerEntries.AsNoTracking()
            .Where(entry => entry.FirmId == firmId && entry.Date <= query.To);

        if (query.WarehouseId is { } warehouse)
        {
            WarehouseId id = WarehouseId.From(warehouse);
            entries = entries.Where(entry => entry.WarehouseId == id);
        }

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

        var buckets = await (
                from entry in entries
                join product in products on entry.ProductId equals product.Id
                group entry by new
                {
                    entry.ProductId,
                    Before = entry.Date < query.From,
                    entry.DocumentType,
                    In = entry.Quantity > 0,
                }
                into bucket
                select new
                {
                    bucket.Key.ProductId,
                    bucket.Key.Before,
                    bucket.Key.DocumentType,
                    bucket.Key.In,
                    Quantity = bucket.Sum(entry => entry.Quantity),
                })
            .ToListAsync(cancellationToken);

        Dictionary<ProductId, Totals> byProduct = [];

        foreach (var bucket in buckets)
        {
            Totals totals = byProduct.TryGetValue(bucket.ProductId, out Totals? found) ? found : new Totals();
            byProduct[bucket.ProductId] = totals;

            if (bucket.Before)
            {
                totals.Opening += bucket.Quantity;
                continue;
            }

            switch (bucket.DocumentType)
            {
                case StockDocumentType.PurchaseReceipt
                    or StockDocumentType.ReceiptNote
                    or StockDocumentType.PurchaseReturn:
                    totals.Purchase += bucket.Quantity;
                    break;
                case StockDocumentType.SalesIssue
                    or StockDocumentType.DeliveryNote
                    or StockDocumentType.SalesReturn:
                    totals.Sales -= bucket.Quantity;
                    break;
                default:
                    if (bucket.In)
                    {
                        totals.OtherIn += bucket.Quantity;
                    }
                    else
                    {
                        totals.OtherOut -= bucket.Quantity;
                    }

                    break;
            }
        }

        List<ProductId> ids = [.. byProduct.Keys];

        var names = await (
                from product in _context.Products.AsNoTracking()
                join filed in _context.Categories.AsNoTracking() on product.CategoryId equals filed.Id
                join unit in _context.UnitsOfMeasure.AsNoTracking() on product.StockUnitId equals unit.Id
                where ids.Contains(product.Id)
                select new { product.Id, product.Code, product.Description, Category = filed.Name, Unit = unit.Code })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        List<StockSummaryRow> rows =
        [
            .. byProduct
                .Where(pair => pair.Value.Any)
                .Select(pair =>
                {
                    var name = names.GetValueOrDefault(pair.Key);
                    Totals totals = pair.Value;

                    return new StockSummaryRow(
                        pair.Key.Value,
                        name?.Code ?? string.Empty,
                        name?.Description ?? string.Empty,
                        name?.Category ?? string.Empty,
                        name?.Unit ?? string.Empty,
                        totals.Opening,
                        totals.Purchase,
                        totals.Sales,
                        totals.OtherIn,
                        totals.OtherOut,
                        totals.Opening + totals.Purchase - totals.Sales + totals.OtherIn - totals.OtherOut);
                })
                .OrderBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase),
        ];

        return new StockSummaryReport(query.From, query.To, rows);
    }

    /// <summary>One product's quantities as they are added up.</summary>
    private sealed class Totals
    {
        public decimal Opening { get; set; }

        public decimal Purchase { get; set; }

        public decimal Sales { get; set; }

        public decimal OtherIn { get; set; }

        public decimal OtherOut { get; set; }

        /// <summary>Gets a value indicating whether the product held or moved anything.</summary>
        public bool Any => Opening != 0m || Purchase != 0m || Sales != 0m || OtherIn != 0m || OtherOut != 0m;
    }
}
