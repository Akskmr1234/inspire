using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Purchase;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads pages of purchase orders for a list.</summary>
/// <remarks>
/// The page is taken in the database and the totals worked out afterwards from the orders
/// themselves, as the purchase list does and for the same reason: the rounding rule lives on
/// the aggregate, and a second spelling of it in SQL would eventually disagree with the
/// document it lists.
/// </remarks>
public sealed class PurchaseOrderReader : IPurchaseOrderReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PurchaseOrderReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PurchaseOrderReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<PurchaseOrderSummary>> ListAsync(
        FirmId firmId,
        PurchaseOrderFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<PurchaseOrder> matching = Narrow(
            _context.PurchaseOrders.Where(order => order.FirmId == firmId), filter);

        int total = await matching.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult.Empty<PurchaseOrderSummary>(page, pageSize);
        }

        List<PurchaseOrderId> ids = await matching
            .OrderByDescending(order => order.Date)
            .ThenByDescending(order => order.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        List<PurchaseOrder> orders = await _context.PurchaseOrders
            .Include(order => order.Lines)
            .Include(order => order.Charges)
            .Where(order => ids.Contains(order.Id))
            .ToListAsync(cancellationToken);

        Dictionary<LedgerId, Ledger> suppliers = await _context.Ledgers
            .Where(ledger => orders.Select(o => o.SupplierLedgerId).Contains(ledger.Id))
            .ToDictionaryAsync(ledger => ledger.Id, cancellationToken);

        List<PurchaseOrderSummary> rows =
        [
            .. ids
                .Select(id => orders.Find(order => order.Id == id))
                .Where(order => order is not null)
                .Select(order => Describe(order!, suppliers)),
        ];

        return new PagedResult<PurchaseOrderSummary>(rows, page, pageSize, total);
    }

    private static IQueryable<PurchaseOrder> Narrow(
        IQueryable<PurchaseOrder> orders,
        PurchaseOrderFilter filter)
    {
        if (filter.From is { } from)
        {
            orders = orders.Where(order => order.Date >= from);
        }

        if (filter.To is { } to)
        {
            orders = orders.Where(order => order.Date <= to);
        }

        if (filter.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (filter.SupplierLedgerId is { } supplier)
        {
            orders = orders.Where(order => order.SupplierLedgerId == supplier);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();

            orders = orders.Where(order =>
                EF.Functions.ILike(order.Number, $"%{term}%")
                || (order.ReferenceNumber != null
                    && EF.Functions.ILike(order.ReferenceNumber, $"%{term}%")));
        }

        // What a buyer asks for: confirmed orders with something still owed, which is the
        // chase list. Expressed as a state plus a line condition rather than a stored flag,
        // so it cannot drift from the quantities it is derived from.
        if (filter.OutstandingOnly)
        {
            orders = orders.Where(order =>
                order.Status == PurchaseOrderStatus.Confirmed
                && order.Lines.Any(line => line.InvoicedQuantity < line.Quantity));
        }

        return orders;
    }

    private static PurchaseOrderSummary Describe(
        PurchaseOrder order,
        IReadOnlyDictionary<LedgerId, Ledger> suppliers)
    {
        Ledger? supplier = suppliers.GetValueOrDefault(order.SupplierLedgerId);

        return new PurchaseOrderSummary(
            order.Id.Value,
            order.Number,
            order.Date,
            order.ExpectedOn,
            order.SupplierLedgerId.Value,
            supplier?.Code ?? string.Empty,
            supplier?.Name ?? string.Empty,
            order.Status,
            order.Currency.Code,
            order.ReferenceNumber,
            order.Lines.Count,
            order.Lines.Count(line => !line.IsFulfilled),
            order.Taxable.Amount,
            order.Tax.Amount,
            order.Total.Amount);
    }
}
