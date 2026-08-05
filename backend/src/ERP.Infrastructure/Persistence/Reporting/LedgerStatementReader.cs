using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the postings behind a statement of account.</summary>
/// <remarks>
/// Only <see cref="VoucherStatus.Posted"/> vouchers are counted, matching the
/// trial balance. A statement that included drafts would not reconcile with the
/// balance the same ledger shows on the trial balance, which is the first thing an
/// accountant checks.
/// </remarks>
public sealed class LedgerStatementReader : ILedgerStatementReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="LedgerStatementReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public LedgerStatementReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<LedgerStatementData?> ReadAsync(
        LedgerId ledgerId,
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var ledger = await _context.Ledgers
            .Where(l => l.Id == ledgerId && l.FirmId == firmId)
            .Join(
                _context.AccountGroups,
                l => l.AccountGroupId,
                g => g.Id,
                (l, g) => new
                {
                    l.Code,
                    l.Name,
                    GroupName = g.Name,
                    l.OpeningBalance,
                    l.OpeningBalanceSide,
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        // The balance brought forward: the ledger's stored opening position plus
        // every posting dated before the period.
        decimal priorPostings = await _context.VoucherLines
            .Where(line => line.LedgerId == ledgerId)
            .Join(
                _context.Vouchers.Where(v =>
                    v.FirmId == firmId && v.Status == VoucherStatus.Posted && v.Date < from),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line)
            .SumAsync(
                line => line.Side == EntrySide.Debit
                    ? line.BaseAmount.Amount
                    : -line.BaseAmount.Amount,
                cancellationToken);

        decimal storedOpening = ledger.OpeningBalanceSide == EntrySide.Debit
            ? ledger.OpeningBalance
            : -ledger.OpeningBalance;

        // The postings themselves, joined to their voucher for the date, number, and
        // narration fallback.
        var postings = await _context.VoucherLines
            .Where(line => line.LedgerId == ledgerId)
            .Join(
                _context.Vouchers.Where(v =>
                    v.FirmId == firmId
                    && v.Status == VoucherStatus.Posted
                    && v.Date >= from
                    && v.Date <= to),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => new
                {
                    voucher.Id,
                    voucher.Date,
                    voucher.Number,
                    voucher.Type,
                    voucher.ReferenceNumber,
                    VoucherNarration = voucher.Narration,
                    LineNarration = line.Narration,
                    line.Side,
                    Amount = line.BaseAmount.Amount,
                    line.LineNumber,
                })
            // Number as the tie-break, so two vouchers on the same date always
            // appear in the same order. Without it the statement's row order - and
            // therefore its running balance column - could differ between runs.
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Number)
            .ThenBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        // The contra ledgers, fetched in one query for every voucher involved rather
        // than one query per posting.
        //
        // The list is of VoucherId, not Guid, and the projection keeps the whole
        // identifier. The registered value converter has already collapsed VoucherId
        // to a single uuid column, so reaching into .Value inside an expression asks
        // EF Core to navigate inside a scalar and fails to translate - the same trap
        // documented on ErpDbContext.CurrentTenant.
        List<VoucherId> voucherIds = [.. postings.Select(p => p.Id).Distinct()];

        var contras = await _context.VoucherLines
            .Where(line => voucherIds.Contains(line.VoucherId) && line.LedgerId != ledgerId)
            .Join(
                _context.Ledgers,
                line => line.LedgerId,
                other => other.Id,
                (line, other) => new
                {
                    line.VoucherId,
                    line.Side,
                    other.Name,
                })
            .ToListAsync(cancellationToken);

        Dictionary<VoucherId, List<(EntrySide Side, string Name)>> contrasByVoucher = contras
            .GroupBy(c => c.VoucherId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.Side, c.Name)).ToList());

        List<LedgerPosting> result = new(postings.Count);

        foreach (var posting in postings)
        {
            // Only the opposite side counts as a contra. On a multi-line voucher the
            // same-side entries are siblings, not the counterpart, and listing them
            // would make the particulars column misleading.
            IReadOnlyList<string> contraNames =
                contrasByVoucher.TryGetValue(posting.Id, out var all)
                    ? [.. all.Where(c => c.Side != posting.Side)
                             .Select(c => c.Name)
                             .Distinct()]
                    : [];

            result.Add(new LedgerPosting(
                posting.Date,
                posting.Id.Value,
                posting.Number,
                posting.Type,
                posting.ReferenceNumber,
                posting.LineNarration ?? posting.VoucherNarration,
                contraNames,
                posting.Side,
                posting.Amount));
        }

        return new LedgerStatementData(
            ledger.Code,
            ledger.Name,
            ledger.GroupName,
            storedOpening + priorPostings,
            result);
    }
}
