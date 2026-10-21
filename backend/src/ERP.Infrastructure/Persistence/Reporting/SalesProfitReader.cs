using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads what goods sold for and what they cost, batch by batch.</summary>
/// <remarks>
/// The sale comes from the invoice lines and the cost from the stock ledger entries the
/// invoice's own stock document wrote - the movement out of each batch at that batch's
/// cost. Where one line went out of more than one batch, its sale is shared between them
/// by quantity, so each batch carries its own goods at its own cost.
/// </remarks>
public sealed class SalesProfitReader : ISalesProfitReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesProfitReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesProfitReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<SalesProfitReport> ReadAsync(
        FirmId firmId,
        SalesProfitQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ProductId? only = query.ProductId is { } chosen ? ProductId.From(chosen) : null;

        var lines = await (
                from line in _context.SalesInvoiceLines.AsNoTracking()
                join invoice in _context.SalesInvoices.AsNoTracking() on line.SalesInvoiceId equals invoice.Id
                where invoice.FirmId == firmId
                    && invoice.Status == SalesInvoiceStatus.Posted
                    && invoice.Date >= query.From
                    && invoice.Date <= query.To
                    && (only == null || line.ProductId == only)
                select new
                {
                    invoice.Kind,
                    invoice.StockDocumentId,
                    invoice.DeliveryNoteId,
                    line.ProductId,
                    line.StockQuantity,
                    Taxable = line.TaxableAmount.Amount,
                })
            .ToListAsync(cancellationToken);

        // What each stock document sold of each product, signed: a return takes back.
        Dictionary<(StockDocumentId? Document, ProductId Product), (decimal Quantity, decimal Sales)> sold = [];

        foreach (var line in lines)
        {
            decimal sign = line.Kind == SalesDocumentKind.Return ? -1m : 1m;
            (StockDocumentId?, ProductId) key = (line.StockDocumentId ?? line.DeliveryNoteId, line.ProductId);

            sold.TryGetValue(key, out (decimal Quantity, decimal Sales) so);
            sold[key] = (so.Quantity + (sign * line.StockQuantity), so.Sales + (sign * line.Taxable));
        }

        List<StockDocumentId> documents =
            [.. sold.Keys.Where(key => key.Document is not null).Select(key => key.Document!.Value).Distinct()];

        var entries = await _context.StockLedgerEntries.AsNoTracking()
            .Where(entry => entry.FirmId == firmId && documents.Contains(entry.DocumentId))
            .Select(entry => new
            {
                entry.DocumentId,
                entry.ProductId,
                entry.BatchId,
                entry.Quantity,
                Value = entry.Value.Amount,
            })
            .ToListAsync(cancellationToken);

        // Out is negative on the ledger; a sale's quantity and cost are what went out.
        ILookup<(StockDocumentId?, ProductId), (BatchId? Batch, decimal Quantity, decimal Cost)> moved = entries
            .GroupBy(entry => (Document: (StockDocumentId?)entry.DocumentId, entry.ProductId, entry.BatchId))
            .Select(group => (
                Key: (group.Key.Document, group.Key.ProductId),
                Batch: group.Key.BatchId,
                Quantity: -group.Sum(entry => entry.Quantity),
                Cost: -group.Sum(entry => entry.Value)))
            .ToLookup(row => row.Key, row => (row.Batch, row.Quantity, row.Cost));

        Dictionary<(ProductId Product, BatchId? Batch), (decimal Quantity, decimal Sales, decimal Cost)> totals = [];

        foreach (((StockDocumentId? document, ProductId product), (decimal quantity, decimal sales)) in sold)
        {
            List<(BatchId? Batch, decimal Quantity, decimal Cost)> outOf = [.. moved[(document, product)]];
            decimal movedQuantity = outOf.Sum(row => row.Quantity);

            if (outOf.Count == 0 || movedQuantity == 0m)
            {
                // Nothing on the shelf moved for it - a service, or a sale whose goods went
                // out on a note outside the period - so it is all margin as far as can be told.
                Add(totals, (product, null), quantity, sales, outOf.Sum(row => row.Cost));
                continue;
            }

            foreach ((BatchId? batch, decimal batchQuantity, decimal cost) in outOf)
            {
                decimal share = batchQuantity / movedQuantity;

                Add(totals, (product, batch), batchQuantity, sales * share, cost);
            }
        }

        List<ProductId> products = [.. totals.Keys.Select(key => key.Product).Distinct()];
        List<BatchId> batches = [.. totals.Keys.Where(key => key.Batch is not null).Select(key => key.Batch!.Value).Distinct()];

        var productNames = await _context.Products.AsNoTracking()
            .Where(product => products.Contains(product.Id))
            .Select(product => new { product.Id, product.Code, product.Description })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var byBatch = await _context.Batches.AsNoTracking()
            .Where(batch => batches.Contains(batch.Id))
            .Select(batch => new { batch.Id, batch.Number, batch.PurchaseRate, batch.SellingRate })
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        List<SalesProfitRow> rows =
        [
            .. totals
                .Select(pair =>
                {
                    var name = productNames.GetValueOrDefault(pair.Key.Product);
                    var batch = pair.Key.Batch is { } id ? byBatch.GetValueOrDefault(id) : null;
                    decimal sales = Round(pair.Value.Sales);
                    decimal cost = Round(pair.Value.Cost);

                    return new SalesProfitRow(
                        pair.Key.Product.Value,
                        name?.Code ?? string.Empty,
                        name?.Description ?? string.Empty,
                        pair.Key.Batch?.Value,
                        batch?.Number,
                        pair.Value.Quantity,
                        sales,
                        cost,
                        sales - cost,
                        sales == 0m ? null : Math.Round((sales - cost) * 100m / sales, 2, MidpointRounding.AwayFromZero),
                        batch?.PurchaseRate,
                        batch?.SellingRate);
                })
                .OrderBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.BatchNumber, StringComparer.OrdinalIgnoreCase),
        ];

        var firm = await _context.Firms
            .Where(row => row.Id == firmId)
            .Select(row => new { row.BaseCurrency })
            .FirstOrDefaultAsync(cancellationToken);
        string currency = firm is null ? string.Empty : firm.BaseCurrency.Code;

        decimal totalSales = rows.Sum(row => row.Sales);
        decimal totalCost = rows.Sum(row => row.Cost);

        return new SalesProfitReport(
            query.From, query.To, currency, rows, totalSales, totalCost, totalSales - totalCost);
    }

    private static void Add(
        Dictionary<(ProductId Product, BatchId? Batch), (decimal Quantity, decimal Sales, decimal Cost)> totals,
        (ProductId Product, BatchId? Batch) key,
        decimal quantity,
        decimal sales,
        decimal cost)
    {
        totals.TryGetValue(key, out (decimal Quantity, decimal Sales, decimal Cost) so);
        totals[key] = (so.Quantity + quantity, so.Sales + sales, so.Cost + cost);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
