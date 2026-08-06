using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Platform;
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

/// <summary>Reads and writes bills.</summary>
public interface IBillRepository
{
    /// <summary>Loads several bills at once, keyed by identifier.</summary>
    /// <param name="ids">The bills to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bills found, with their allocations, keyed by identifier.</returns>
    /// <remarks>
    /// Allocations are loaded with the bill because settling one appends to them and
    /// re-derives the status from the total. A bill loaded without them would
    /// compute its outstanding amount from an empty collection and let a settled
    /// bill be paid twice.
    /// </remarks>
    Task<IReadOnlyDictionary<BillId, Bill>> GetManyAsync(
        IEnumerable<BillId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a party already has a bill under a reference.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="ledgerId">The party.</param>
    /// <param name="billNumbers">The references to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Those of the supplied references that are already in use.</returns>
    /// <remarks>
    /// A unique index already forbids the duplicate. Checking first turns what would
    /// be a constraint violation surfacing as a 500 into a message naming the
    /// reference that clashed - which is the difference between an operator fixing
    /// their own typo and raising a support ticket.
    /// </remarks>
    Task<IReadOnlySet<string>> FindExistingReferencesAsync(
        FirmId firmId,
        LedgerId ledgerId,
        IEnumerable<string> billNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Loads every bill a voucher allocated against.</summary>
    /// <param name="voucherId">The settling voucher.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bills that voucher settled, with their allocations.</returns>
    /// <remarks>
    /// Wanted when a settlement has to be undone - a cheque that bounces, or a
    /// receipt that is cancelled. The voucher knows what it paid; the bills know
    /// what they were paid by, and this is the direction the reversal needs.
    /// </remarks>
    Task<IReadOnlyList<Bill>> FindAllocatedByAsync(
        VoucherId voucherId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a bill.</summary>
    /// <param name="bill">The bill to add.</param>
    void Add(Bill bill);
}

/// <summary>Reads and writes cheques.</summary>
public interface IChequeRepository
{
    /// <summary>Finds a cheque.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cheque, or <see langword="null"/>.</returns>
    Task<Cheque?> FindAsync(ChequeId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines which of a set of cheque numbers a party already has live.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="partyLedgerId">The party.</param>
    /// <param name="chequeNumbers">The numbers to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Those of the supplied numbers already on an open cheque.</returns>
    /// <remarks>
    /// Only open cheques count, matching the filtered unique index. A cheque that
    /// bounced is re-presented under the same number, so a check over all history
    /// would refuse the very thing that happens next.
    /// </remarks>
    Task<IReadOnlySet<string>> FindLiveNumbersAsync(
        FirmId firmId,
        LedgerId partyLedgerId,
        IEnumerable<string> chequeNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a cheque.</summary>
    /// <param name="cheque">The cheque to add.</param>
    void Add(Cheque cheque);
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

/// <summary>Reads and writes menu entries.</summary>
public interface IMenuItemRepository
{
    /// <summary>Finds a menu entry.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    Task<MenuItem?> FindAsync(MenuItemId id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a firm already uses a menu code.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the code is taken.</returns>
    /// <remarks>
    /// Checked before insert so a duplicate is refused with a message naming the code,
    /// rather than surfacing as a unique-index violation the caller cannot interpret.
    /// The index is still the backstop against two administrators racing.
    /// </remarks>
    Task<bool> CodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the entries sitting directly beneath one.</summary>
    /// <param name="id">The parent entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many entries name it as their parent.</returns>
    /// <remarks>
    /// Deleting a heading must not silently take a subtree of screens with it, so the
    /// caller is refused until the children have been moved. The foreign key restricts
    /// the delete regardless; this turns that into an error somebody can act on.
    /// </remarks>
    Task<int> CountChildrenAsync(
        MenuItemId id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a menu entry.</summary>
    /// <param name="item">The entry to add.</param>
    void Add(MenuItem item);

    /// <summary>Removes a menu entry.</summary>
    /// <param name="item">The entry to remove.</param>
    void Remove(MenuItem item);
}
