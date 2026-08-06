using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Aggregates the vouchers behind the transaction summary.</summary>
/// <remarks>
/// <para>
/// Two grouped queries rather than one. The count is over vouchers and the total is
/// over their lines, and expressing both in a single statement means either counting
/// distinct vouchers across a join that multiplies them by their line count, or a
/// correlated subquery per group. Two aggregations over the same index are cheaper than
/// either, and far easier to be sure of.
/// </para>
/// <para>
/// The total is summed from debit lines only. Debits equal credits by the voucher's own
/// balance invariant, so either side states the value; summing both would report every
/// figure at twice what it is.
/// </para>
/// </remarks>
public sealed class TransactionSummaryReader : ITransactionSummaryReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TransactionSummaryReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TransactionSummaryReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransactionSummaryBucket>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherStatus? status,
        CancellationToken cancellationToken = default)
    {
        // Soft-deleted vouchers are excluded for the same reason the voucher report
        // excludes them: a discarded draft that handed its number back never happened,
        // and a control total that counted it could not be reconciled against anything.
        IQueryable<Voucher> vouchers = _context.Vouchers
            .Where(v => v.FirmId == firmId && !v.IsDeleted && v.Date >= from && v.Date <= to);

        if (status is { } voucherStatus)
        {
            vouchers = vouchers.Where(v => v.Status == voucherStatus);
        }

        var counts = await vouchers
            .GroupBy(v => new
            {
                v.Type,
                v.Status,
                v.Date.Year,
                v.Date.Month,
            })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Status,
                group.Key.Year,
                group.Key.Month,
                VoucherCount = group.Count(),
            })
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return [];
        }

        var totals = await _context.VoucherLines
            .Where(line => line.Side == EntrySide.Debit)
            .Join(
                vouchers,
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => new
                {
                    voucher.Type,
                    voucher.Status,
                    voucher.Date.Year,
                    voucher.Date.Month,
                    Amount = line.BaseAmount.Amount,
                })
            .GroupBy(row => new
            {
                row.Type,
                row.Status,
                row.Year,
                row.Month,
            })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Status,
                group.Key.Year,
                group.Key.Month,
                TotalAmount = group.Sum(row => row.Amount),
            })
            .ToListAsync(cancellationToken);

        Dictionary<(VoucherType, VoucherStatus, int, int), decimal> totalByCell =
            totals.ToDictionary(
                row => (row.Type, row.Status, row.Year, row.Month),
                row => row.TotalAmount);

        List<TransactionSummaryBucket> cells = new(counts.Count);

        // Driven by the counts rather than the totals. A voucher with no debit lines -
        // a draft somebody is midway through entering - has no total, and it must still
        // be counted, because the number of drafts left unposted is one of the things
        // this report is opened to find out.
        foreach (var count in counts)
        {
            cells.Add(new TransactionSummaryBucket(
                count.Type,
                count.Status,
                count.Year,
                count.Month,
                count.VoucherCount,
                totalByCell.GetValueOrDefault(
                    (count.Type, count.Status, count.Year, count.Month))));
        }

        return cells;
    }
}
