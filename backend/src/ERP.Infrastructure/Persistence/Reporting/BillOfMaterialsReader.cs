using ERP.Application.Inventory.Production;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads bills of materials, with what their components cost today.</summary>
/// <remarks>
/// The estimate is at what each component's stock costs on average today - what a
/// production run would charge the finished good were it posted now - or at the product
/// master's cost where none is held.
/// </remarks>
public sealed class BillOfMaterialsReader : IBillOfMaterialsReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="BillOfMaterialsReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public BillOfMaterialsReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillOfMaterialsSummary>> ListAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        List<BillOfMaterials> bills = await _context.BillsOfMaterials
            .Include(bill => bill.Lines)
            .Where(bill => bill.FirmId == firmId && (includeInactive || bill.IsActive))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Dictionary<ProductId, ProductFacts> products = await ProductsAsync(
            firmId, [.. bills.SelectMany(bill => bill.Lines.Select(line => line.ProductId).Append(bill.ProductId))],
            cancellationToken);

        return [.. bills.Select(bill => Summarise(bill, products)).OrderBy(bill => bill.ProductCode)];
    }

    /// <inheritdoc />
    public async Task<BillOfMaterialsDetail?> FindAsync(
        FirmId firmId,
        BillOfMaterialsId id,
        CancellationToken cancellationToken = default)
    {
        BillOfMaterials? bill = await _context.BillsOfMaterials
            .Include(row => row.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.FirmId == firmId, cancellationToken);

        if (bill is null)
        {
            return null;
        }

        Dictionary<ProductId, ProductFacts> products = await ProductsAsync(
            firmId, [.. bill.Lines.Select(line => line.ProductId).Append(bill.ProductId)], cancellationToken);

        return new BillOfMaterialsDetail(
            Summarise(bill, products),
            bill.Narration,
            [
                .. bill.Lines.OrderBy(line => line.LineNumber).Select(line =>
                {
                    ProductFacts? component = products.GetValueOrDefault(line.ProductId);

                    return new BillOfMaterialsComponent(
                        line.ProductId.Value,
                        component?.Code ?? string.Empty,
                        component?.Name ?? string.Empty,
                        component?.Unit ?? string.Empty,
                        line.Quantity,
                        component?.Cost ?? 0m,
                        component?.TracksBatches ?? false,
                        line.Remarks);
                }),
            ]);
    }

    private static BillOfMaterialsSummary Summarise(
        BillOfMaterials bill,
        Dictionary<ProductId, ProductFacts> products)
    {
        ProductFacts? finished = products.GetValueOrDefault(bill.ProductId);

        return new BillOfMaterialsSummary(
            bill.Id.Value,
            bill.ProductId.Value,
            finished?.Code ?? string.Empty,
            finished?.Name ?? string.Empty,
            finished?.Unit ?? string.Empty,
            bill.OutputQuantity,
            bill.Lines.Count,
            decimal.Round(
                bill.Lines.Sum(line => line.Quantity * (products.GetValueOrDefault(line.ProductId)?.Cost ?? 0m)),
                4),
            bill.IsActive);
    }

    private async Task<Dictionary<ProductId, ProductFacts>> ProductsAsync(
        FirmId firmId,
        List<ProductId> ids,
        CancellationToken cancellationToken)
    {
        List<ProductId> distinct = [.. ids.Distinct()];

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        var rows = await _context.Products
            .Where(product => distinct.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.StockUnitId,
                product.Rates.Cost,
                product.TracksBatches,
            })
            .ToListAsync(cancellationToken);

        // What the goods on the shelves cost on average across the firm's warehouses,
        // which is what a run would use them up at; the master's cost where none is held.
        var held = await _context.StockBalances
            .Where(balance => balance.FirmId == firmId
                && distinct.Contains(balance.ProductId)
                && balance.Quantity > 0m)
            .GroupBy(balance => balance.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(balance => balance.Quantity),
                Value = group.Sum(balance => balance.Quantity * balance.AverageCost),
            })
            .ToDictionaryAsync(row => row.ProductId, cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => new ProductFacts(
                row.Code,
                row.Description,
                units.GetValueOrDefault(row.StockUnitId, string.Empty),
                held.TryGetValue(row.Id, out var stock) && stock.Quantity > 0m
                    ? decimal.Round(stock.Value / stock.Quantity, 6)
                    : row.Cost,
                row.TracksBatches));
    }

    private sealed record ProductFacts(string Code, string Name, string Unit, decimal Cost, bool TracksBatches);
}
