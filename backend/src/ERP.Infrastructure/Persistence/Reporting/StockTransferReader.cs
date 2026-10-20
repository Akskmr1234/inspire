using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads transfer requests for the list and the detail screens.</summary>
public sealed class StockTransferReader : IStockTransferReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockTransferReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockTransferReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTransferSummary>> ListAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        StockTransferStatus? status,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockTransferRequest> requests = _context.StockTransferRequests
            .Where(request => request.FirmId == firmId && request.Date >= from && request.Date <= to);

        if (status is { } state)
        {
            requests = requests.Where(request => request.Status == state);
        }

        if (warehouseId is { } warehouse)
        {
            requests = requests.Where(request =>
                request.FromWarehouseId == warehouse || request.ToWarehouseId == warehouse);
        }

        List<StockTransferRequest> rows = await requests
            .Include(request => request.Lines)
            .OrderByDescending(request => request.Date)
            .ThenByDescending(request => request.Number)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Count == 0
            ? []
            : await DescribeAsync(firmId, rows, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StockTransferDetail?> FindAsync(
        FirmId firmId,
        StockTransferRequestId requestId,
        CancellationToken cancellationToken = default)
    {
        StockTransferRequest? request = await _context.StockTransferRequests
            .Include(row => row.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == requestId && row.FirmId == firmId, cancellationToken);

        if (request is null)
        {
            return null;
        }

        StockTransferSummary summary = (await DescribeAsync(firmId, [request], cancellationToken))[0];

        List<ProductId> productIds = [.. request.Lines.Select(line => line.ProductId)];

        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Description,
                product.StockUnitId,
                product.TracksBatches,
                product.TracksSerialNumbers,
            })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        Dictionary<UnitOfMeasureId, string> units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId)
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);

        // What the sending warehouse holds today: the figure an approver needs in front
        // of them before agreeing to part with it.
        Dictionary<ProductId, decimal> onHand = await _context.StockBalances
            .Where(balance => balance.FirmId == firmId
                && balance.WarehouseId == request.FromWarehouseId
                && productIds.Contains(balance.ProductId))
            .ToDictionaryAsync(balance => balance.ProductId, balance => balance.Quantity, cancellationToken);

        return new StockTransferDetail(
            summary,
            request.Reason,
            request.CreatedAtUtc,
            request.DecidedAtUtc,
            request.DispatchDocumentId?.Value,
            request.DispatchedAtUtc,
            request.ReceiptDocumentId?.Value,
            request.ReceivedAtUtc,
            [
                .. request.Lines.OrderBy(line => line.LineNumber).Select(line =>
                {
                    var product = products.GetValueOrDefault(line.ProductId);

                    return new StockTransferLineDetail(
                        line.ProductId.Value,
                        product?.Code ?? string.Empty,
                        product?.Description ?? string.Empty,
                        line.UnitId.Value,
                        units.GetValueOrDefault(line.UnitId, string.Empty),
                        product is null ? string.Empty : units.GetValueOrDefault(product.StockUnitId, string.Empty),
                        line.Quantity,
                        line.StockQuantity,
                        line.ApprovedQuantity,
                        line.DispatchedQuantity,
                        onHand.GetValueOrDefault(line.ProductId),
                        product?.TracksBatches ?? false,
                        product?.TracksSerialNumbers ?? false,
                        line.Remarks);
                }),
            ]);
    }

    private async Task<IReadOnlyList<StockTransferSummary>> DescribeAsync(
        FirmId firmId,
        IReadOnlyList<StockTransferRequest> rows,
        CancellationToken cancellationToken)
    {
        Dictionary<WarehouseId, string> warehouses = await _context.Warehouses
            .Where(warehouse => warehouse.FirmId == firmId)
            .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        List<StockDocumentId> documentIds =
        [
            .. rows.SelectMany(row => new[] { row.DispatchDocumentId, row.ReceiptDocumentId })
                .Where(id => id is not null)
                .Select(id => id!.Value),
        ];

        Dictionary<StockDocumentId, string> numbers = documentIds.Count == 0
            ? []
            : await _context.StockDocuments
                .Where(document => documentIds.Contains(document.Id))
                .ToDictionaryAsync(document => document.Id, document => document.Number, cancellationToken);

        return
        [
            .. rows.Select(row => new StockTransferSummary(
                row.Id.Value,
                row.Number,
                row.Date,
                row.FromWarehouseId.Value,
                warehouses.GetValueOrDefault(row.FromWarehouseId, string.Empty),
                row.ToWarehouseId.Value,
                warehouses.GetValueOrDefault(row.ToWarehouseId, string.Empty),
                row.Status,
                row.Lines.Count,
                row.Lines.Sum(line => line.StockQuantity),
                row.Lines.Sum(line => line.DispatchedQuantity),
                row.Narration,
                row.DispatchDocumentId is { } sent ? numbers.GetValueOrDefault(sent) : null,
                row.ReceiptDocumentId is { } received ? numbers.GetValueOrDefault(received) : null)),
        ];
    }
}
