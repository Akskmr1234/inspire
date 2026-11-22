using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>The PostgreSQL implementation of <see cref="IPosStore"/>.</summary>
public sealed class PosStore : IPosStore
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PosStore"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PosStore(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<PosShift?> FindOpenShiftAsync(
        FirmId firmId,
        UserId userId,
        CancellationToken cancellationToken = default) =>
        _context.PosShifts
            .Where(shift => shift.FirmId == firmId && shift.UserId == userId && shift.ClosedAtUtc == null)
            .OrderByDescending(shift => shift.OpenedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<PosShift?> FindShiftAsync(PosShiftId id, CancellationToken cancellationToken = default) =>
        _context.PosShifts.FirstOrDefaultAsync(shift => shift.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountOpenShiftsAsync(
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _context.PosShifts.CountAsync(
            shift => shift.FirmId == firmId && shift.BusinessDate <= date && shift.ClosedAtUtc == null,
            cancellationToken);

    /// <inheritdoc />
    public Task<PosDayClose?> FindDayCloseAsync(
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _context.PosDayCloses.FirstOrDefaultAsync(
            close => close.FirmId == firmId && close.BusinessDate == date,
            cancellationToken);

    /// <inheritdoc />
    public async Task<PosTakings> TakingsAsync(
        FirmId firmId,
        DateOnly? date,
        UserId? userId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SalesInvoice> invoices = _context.SalesInvoices.AsNoTracking()
            .Where(invoice => invoice.FirmId == firmId && invoice.Status == SalesInvoiceStatus.Posted);

        if (date is { } day)
        {
            invoices = invoices.Where(invoice => invoice.Date == day);
        }

        if (userId is { } user)
        {
            invoices = invoices.Where(invoice => invoice.CreatedBy == user);
        }

        if (fromUtc is { } from)
        {
            invoices = invoices.Where(invoice => invoice.CreatedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            invoices = invoices.Where(invoice => invoice.CreatedAtUtc <= to);
        }

        // A bill's total is worked out from its lines and charges rather than stored, so
        // the bills are read whole and added up here - a day's or a shift's are few.
        List<SalesInvoice> loaded = await invoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.Charges)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var groups = loaded
            .Select(invoice => new { invoice.Kind, invoice.PaymentMode, invoice.Total.Amount })
            .GroupBy(bill => new { bill.Kind, bill.PaymentMode })
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.PaymentMode,
                Count = group.Count(),
                Total = group.Sum(bill => bill.Amount),
            });

        int salesCount = 0;
        int returnsCount = 0;
        decimal sales = 0m;
        decimal returns = 0m;
        decimal cash = 0m;
        decimal card = 0m;
        decimal online = 0m;
        decimal credit = 0m;

        foreach (var group in groups)
        {
            bool isReturn = group.Kind == SalesDocumentKind.Return;
            decimal signed = isReturn ? -group.Total : group.Total;

            if (isReturn)
            {
                returnsCount += group.Count;
                returns += group.Total;
            }
            else
            {
                salesCount += group.Count;
                sales += group.Total;
            }

            switch (group.PaymentMode)
            {
                case SalesPaymentMode.Cash:
                    cash += signed;
                    break;
                case SalesPaymentMode.Card:
                    card += signed;
                    break;
                case SalesPaymentMode.Online:
                    online += signed;
                    break;
                default:
                    credit += signed;
                    break;
            }
        }

        return new PosTakings(salesCount, returnsCount, sales, returns, cash, card, online, credit);
    }

    /// <inheritdoc />
    public void Add(PosShift shift) => _context.PosShifts.Add(shift);

    /// <inheritdoc />
    public void Add(PosDayClose close) => _context.PosDayCloses.Add(close);
}
