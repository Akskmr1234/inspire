using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the vouchers behind the day book.</summary>
/// <remarks>
/// Only <see cref="VoucherStatus.Posted"/> vouchers appear, matching the trial
/// balance and the statement of account. A day book that included drafts would not
/// reconcile with the balances derived from the same postings, which is the first
/// thing an accountant checks.
/// </remarks>
public sealed class DayBookReader : IDayBookReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="DayBookReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public DayBookReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DayBookEntry>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherType? voucherType,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Voucher> vouchers = _context.Vouchers
            .Where(v =>
                v.FirmId == firmId
                && v.Status == VoucherStatus.Posted
                && v.Date >= from
                && v.Date <= to);

        if (voucherType.HasValue)
        {
            vouchers = vouchers.Where(v => v.Type == voucherType.Value);
        }

        // Ordered by date, then by document number. Entry order within a day is not
        // recorded separately, and the number is what an accountant reads the
        // register by - it is also what makes the output stable across requests,
        // which an unordered query would not be.
        var headers = await vouchers
            .OrderBy(v => v.Date)
            .ThenBy(v => v.Number)
            .Select(v => new
            {
                v.Id,
                v.Date,
                v.Number,
                v.Type,
                v.ReferenceNumber,
                v.Narration,
            })
            .ToListAsync(cancellationToken);

        if (headers.Count == 0)
        {
            return [];
        }

        List<VoucherId> voucherIds = [.. headers.Select(h => h.Id)];

        // The lines are fetched in one query rather than per voucher. Loading them
        // inside the loop would issue one round trip per voucher, which on a busy
        // day is several hundred - the classic N+1 that only shows up once real
        // data arrives.
        var lines = await _context.VoucherLines
            .Where(line => voucherIds.Contains(line.VoucherId))
            .Join(
                _context.Ledgers,
                line => line.LedgerId,
                ledger => ledger.Id,
                (line, ledger) => new
                {
                    line.VoucherId,
                    LedgerId = ledger.Id,
                    LedgerCode = ledger.Code,
                    LedgerName = ledger.Name,
                    line.Narration,
                    line.Side,
                    Amount = line.BaseAmount.Amount,
                    line.LineNumber,
                })
            .ToListAsync(cancellationToken);

        ILookup<VoucherId, DayBookLine> linesByVoucher = lines
            // Entry order, which is what the voucher document itself shows. Sorting
            // by anything else would make the register disagree with the printed
            // voucher a reader is holding beside it.
            .OrderBy(l => l.LineNumber)
            .ToLookup(
                l => l.VoucherId,
                l => new DayBookLine(
                    l.LedgerId.Value,
                    l.LedgerCode,
                    l.LedgerName,
                    l.Narration,
                    l.Side == EntrySide.Debit ? l.Amount : 0m,
                    l.Side == EntrySide.Credit ? l.Amount : 0m));

        List<DayBookEntry> entries = new(headers.Count);

        foreach (var header in headers)
        {
            IReadOnlyList<DayBookLine> entryLines = [.. linesByVoucher[header.Id]];

            entries.Add(new DayBookEntry(
                header.Id.Value,
                header.Date,
                header.Number,
                header.Type,
                header.ReferenceNumber,
                header.Narration,
                DebitTotalOf(entryLines),
                entryLines));
        }

        return entries;
    }

    /// <summary>States a voucher's value as the sum of its debits.</summary>
    /// <param name="lines">The voucher's lines.</param>
    /// <returns>The total debits.</returns>
    /// <remarks>
    /// Debits equal credits by the voucher aggregate's own invariant, so either
    /// side states the amount. Debits are the conventional choice.
    /// </remarks>
    private static decimal DebitTotalOf(IReadOnlyList<DayBookLine> lines)
    {
        decimal total = 0m;

        foreach (DayBookLine line in lines)
        {
            total += line.Debit;
        }

        return total;
    }
}
