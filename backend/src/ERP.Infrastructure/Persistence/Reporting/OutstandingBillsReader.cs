using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the open bills behind the outstanding and aging reports.</summary>
/// <remarks>
/// <para>
/// The settled figure is recomputed from the allocations dated on or before the
/// reporting date rather than read from the bill's own <c>SettledAmount</c>. The
/// stored figure is today's; using it would state last month's position with this
/// month's receipts already deducted, so a report re-run for a period end would
/// disagree with the one printed at the time. That is the kind of discrepancy an
/// auditor finds and nobody can explain.
/// </para>
/// <para>
/// It is also why the query cannot simply take unsettled bills: a bill settled last
/// week was open at the end of last month, and must appear in a report as at that
/// date.
/// </para>
/// </remarks>
public sealed class OutstandingBillsReader : IOutstandingBillsReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="OutstandingBillsReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public OutstandingBillsReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutstandingBillRow>> ReadAsync(
        FirmId firmId,
        BillType type,
        DateOnly asAt,
        LedgerId? ledgerId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Bill> bills = _context.Bills
            .Where(b => b.FirmId == firmId && b.Type == type && b.BillDate <= asAt);

        if (ledgerId is { } party)
        {
            bills = bills.Where(b => b.LedgerId == party);
        }

        // Settled bills are excluded unless something was allocated to them after the
        // reporting date, in which case they were still open on it. For the ordinary
        // case - a report as at today - no such allocation exists, and this collapses
        // to the filtered index covering open bills by party.
        bills = bills.Where(b =>
            b.Status != BillStatus.Settled
            || _context.BillAllocations.Any(a => a.BillId == b.Id && a.AllocatedOn > asAt));

        var candidates = await bills
            .Join(
                _context.Ledgers,
                bill => bill.LedgerId,
                ledger => ledger.Id,
                (bill, ledger) => new
                {
                    bill.Id,
                    LedgerId = ledger.Id,
                    LedgerCode = ledger.Code,
                    LedgerName = ledger.Name,
                    bill.BillNumber,
                    bill.BillDate,
                    bill.DueDate,
                    OriginalAmount = bill.OriginalAmount.Amount,
                    Currency = bill.OriginalAmount.Currency,
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        List<BillId> billIds = [.. candidates.Select(c => c.Id)];

        // Summed in one query rather than per bill. Loading them inside the loop
        // would be a round trip per bill, which on a debtors report covering a
        // year's invoices is thousands.
        Dictionary<BillId, decimal> settled = await _context.BillAllocations
            .Where(a => billIds.Contains(a.BillId) && a.AllocatedOn <= asAt)
            .GroupBy(a => a.BillId)
            .Select(group => new
            {
                BillId = group.Key,
                Amount = group.Sum(a => a.Amount.Amount),
            })
            .ToDictionaryAsync(row => row.BillId, row => row.Amount, cancellationToken);

        List<OutstandingBillRow> rows = new(candidates.Count);

        foreach (var candidate in candidates)
        {
            decimal allocated = settled.GetValueOrDefault(candidate.Id);

            // A bill fully settled on or before the reporting date is not
            // outstanding, and one whose only allocations came later is - which is
            // exactly what the recomputed figure decides.
            if (candidate.OriginalAmount <= allocated)
            {
                continue;
            }

            rows.Add(new OutstandingBillRow(
                candidate.Id.Value,
                candidate.LedgerId.Value,
                candidate.LedgerCode,
                candidate.LedgerName,
                candidate.BillNumber,
                candidate.BillDate,
                candidate.DueDate,
                candidate.OriginalAmount,
                allocated,
                candidate.Currency.Code));
        }

        return rows;
    }
}
