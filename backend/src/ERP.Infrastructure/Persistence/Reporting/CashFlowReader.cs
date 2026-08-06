using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the cash position and the postings that moved it.</summary>
/// <remarks>
/// <para>
/// The direct method, expressed as a single idea: for any voucher that touches cash or
/// bank, the accounts on its <em>other</em> lines say what the money was for, and their
/// amounts are what moved. So the report is built from the non-cash lines of
/// cash-touching vouchers rather than from the cash lines themselves.
/// </para>
/// <para>
/// That formulation handles two awkward cases without special-casing either. A transfer
/// from till to bank has no non-cash line and therefore contributes nothing, which is
/// right - moving money between the firm's own accounts does not change what it holds.
/// And a transfer carrying a charge (debit bank 990, debit charges 10, credit cash
/// 1,000) contributes exactly the 10 of charges, which is exactly the net movement.
/// </para>
/// <para>
/// Only posted vouchers count, matching every other report built on the ledgers: a
/// cash flow statement that included drafts would not reconcile with the bank book
/// beside it.
/// </para>
/// </remarks>
public sealed class CashFlowReader : ICashFlowReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="CashFlowReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public CashFlowReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<CashFlowData> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // The accounts whose balance *is* the cash position. Everything else in this
        // reader is expressed relative to this set.
        var cashLedgers = await _context.Ledgers
            .Where(ledger =>
                ledger.FirmId == firmId
                && (ledger.Kind == LedgerKind.Cash || ledger.Kind == LedgerKind.Bank))
            .Select(ledger => new
            {
                ledger.Id,
                ledger.OpeningBalance,
                ledger.OpeningBalanceSide,
            })
            .ToListAsync(cancellationToken);

        if (cashLedgers.Count == 0)
        {
            return new CashFlowData(0m, 0m, []);
        }

        List<LedgerId> cashLedgerIds = [.. cashLedgers.Select(ledger => ledger.Id)];

        // The position brought in from before the system held any postings. Omitting it
        // would make the statement fail to reconcile on any firm that opened its books
        // with money already in the bank - which is all of them.
        decimal storedOpening = cashLedgers.Sum(ledger =>
            ledger.OpeningBalanceSide == EntrySide.Debit
                ? ledger.OpeningBalance
                : -ledger.OpeningBalance);

        decimal openingBalance =
            storedOpening + await SumCashPostingsAsync(firmId, cashLedgerIds, null, from, cancellationToken);

        decimal closingBalance =
            storedOpening + await SumCashPostingsAsync(firmId, cashLedgerIds, null, to.AddDays(1), cancellationToken);

        // Vouchers of the period that touched cash at all. Their identifiers are pulled
        // first so the movement query can be a plain filter rather than a correlated
        // existence check evaluated per line.
        List<VoucherId> cashVoucherIds = await _context.VoucherLines
            .Where(line => cashLedgerIds.Contains(line.LedgerId))
            .Join(
                PostedVouchersIn(firmId, from, to),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line.VoucherId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (cashVoucherIds.Count == 0)
        {
            return new CashFlowData(openingBalance, closingBalance, []);
        }

        // The non-cash lines of those vouchers, which are what says where the money
        // went. A credit here is cash coming in - the cash side of the same voucher was
        // the debit - and a debit is cash going out.
        var movements = await _context.VoucherLines
            .Where(line =>
                cashVoucherIds.Contains(line.VoucherId)
                && !cashLedgerIds.Contains(line.LedgerId))
            .Join(
                _context.Ledgers,
                line => line.LedgerId,
                ledger => ledger.Id,
                (line, ledger) => new { line, ledger })
            .Join(
                _context.AccountGroups,
                pair => pair.ledger.AccountGroupId,
                group => group.Id,
                (pair, group) => new
                {
                    pair.ledger.Id,
                    pair.ledger.Code,
                    pair.ledger.Name,
                    pair.ledger.Kind,
                    group.Nature,
                    pair.line.Side,
                    Amount = pair.line.BaseAmount.Amount,
                })
            .GroupBy(row => new
            {
                row.Id,
                row.Code,
                row.Name,
                row.Kind,
                row.Nature,
            })
            .Select(group => new
            {
                group.Key.Id,
                group.Key.Code,
                group.Key.Name,
                group.Key.Kind,
                group.Key.Nature,
                Inflow = group
                    .Where(row => row.Side == EntrySide.Credit)
                    .Sum(row => (decimal?)row.Amount) ?? 0m,
                Outflow = group
                    .Where(row => row.Side == EntrySide.Debit)
                    .Sum(row => (decimal?)row.Amount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return new CashFlowData(
            openingBalance,
            closingBalance,
            [.. movements.Select(movement => new CashFlowMovement(
                movement.Id.Value,
                movement.Code,
                movement.Name,
                movement.Kind,
                movement.Nature,
                movement.Inflow,
                movement.Outflow))]);
    }

    /// <summary>The posted vouchers of a firm within an optional date range.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The first date, or null for no lower bound.</param>
    /// <param name="to">The last date, or null for no upper bound.</param>
    /// <returns>The query, unexecuted.</returns>
    private IQueryable<Voucher> PostedVouchersIn(FirmId firmId, DateOnly? from, DateOnly? to)
    {
        IQueryable<Voucher> vouchers = _context.Vouchers
            .Where(v => v.FirmId == firmId && v.Status == VoucherStatus.Posted);

        if (from is { } start)
        {
            vouchers = vouchers.Where(v => v.Date >= start);
        }

        if (to is { } end)
        {
            vouchers = vouchers.Where(v => v.Date <= end);
        }

        return vouchers;
    }

    /// <summary>Sums the movement on the cash accounts before a date.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cashLedgerIds">The cash and bank accounts.</param>
    /// <param name="from">The lower bound, or null for none.</param>
    /// <param name="before">The exclusive upper bound.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signed movement, debit-positive.</returns>
    private async Task<decimal> SumCashPostingsAsync(
        FirmId firmId,
        List<LedgerId> cashLedgerIds,
        DateOnly? from,
        DateOnly before,
        CancellationToken cancellationToken) =>
        await _context.VoucherLines
            .Where(line => cashLedgerIds.Contains(line.LedgerId))
            .Join(
                PostedVouchersIn(firmId, from, before.AddDays(-1)),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line)
            .SumAsync(
                line => line.Side == EntrySide.Debit
                    ? line.BaseAmount.Amount
                    : -line.BaseAmount.Amount,
                cancellationToken);
}
