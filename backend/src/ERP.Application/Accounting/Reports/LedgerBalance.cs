using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Reports;

/// <summary>Reads where one ledger stands: its opening balance and everything posted to it.</summary>
/// <remarks>
/// Its own reader rather than a trial balance filtered to one row. A sales counter asks
/// for one customer's balance with every invoice it opens, and aggregating the whole
/// firm's postings to answer it would make the counter wait on every other account.
/// </remarks>
public interface ILedgerBalanceReader
{
    /// <summary>Reads a ledger's balance as at a date.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="ledgerId">The ledger.</param>
    /// <param name="asAt">The last date included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The balance in debit-positive terms, in the firm's base currency: what a customer
    /// owes is positive, what the firm owes them is negative.
    /// </returns>
    Task<decimal> GetBalanceAsync(
        FirmId firmId,
        LedgerId ledgerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);
}
