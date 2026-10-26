using System.Globalization;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Inventory.Products;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes the product master.</summary>
public sealed class ProductRepository : IProductRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="ProductRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ProductRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Product?> FindAsync(
        ProductId id,
        CancellationToken cancellationToken = default) =>
        _context.Products
            .Include(product => product.Barcodes)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ProductId, Product>> GetManyAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<ProductId, Product>();
        }

        List<ProductId> wanted = [.. ids.Distinct()];

        // Without the barcodes. A stock document needs a product's code, its stock
        // unit and its item type; the barcodes are a child collection nothing here
        // reads, and loading forty products' worth of them per document would be the
        // largest part of the query for no purpose.
        return await _context.Products
            .Where(product => wanted.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) =>
        _context.Products.AnyAsync(
            product => product.FirmId == firmId && product.Code == code, cancellationToken);

    /// <inheritdoc />
    public void Add(Product product) => _context.Products.Add(product);
}

/// <summary>Reads the product master.</summary>
public sealed class ProductReader : IProductReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="ProductReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ProductReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductSummary>> ListAsync(
        FirmId firmId,
        string? search,
        CategoryId? categoryId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Product> products = _context.Products
            .Where(product => product.FirmId == firmId);

        if (!includeInactive)
        {
            products = products.Where(product => product.IsActive);
        }

        if (categoryId is { } category)
        {
            products = products.Where(product => product.CategoryId == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();

            // Barcode as well as code and description, because scanning is how a
            // product is most often found and the code on the label is frequently not
            // the code in the master.
            products = products.Where(product =>
                EF.Functions.ILike(product.Code, $"%{term}%")
                || EF.Functions.ILike(product.Description, $"%{term}%")
                || product.Barcodes.Any(barcode =>
                    EF.Functions.ILike(barcode.Barcode, $"%{term}%")));
        }

        var rows = await products
            .OrderBy(product => product.Code)
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.DescriptionArabic,
                product.ItemType,
                product.CategoryId,
                product.BrandId,
                product.TaxSlabId,
                product.StockUnitId,
                Currency = product.Currency,
                Cost = product.Rates.Cost,
                RetailRate = product.Rates.RetailRate,
                WholesaleRate = product.Rates.WholesaleRate,
                MaximumRetailPrice = product.Rates.MaximumRetailPrice,
                ReorderLevel = product.Levels.Reorder,
                product.TracksBatches,
                product.TracksSerialNumbers,
                product.IsDiscontinued,
                product.IsActive,
                BarcodeCount = product.Barcodes.Count,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // The three lookups are fetched once each and joined in memory. Three left
        // joins in the projection would multiply the row set and then be de-duplicated,
        // which on a master of tens of thousands is work for nothing.
        Dictionary<CategoryId, string> categories = await _context.Categories
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        Dictionary<BrandId, string> brands = await _context.Brands
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Code, cancellationToken);

        Dictionary<TaxSlabId, decimal> slabs = await _context.TaxSlabs
            .Where(row => row.FirmId == firmId)
            .ToDictionaryAsync(row => row.Id, row => row.Percentage, cancellationToken);

        // Read from the stock positions rather than kept on the product. Every movement -
        // a sale, a purchase, a return, an adjustment, a transfer, production - already
        // updates the position in the same transaction as the movement itself, so their
        // sum is current by construction. A copy on the product would be a second place
        // every one of those paths had to remember to update, and the first one that
        // forgot would leave the list disagreeing with the stock reports.
        Dictionary<ProductId, decimal> onHand = await _context.StockBalances
            .Where(row => row.FirmId == firmId)
            .GroupBy(row => row.ProductId)
            .Select(group => new { group.Key, Quantity = group.Sum(row => row.Quantity) })
            .ToDictionaryAsync(row => row.Key, row => row.Quantity, cancellationToken);

        return
        [
            .. rows.Select(row => new ProductSummary(
                row.Id.Value,
                row.Code,
                row.Description,
                row.DescriptionArabic,
                row.ItemType,
                row.CategoryId.Value,
                categories.GetValueOrDefault(row.CategoryId, string.Empty),
                row.BrandId is { } brand ? brands.GetValueOrDefault(brand) : null,
                units.GetValueOrDefault(row.StockUnitId, string.Empty),
                row.Currency.Code,
                row.Cost,
                row.RetailRate,
                row.ReorderLevel,
                row.TracksBatches,
                row.TracksSerialNumbers,
                row.IsDiscontinued,
                row.IsActive,
                row.BarcodeCount,
                row.WholesaleRate,
                row.MaximumRetailPrice,
                row.TaxSlabId is { } slab && slabs.TryGetValue(slab, out decimal rate)
                    ? rate
                    : null,
                onHand.GetValueOrDefault(row.Id))),
        ];
    }

    /// <inheritdoc />
    public async Task<ProductDetail?> FindAsync(
        FirmId firmId,
        ProductId productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Where(row => row.Id == productId && row.FirmId == firmId)
            .Select(row => new
            {
                row.Id,
                row.Code,
                row.Description,
                row.DescriptionArabic,
                row.ShortDescription,
                row.ItemName,
                row.Manufacturer,
                row.Label,
                row.Size,
                row.Origin,
                row.ItemType,
                row.CategoryId,
                row.BrandId,
                row.TaxSlabId,
                TaxPercentage = _context.TaxSlabs
                    .Where(slab => slab.Id == row.TaxSlabId)
                    .Select(slab => (decimal?)slab.Percentage)
                    .FirstOrDefault(),
                row.StockUnitId,
                row.PurchaseUnitId,
                row.SalesUnitId,
                row.Currency,
                row.CostingMethod,
                Rates = row.Rates,
                Levels = row.Levels,
                row.Movement,
                Device = row.Device,
                row.Rack,
                row.Bin,
                row.TracksBatches,
                row.TracksSerialNumbers,
                row.ShelfLifeDays,
                row.IsPacking,
                row.IsDiscontinued,
                row.IsActive,
                Barcodes = row.Barcodes
                    .OrderBy(barcode => barcode.Barcode)
                    .Select(barcode => new ProductBarcodeView(
                        barcode.Id.Value,
                        barcode.Barcode,
                        barcode.Rates.Cost,
                        barcode.Rates.RetailRate,
                        barcode.Rates.WholesaleRate,
                        barcode.Rates.MaximumRetailPrice))
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return product is null
            ? null
            : new ProductDetail(
                product.Id.Value,
                product.Code,
                product.Description,
                product.DescriptionArabic,
                product.ShortDescription,
                product.ItemName,
                product.Manufacturer,
                product.Label,
                product.Size,
                product.Origin,
                product.ItemType,
                product.CategoryId.Value,
                product.BrandId?.Value,
                product.StockUnitId.Value,
                product.PurchaseUnitId.Value,
                product.SalesUnitId.Value,
                product.Currency.Code,
                product.CostingMethod,
                product.Rates.Cost,
                product.Rates.ProfitPercentage,
                product.Rates.CorPercentage,
                product.Rates.RetailRate,
                product.Rates.WholesaleRate,
                product.Rates.OtherRate,
                product.Rates.MaximumRetailPrice,
                product.Levels.Minimum,
                product.Levels.Reorder,
                product.Levels.Maximum,
                product.Movement,
                product.Device.Device,
                product.Device.Colour,
                product.Device.Battery,
                product.Device.Ram,
                product.Device.Storage,
                product.Rack,
                product.Bin,
                product.TracksBatches,
                product.TracksSerialNumbers,
                product.ShelfLifeDays,
                product.IsPacking,
                product.IsDiscontinued,
                product.IsActive,
                product.Barcodes,
                product.TaxSlabId?.Value,
                product.TaxPercentage);
    }
}
