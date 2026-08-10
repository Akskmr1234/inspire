using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads serialised units for the selection list and the service desk.</summary>
/// <remarks>
/// Two questions on one shape: which units of this product are on this shelf, and whose
/// is the machine with this number on it. They differ only in the filter, so they share
/// the projection - a unit that read one way on the sales screen and another at the
/// service desk would be two answers about one machine.
/// </remarks>
public sealed class SerialReader : ISerialReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SerialReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SerialReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SerialNumberView>> ForProductAsync(
        FirmId firmId,
        ProductId productId,
        WarehouseId? warehouseId,
        bool includeGone,
        DateOnly asOn,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SerialNumber> units = _context.SerialNumbers
            .Where(serial => serial.FirmId == firmId && serial.ProductId == productId);

        if (warehouseId is { } warehouse)
        {
            units = units.Where(serial => serial.WarehouseId == warehouse);
        }

        if (!includeGone)
        {
            // The two states that mean "on a shelf". A unit back from a customer is
            // available again, and section 12.7 says so plainly.
            units = units.Where(serial =>
                serial.Status == SerialStatus.InStock
                || serial.Status == SerialStatus.ReturnedFromCustomer);
        }

        return await ReadAsync(firmId, units, asOn, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SerialNumberView>> FindAsync(
        FirmId firmId,
        string number,
        DateOnly asOn,
        CancellationToken cancellationToken = default) =>
        await ReadAsync(
            firmId,
            _context.SerialNumbers
                .Where(serial => serial.FirmId == firmId && serial.Number == number),
            asOn,
            cancellationToken);

    private async Task<IReadOnlyList<SerialNumberView>> ReadAsync(
        FirmId firmId,
        IQueryable<SerialNumber> units,
        DateOnly asOn,
        CancellationToken cancellationToken)
    {
        var rows = await units
            .Join(
                _context.Products,
                serial => serial.ProductId,
                product => product.Id,
                (serial, product) => new { serial, product })
            .Select(row => new
            {
                row.serial.Id,
                row.serial.Number,
                row.serial.ProductId,
                row.product.Code,
                row.product.Description,
                row.serial.BatchId,
                row.serial.Status,
                row.serial.WarehouseId,
                row.serial.UnitCost,
                row.serial.ReceivedOn,
                row.serial.IssuedOn,
                row.serial.WarrantyUntil,
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

        List<BatchId> batchIds =
            [.. rows.Where(row => row.BatchId is not null)
                .Select(row => row.BatchId!.Value)
                .Distinct()];

        Dictionary<BatchId, string> batches = batchIds.Count == 0
            ? []
            : await _context.Batches
                .Where(batch => batchIds.Contains(batch.Id))
                .ToDictionaryAsync(batch => batch.Id, batch => batch.Number, cancellationToken);

        return
        [
            .. rows
                .OrderBy(row => row.Code)
                .ThenBy(row => row.Number)
                .Select(row => new SerialNumberView(
                    row.Id.Value,
                    row.Number,
                    row.ProductId.Value,
                    row.Code,
                    row.Description,
                    row.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null,
                    row.Status,
                    row.WarehouseId?.Value,
                    row.WarehouseId is { } held ? warehouses.GetValueOrDefault(held) : null,
                    row.UnitCost,
                    row.ReceivedOn,
                    row.IssuedOn,
                    row.WarrantyUntil,
                    // An unknown term is not a term. Treating a blank as cover would
                    // have a service desk giving away repairs.
                    row.WarrantyUntil is { } until && asOn <= until)),
        ];
    }
}
