using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads pages of sales orders for a list.</summary>
/// <remarks>
/// The page is taken in the database and the totals worked out afterwards from the orders
/// themselves, as the invoice list does and for the same reason: the rounding rule lives
/// on the aggregate, and a second spelling of it in SQL would eventually disagree with the
/// document it lists.
/// </remarks>
public sealed class SalesOrderReader : ISalesOrderReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesOrderReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesOrderReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<SalesOrderSummary>> ListAsync(
        FirmId firmId,
        SalesOrderFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<SalesOrder> matching = Narrow(
            _context.SalesOrders.Where(order => order.FirmId == firmId), filter);

        int total = await matching.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult.Empty<SalesOrderSummary>(page, pageSize);
        }

        List<SalesOrderId> ids = await matching
            .OrderByDescending(order => order.Date)
            .ThenByDescending(order => order.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        List<SalesOrder> orders = await _context.SalesOrders
            .Include(order => order.Lines)
            .Include(order => order.Charges)
            .Where(order => ids.Contains(order.Id))
            .ToListAsync(cancellationToken);

        Dictionary<LedgerId, Ledger> customers = await _context.Ledgers
            .Where(ledger => orders.Select(o => o.CustomerLedgerId).Contains(ledger.Id))
            .ToDictionaryAsync(ledger => ledger.Id, cancellationToken);

        List<SalesOrderSummary> rows =
        [
            .. ids
                .Select(id => orders.Find(order => order.Id == id))
                .Where(order => order is not null)
                .Select(order => Describe(order!, customers)),
        ];

        return new PagedResult<SalesOrderSummary>(rows, page, pageSize, total);
    }

    private static IQueryable<SalesOrder> Narrow(
        IQueryable<SalesOrder> orders,
        SalesOrderFilter filter)
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

        if (filter.CustomerLedgerId is { } customer)
        {
            orders = orders.Where(order => order.CustomerLedgerId == customer);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();

            orders = orders.Where(order =>
                EF.Functions.ILike(order.Number, $"%{term}%")
                || (order.ReferenceNumber != null
                    && EF.Functions.ILike(order.ReferenceNumber, $"%{term}%")));
        }

        // What a warehouse asks for: confirmed orders with something still owed. Expressed
        // as a state plus a line condition rather than a stored flag, so it cannot drift
        // from the quantities it is derived from.
        if (filter.OutstandingOnly)
        {
            orders = orders.Where(order =>
                order.Status == SalesOrderStatus.Confirmed
                && order.Lines.Any(line => line.InvoicedQuantity < line.Quantity));
        }

        return orders;
    }

    private static SalesOrderSummary Describe(
        SalesOrder order,
        IReadOnlyDictionary<LedgerId, Ledger> customers)
    {
        Ledger? customer = customers.GetValueOrDefault(order.CustomerLedgerId);

        return new SalesOrderSummary(
            order.Id.Value,
            order.Number,
            order.Date,
            order.ExpectedOn,
            order.CustomerLedgerId.Value,
            customer?.Code ?? string.Empty,
            customer?.Name ?? string.Empty,
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
