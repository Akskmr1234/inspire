using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>
/// Aggregates postings per ledger for the trial balance.
/// </summary>
/// <remarks>
/// <para>
/// The aggregation runs in PostgreSQL, not in memory. A firm with a year of trading
/// has hundreds of thousands of voucher lines and perhaps two hundred ledgers;
/// loading the lines to sum them client-side would move the whole ledger across the
/// wire to produce two hundred rows.
/// </para>
/// <para>
/// Only <see cref="VoucherStatus.Posted"/> vouchers are counted. Drafts are not in
/// the books, and a cancelled voucher has been reversed out - including either would
/// make the report disagree with the ledgers it claims to summarise.
/// </para>
/// <para>
/// Tenant isolation needs no clause here: the global query filter and the
/// row-level-security policy both apply to <c>voucher_lines</c>, <c>vouchers</c>,
/// and <c>ledgers</c>.
/// </para>
/// </remarks>
public sealed class TrialBalanceReader : ITrialBalanceReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TrialBalanceReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TrialBalanceReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerMovement>> GetMovementsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Postings before the period, netted into a single debit-positive figure per
        // ledger. This is what carries a prior year's closing position forward.
        var priorMovements = await _context.VoucherLines
            .Join(
                _context.Vouchers.Where(v =>
                    v.FirmId == firmId
                    && v.Status == VoucherStatus.Posted
                    && v.Date < from),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line)
            .GroupBy(line => line.LedgerId)
            .Select(g => new
            {
                LedgerId = g.Key,
                Signed = g.Sum(l =>
                    l.Side == EntrySide.Debit
                        ? l.BaseAmount.Amount
                        : -l.BaseAmount.Amount),
            })
            .ToListAsync(cancellationToken);

        // Postings inside the period, kept as separate debit and credit totals
        // because a trial balance shows both columns rather than a net figure.
        var periodMovements = await _context.VoucherLines
            .Join(
                _context.Vouchers.Where(v =>
                    v.FirmId == firmId
                    && v.Status == VoucherStatus.Posted
                    && v.Date >= from
                    && v.Date <= to),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line)
            .GroupBy(line => line.LedgerId)
            .Select(g => new
            {
                LedgerId = g.Key,
                Debit = g.Sum(l => l.Side == EntrySide.Debit ? l.BaseAmount.Amount : 0m),
                Credit = g.Sum(l => l.Side == EntrySide.Credit ? l.BaseAmount.Amount : 0m),
            })
            .ToListAsync(cancellationToken);

        // Every ledger, with its group, so a ledger carrying only a manually-entered
        // opening balance and no postings still appears.
        var ledgers = await _context.Ledgers
            .Where(l => l.FirmId == firmId)
            .Join(
                _context.AccountGroups,
                ledger => ledger.AccountGroupId,
                group => group.Id,
                (ledger, group) => new
                {
                    ledger.Id,
                    ledger.Code,
                    ledger.Name,
                    GroupCode = group.Code,
                    GroupName = group.Name,
                    group.Nature,
                    ledger.OpeningBalance,
                    ledger.OpeningBalanceSide,
                })
            .OrderBy(x => x.GroupCode)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        Dictionary<LedgerId, decimal> priorByLedger =
            priorMovements.ToDictionary(x => x.LedgerId, x => x.Signed);

        Dictionary<LedgerId, (decimal Debit, decimal Credit)> periodByLedger =
            periodMovements.ToDictionary(x => x.LedgerId, x => (x.Debit, x.Credit));

        List<LedgerMovement> results = new(ledgers.Count);

        foreach (var ledger in ledgers)
        {
            // The stored opening balance is the position brought in from before the
            // system was used; prior postings are what accumulated inside it.
            decimal storedOpening = ledger.OpeningBalanceSide == EntrySide.Debit
                ? ledger.OpeningBalance
                : -ledger.OpeningBalance;

            decimal priorPostings = priorByLedger.GetValueOrDefault(ledger.Id, 0m);

            (decimal debit, decimal credit) =
                periodByLedger.GetValueOrDefault(ledger.Id, (0m, 0m));

            results.Add(new LedgerMovement(
                ledger.Id.Value,
                ledger.Code,
                ledger.Name,
                ledger.GroupCode,
                ledger.GroupName,
                ledger.Nature,
                storedOpening + priorPostings,
                debit,
                credit));
        }

        return results;
    }
}
