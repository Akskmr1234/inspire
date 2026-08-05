using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Persistence;

/// <summary>
/// Commits the work accumulated in the current unit of work, and runs a group of
/// changes as one transaction.
/// </summary>
/// <remarks>
/// Declared here rather than exposing a <c>DbContext</c> so the Application layer
/// stays free of EF Core. That is not architectural purity for its own sake: it is
/// what stops a use case reaching for <c>ExecuteDelete</c> or an
/// <c>IgnoreQueryFilters</c> that would bypass tenant isolation.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists all pending changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs work inside a single database transaction.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The work to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation's result.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes vouchers.</summary>
public interface IVoucherRepository
{
    /// <summary>Finds a voucher and its lines.</summary>
    /// <param name="id">The voucher.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The voucher, or <see langword="null"/>.</returns>
    Task<Voucher?> FindAsync(VoucherId id, CancellationToken cancellationToken = default);

    /// <summary>Adds a voucher.</summary>
    /// <param name="voucher">The voucher to add.</param>
    void Add(Voucher voucher);
}

/// <summary>Reads ledgers.</summary>
public interface ILedgerRepository
{
    /// <summary>Finds a ledger.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledger, or <see langword="null"/>.</returns>
    Task<Ledger?> FindAsync(LedgerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads several ledgers at once, keyed by identifier.
    /// </summary>
    /// <param name="ids">The ledgers to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledgers found, keyed by identifier.</returns>
    /// <remarks>
    /// A voucher validates every line's ledger. Loading them one at a time would
    /// issue a query per line, which for a fifty-line journal is fifty round trips.
    /// </remarks>
    Task<IReadOnlyDictionary<LedgerId, Ledger>> GetManyAsync(
        IEnumerable<LedgerId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a firm's ledgers with their account groups, for lookup.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="activeOnly">Whether to exclude deactivated ledgers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledgers, each paired with its group, ordered by group then code.</returns>
    /// <remarks>
    /// Returns the group alongside each ledger because every lookup that uses this
    /// shows it - a voucher entry grid needs to distinguish "Sales Account" under
    /// Sales Accounts from a similarly-named ledger elsewhere in the chart.
    /// </remarks>
    Task<IReadOnlyList<(Ledger Ledger, AccountGroup Group)>> ListWithGroupAsync(
        FirmId firmId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads firms.</summary>
public interface IFirmRepository
{
    /// <summary>Finds a firm.</summary>
    /// <param name="id">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The firm, or <see langword="null"/>.</returns>
    Task<Firm?> FindAsync(FirmId id, CancellationToken cancellationToken = default);
}

/// <summary>Reads financial years.</summary>
public interface IFinancialYearRepository
{
    /// <summary>Finds the financial year containing a date.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="date">The document date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year, or <see langword="null"/> when the date falls outside every year.</returns>
    Task<FinancialYear?> FindContainingAsync(
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and creates numbering series.</summary>
public interface INumberingSeriesRepository
{
    /// <summary>
    /// Loads the series governing a document type for a branch and year, taking a
    /// database lock so it can be safely advanced.
    /// </summary>
    /// <param name="documentType">The document type, from <see cref="DocumentTypes"/>.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="financialYearId">The financial year.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The most specific matching series, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Resolution is most-specific-first: a series scoped to both this branch and
    /// this year wins over one scoped to the branch alone, which wins over a
    /// firm-wide series. That is what lets an administrator override numbering for
    /// one branch without redefining it for all of them.
    /// </para>
    /// <para>
    /// The implementation takes a row lock, so two concurrent postings queue rather
    /// than both reading the same next number. The unique index on the document
    /// number is the backstop if that ever fails.
    /// </para>
    /// </remarks>
    Task<NumberingSeries?> FindForUpdateAsync(
        string documentType,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a series.</summary>
    /// <param name="series">The series to add.</param>
    void Add(NumberingSeries series);
}
