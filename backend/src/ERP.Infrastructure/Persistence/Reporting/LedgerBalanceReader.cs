using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads one ledger's balance, summed in PostgreSQL.</summary>
/// <remarks>
/// Posted vouchers only, as the trial balance counts them: a draft is not in the books and
/// a cancelled voucher has been reversed out, so including either would give a counter a
/// balance the ledger statement disagrees with.
/// </remarks>
public sealed class LedgerBalanceReader : ILedgerBalanceReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="LedgerBalanceReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public LedgerBalanceReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<decimal> GetBalanceAsync(
        FirmId firmId,
        LedgerId ledgerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var opening = await _context.Ledgers
            .Where(ledger => ledger.Id == ledgerId && ledger.FirmId == firmId)
            .Select(ledger => new { ledger.OpeningBalance, ledger.OpeningBalanceSide })
            .FirstOrDefaultAsync(cancellationToken);

        if (opening is null)
        {
            return 0m;
        }

        decimal postings = await _context.VoucherLines
            .Where(line => line.LedgerId == ledgerId)
            .Join(
                _context.Vouchers.Where(voucher =>
                    voucher.FirmId == firmId
                    && voucher.Status == VoucherStatus.Posted
                    && voucher.Date <= asAt),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => line)
            .SumAsync(
                line => line.Side == EntrySide.Debit
                    ? line.BaseAmount.Amount
                    : -line.BaseAmount.Amount,
                cancellationToken);

        decimal stored = opening.OpeningBalanceSide == EntrySide.Debit
            ? opening.OpeningBalance
            : -opening.OpeningBalance;

        return stored + postings;
    }
}
