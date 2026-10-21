using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the history of stock corrections from the documents and the stock ledger.</summary>
/// <remarks>
/// The stock either side of a correction comes from the ledger, which recorded the balance
/// the moment the line posted - today's balance has moved on and says nothing about then.
/// A count that found exactly what the system held moved nothing and wrote no entry; its
/// line still belongs in the history, with the count as both figures.
/// </remarks>
public sealed class StockHistoryReader : IStockHistoryReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockHistoryReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockHistoryReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockHistoryRow>> ListAsync(
        FirmId firmId,
        IReadOnlyCollection<StockDocumentType> types,
        DateOnly from,
        DateOnly to,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockDocument> documents = _context.StockDocuments
            .Where(document => document.FirmId == firmId
                && types.Contains(document.Type)
                && document.Status != StockDocumentStatus.Draft
                && document.Date >= from
                && document.Date <= to);

        if (warehouseId is { } warehouse)
        {
            documents = documents.Where(document => document.WarehouseId == warehouse);
        }

        List<StockDocument> found = await documents
            .Include(document => document.Lines)
            .OrderByDescending(document => document.Date)
            .ThenByDescending(document => document.Number)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (found.Count == 0)
        {
            return [];
        }

        List<StockDocumentId> ids = [.. found.Select(document => document.Id)];

        // The original movements only: a cancelled document also carries its reversals,
        // which are the same goods going back and would double every line.
        var entries = (await _context.StockLedgerEntries
                .Where(entry => ids.Contains(entry.DocumentId))
                .OrderBy(entry => entry.PostedAtUtc)
                .ThenBy(entry => entry.Id)
                .Select(entry => new
                {
                    entry.DocumentId,
                    entry.ProductId,
                    entry.BatchId,
                    entry.Quantity,
                    Value = entry.Value.Amount,
                    entry.BalanceQuantity,
                })
                .ToListAsync(cancellationToken))
            .GroupBy(entry => (entry.DocumentId, entry.ProductId, entry.BatchId))
            .ToDictionary(group => group.Key, group => group.First());

        List<ProductId> productIds = [.. found.SelectMany(document => document.Lines).Select(line => line.ProductId).Distinct()];

        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new { product.Id, product.Code, product.Description, product.StockUnitId })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        Dictionary<WarehouseId, string> warehouses = await _context.Warehouses
            .Where(place => place.FirmId == firmId)
            .ToDictionaryAsync(place => place.Id, place => place.Name, cancellationToken);

        List<BatchId> batchIds =
        [
            .. found.SelectMany(document => document.Lines)
                .Where(line => line.BatchId is not null)
                .Select(line => line.BatchId!.Value)
                .Distinct(),
        ];

        Dictionary<BatchId, string> batches = batchIds.Count == 0
            ? []
            : await _context.Batches
                .Where(batch => batchIds.Contains(batch.Id))
                .ToDictionaryAsync(batch => batch.Id, batch => batch.Number, cancellationToken);

        List<StockHistoryRow> rows = [];

        foreach (StockDocument document in found)
        {
            foreach (StockDocumentLine line in document.Lines.OrderBy(line => line.LineNumber))
            {
                var product = products.GetValueOrDefault(line.ProductId);
                bool moved = entries.TryGetValue((document.Id, line.ProductId, line.BatchId), out var entry);

                decimal adjusted = moved ? entry!.Quantity : 0m;
                decimal actual = moved
                    ? entry!.BalanceQuantity
                    : line.StockQuantity;

                rows.Add(new StockHistoryRow(
                    document.Id.Value,
                    document.Number,
                    document.Date,
                    document.Type,
                    document.Status,
                    warehouses.GetValueOrDefault(document.WarehouseId, string.Empty),
                    line.ProductId.Value,
                    product?.Code ?? string.Empty,
                    product?.Description ?? string.Empty,
                    product is null ? string.Empty : units.GetValueOrDefault(product.StockUnitId, string.Empty),
                    line.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null,
                    actual - adjusted,
                    actual,
                    adjusted,
                    moved ? entry!.Value : 0m,
                    line.Remarks ?? document.Narration));
            }
        }

        return rows;
    }
}
