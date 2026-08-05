using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tenancy;

/// <summary>The lifecycle state of a financial year.</summary>
public enum FinancialYearStatus
{
    /// <summary>Accepting postings.</summary>
    Open = 1,

    /// <summary>
    /// Closed to new postings, but reopenable by an authorised user - the state a
    /// year sits in between period-end and the audit signing off.
    /// </summary>
    Closed = 2,

    /// <summary>
    /// Permanently sealed after audit. Nothing may post, and it cannot be
    /// reopened.
    /// </summary>
    Locked = 3,
}

/// <summary>
/// An accounting period against which documents are posted.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately an arbitrary date range rather than a calendar or April-March
/// year. The legacy system this replaces runs a year from 01-10-2021 to
/// 31-12-2026, and the platform must serve both Gulf and Indian firms whose
/// statutory years differ. Any assumption about year length or start month would
/// be wrong for someone.
/// </para>
/// <para>
/// Financial years are firm-scoped, not branch-scoped: branches within a firm
/// share one set of books and therefore one set of periods, even though they
/// number their documents separately.
/// </para>
/// </remarks>
public sealed class FinancialYear : AggregateRoot<FinancialYearId>, IFirmScoped, IAuditable
{
    private FinancialYear(
        FinancialYearId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        DateOnly startDate,
        DateOnly endDate)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        StartDate = startDate;
        EndDate = endDate;
        Status = FinancialYearStatus.Open;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private FinancialYear()
    {
        Code = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>
    /// Gets the label users see and which appears in document numbers, such as
    /// <c>2026</c> in <c>SL/2026/0001</c>.
    /// </summary>
    public string Code { get; private set; }

    /// <summary>Gets the first date on which the year accepts postings, inclusive.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Gets the last date on which the year accepts postings, inclusive.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public FinancialYearStatus Status { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets a value indicating whether postings are currently permitted.</summary>
    public bool IsOpen => Status == FinancialYearStatus.Open;

    /// <summary>Creates a financial year.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The label, for example <c>2026</c> or <c>2025-26</c>.</param>
    /// <param name="startDate">The first date, inclusive.</param>
    /// <param name="endDate">The last date, inclusive.</param>
    /// <param name="existingYears">
    /// The firm's existing years, checked for overlap. Pass an empty sequence for
    /// the first year.
    /// </param>
    /// <returns>The year, or a failure explaining why it is invalid.</returns>
    public static Result<FinancialYear> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        DateOnly startDate,
        DateOnly endDate,
        IEnumerable<FinancialYear> existingYears)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<FinancialYear>(Error.Validation(
                "FinancialYear.CodeRequired",
                "A financial year code is required."));
        }

        if (code.Trim().Length > 20)
        {
            return Result.Failure<FinancialYear>(Error.Validation(
                "FinancialYear.CodeTooLong",
                "A financial year code cannot exceed 20 characters."));
        }

        if (endDate < startDate)
        {
            return Result.Failure<FinancialYear>(Error.Validation(
                "FinancialYear.EndBeforeStart",
                $"The end date ({endDate:yyyy-MM-dd}) cannot precede the start date " +
                $"({startDate:yyyy-MM-dd})."));
        }

        // Overlapping years would make the period a document posts into
        // ambiguous, and with it every balance derived from that period.
        foreach (FinancialYear existing in existingYears)
        {
            if (existing.OverlapsWith(startDate, endDate))
            {
                return Result.Failure<FinancialYear>(Error.Conflict(
                    "FinancialYear.Overlaps",
                    $"The range {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} overlaps " +
                    $"financial year '{existing.Code}' " +
                    $"({existing.StartDate:yyyy-MM-dd} to {existing.EndDate:yyyy-MM-dd})."));
            }

            if (string.Equals(existing.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<FinancialYear>(Error.Conflict(
                    "FinancialYear.DuplicateCode",
                    $"A financial year with code '{code.Trim()}' already exists for this firm."));
            }
        }

        return Result.Success(new FinancialYear(
            FinancialYearId.NewId(),
            tenantId,
            firmId,
            code.Trim(),
            startDate,
            endDate));
    }

    /// <summary>Determines whether a date falls within this year, inclusive of both ends.</summary>
    /// <param name="date">The date to test.</param>
    /// <returns><see langword="true"/> when the date is in range.</returns>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>
    /// Determines whether a candidate range overlaps this year by even one day.
    /// </summary>
    /// <param name="otherStart">The candidate start, inclusive.</param>
    /// <param name="otherEnd">The candidate end, inclusive.</param>
    /// <returns><see langword="true"/> when the ranges intersect.</returns>
    public bool OverlapsWith(DateOnly otherStart, DateOnly otherEnd) =>
        otherStart <= EndDate && otherEnd >= StartDate;

    /// <summary>
    /// Determines whether a document dated <paramref name="date"/> may be posted.
    /// </summary>
    /// <param name="date">The proposed document date.</param>
    /// <returns>Success when posting is permitted, otherwise the reason it is not.</returns>
    /// <remarks>
    /// The single gate every posting passes through. Both conditions matter: a
    /// date outside the range belongs to a different period, and a closed year
    /// must not accept new entries or previously published statements would
    /// silently change.
    /// </remarks>
    public Result CanPostOn(DateOnly date)
    {
        if (!Contains(date))
        {
            return Result.Failure(Error.BusinessRule(
                "FinancialYear.DateOutOfRange",
                $"{date:yyyy-MM-dd} falls outside financial year '{Code}' " +
                $"({StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd})."));
        }

        return Status switch
        {
            FinancialYearStatus.Open => Result.Success(),
            FinancialYearStatus.Closed => Result.Failure(Error.BusinessRule(
                "FinancialYear.Closed",
                $"Financial year '{Code}' is closed. Reopen it before posting.")),
            FinancialYearStatus.Locked => Result.Failure(Error.BusinessRule(
                "FinancialYear.Locked",
                $"Financial year '{Code}' is locked after audit and cannot accept postings.")),
            _ => Result.Failure(Error.Unexpected(
                "FinancialYear.UnknownStatus",
                $"Financial year '{Code}' has an unrecognised status.")),
        };
    }

    /// <summary>Closes the year to new postings.</summary>
    /// <returns>Success, or a failure when the year is already locked.</returns>
    public Result Close()
    {
        if (Status == FinancialYearStatus.Locked)
        {
            return Result.Failure(Error.BusinessRule(
                "FinancialYear.AlreadyLocked",
                $"Financial year '{Code}' is locked and cannot be closed again."));
        }

        Status = FinancialYearStatus.Closed;
        Raise(new FinancialYearClosed(Id, FirmId, Code));
        return Result.Success();
    }

    /// <summary>Reopens a closed year.</summary>
    /// <returns>Success, or a failure when the year is locked.</returns>
    public Result Reopen()
    {
        if (Status == FinancialYearStatus.Locked)
        {
            return Result.Failure(Error.BusinessRule(
                "FinancialYear.LockedCannotReopen",
                $"Financial year '{Code}' was locked after audit and cannot be reopened."));
        }

        Status = FinancialYearStatus.Open;
        Raise(new FinancialYearReopened(Id, FirmId, Code));
        return Result.Success();
    }

    /// <summary>Seals the year permanently after audit.</summary>
    /// <returns>Success, or a failure when the year is still open.</returns>
    /// <remarks>
    /// Requires the year to be closed first. Locking directly from open would let
    /// a mis-click permanently seal a period that still needed adjusting entries,
    /// and by design there is no way back.
    /// </remarks>
    public Result Lock()
    {
        if (Status == FinancialYearStatus.Open)
        {
            return Result.Failure(Error.BusinessRule(
                "FinancialYear.MustCloseBeforeLocking",
                $"Financial year '{Code}' must be closed before it can be locked."));
        }

        Status = FinancialYearStatus.Locked;
        Raise(new FinancialYearLocked(Id, FirmId, Code));
        return Result.Success();
    }
}

/// <summary>Raised when a financial year is closed to postings.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="Code">The year's label.</param>
public sealed record FinancialYearClosed(
    FinancialYearId FinancialYearId,
    FirmId FirmId,
    string Code) : DomainEvent;

/// <summary>Raised when a closed financial year is reopened.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="Code">The year's label.</param>
public sealed record FinancialYearReopened(
    FinancialYearId FinancialYearId,
    FirmId FirmId,
    string Code) : DomainEvent;

/// <summary>Raised when a financial year is permanently sealed after audit.</summary>
/// <param name="FinancialYearId">The year.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="Code">The year's label.</param>
public sealed record FinancialYearLocked(
    FinancialYearId FinancialYearId,
    FirmId FirmId,
    string Code) : DomainEvent;
