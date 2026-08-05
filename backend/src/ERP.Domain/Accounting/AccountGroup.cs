using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Accounting;

/// <summary>
/// Which side of the books an account naturally sits on, and therefore which
/// financial statement it belongs to.
/// </summary>
/// <remarks>
/// The five roots of any double-entry chart of accounts. Everything about
/// presentation follows from this: assets and liabilities form the balance sheet,
/// income and expenses the profit and loss, and equity closes the two together.
/// </remarks>
public enum AccountNature
{
    /// <summary>Something the business owns. Increases with a debit. Balance sheet.</summary>
    Asset = 1,

    /// <summary>Something the business owes. Increases with a credit. Balance sheet.</summary>
    Liability = 2,

    /// <summary>The owners' residual interest. Increases with a credit. Balance sheet.</summary>
    Equity = 3,

    /// <summary>Revenue earned. Increases with a credit. Profit and loss.</summary>
    Income = 4,

    /// <summary>Cost incurred. Increases with a debit. Profit and loss.</summary>
    Expense = 5,
}

/// <summary>
/// A node in the chart of accounts, grouping ledgers and other groups beneath it.
/// </summary>
/// <remarks>
/// <para>
/// Groups form a tree: <c>Assets → Current Assets → Cash and Bank</c>, with
/// ledgers hanging off the leaves. Reports walk this tree, so the hierarchy is
/// what produces a trial balance that sub-totals sensibly rather than listing
/// several hundred ledgers flat.
/// </para>
/// <para>
/// <see cref="Nature"/> is fixed at the root and inherited down. A child cannot
/// contradict its parent - an expense group beneath Assets would make the balance
/// sheet and the profit and loss disagree, and nothing downstream could reconcile
/// them.
/// </para>
/// </remarks>
public sealed class AccountGroup : AggregateRoot<AccountGroupId>, IFirmScoped, IAuditable, ISoftDeletable
{
    private AccountGroup(
        AccountGroupId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        AccountNature nature,
        AccountGroupId? parentId,
        bool isSystemGroup)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        Nature = nature;
        ParentId = parentId;
        IsSystemGroup = isSystemGroup;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private AccountGroup()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the code shown in reports, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the group name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, for RTL presentation.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets which side of the books this group sits on.</summary>
    public AccountNature Nature { get; private set; }

    /// <summary>Gets the parent group, or <see langword="null"/> for a root.</summary>
    public AccountGroupId? ParentId { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the group was seeded and cannot be deleted.
    /// </summary>
    /// <remarks>
    /// The five roots are seeded as system groups. Deleting one would leave
    /// ledgers with no statement to appear on.
    /// </remarks>
    public bool IsSystemGroup { get; private set; }

    /// <summary>
    /// Gets the schedule this group appears under in the statutory financial
    /// statements.
    /// </summary>
    /// <remarks>
    /// The specification lists "Financial Schedules" beside account groups. The
    /// statutory presentation order differs from the internal chart - a balance
    /// sheet groups by schedule, not by the tree an accountant finds convenient -
    /// so it is recorded separately rather than inferred.
    /// </remarks>
    public string? Schedule { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? DeletedBy { get; private set; }

    /// <summary>Gets a value indicating whether a debit increases this group's balance.</summary>
    /// <remarks>
    /// Assets and expenses increase with a debit; liabilities, equity, and income
    /// increase with a credit. Every balance and every report sign depends on this
    /// single fact, so it is derived from <see cref="Nature"/> rather than stored
    /// where it could drift out of step.
    /// </remarks>
    public bool IncreasesWithDebit =>
        Nature is AccountNature.Asset or AccountNature.Expense;

    /// <summary>Gets a value indicating whether this group appears on the balance sheet.</summary>
    public bool IsBalanceSheetGroup =>
        Nature is AccountNature.Asset or AccountNature.Liability or AccountNature.Equity;

    /// <summary>Creates a root group, which defines a nature for everything beneath it.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The group code.</param>
    /// <param name="name">The group name.</param>
    /// <param name="nature">Which side of the books the group sits on.</param>
    /// <param name="isSystemGroup">Whether the group is seeded and undeletable.</param>
    /// <returns>The group, or a validation failure.</returns>
    public static Result<AccountGroup> CreateRoot(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        AccountNature nature,
        bool isSystemGroup = false)
    {
        Result validation = Validate(code, name, nature);

        return validation.IsFailure
            ? Result.Failure<AccountGroup>(validation.Error)
            : Result.Success(new AccountGroup(
                AccountGroupId.NewId(), tenantId, firmId,
                code.Trim().ToUpperInvariant(), name.Trim(), nature,
                parentId: null, isSystemGroup));
    }

    /// <summary>Creates a group beneath an existing one, inheriting its nature.</summary>
    /// <param name="parent">The parent group.</param>
    /// <param name="code">The group code.</param>
    /// <param name="name">The group name.</param>
    /// <returns>The group, or a validation failure.</returns>
    /// <remarks>
    /// The nature is taken from the parent rather than supplied, which makes it
    /// impossible to place an expense group under Assets. Enforcing the rule by
    /// construction is stronger than validating it afterwards.
    /// </remarks>
    public static Result<AccountGroup> CreateChild(
        AccountGroup parent,
        string code,
        string name)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Result validation = Validate(code, name, parent.Nature);

        if (validation.IsFailure)
        {
            return Result.Failure<AccountGroup>(validation.Error);
        }

        AccountGroup child = new(
            AccountGroupId.NewId(), parent.TenantId, parent.FirmId,
            code.Trim().ToUpperInvariant(), name.Trim(), parent.Nature,
            parent.Id, isSystemGroup: false)
        {
            Schedule = parent.Schedule,
        };

        return Result.Success(child);
    }

    /// <summary>Sets the statutory schedule this group is presented under.</summary>
    /// <param name="schedule">The schedule name, or <see langword="null"/> to clear it.</param>
    public void SetSchedule(string? schedule) =>
        Schedule = string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim();

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or <see langword="null"/> to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Renames the group.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "AccountGroup.NameRequired", "An account group name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Checks whether the group may be deleted.</summary>
    /// <returns>Success, or the reason it may not.</returns>
    public Result EnsureDeletable() => IsSystemGroup
        ? Result.Failure(Error.BusinessRule(
            "AccountGroup.SystemGroup",
            $"'{Name}' is a system group and cannot be deleted."))
        : Result.Success();

    private static Result Validate(string code, string name, AccountNature nature)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "AccountGroup.CodeRequired", "An account group code is required."));
        }

        if (code.Trim().Length > 20)
        {
            return Result.Failure(Error.Validation(
                "AccountGroup.CodeTooLong", "An account group code cannot exceed 20 characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "AccountGroup.NameRequired", "An account group name is required."));
        }

        return Enum.IsDefined(nature)
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "AccountGroup.UnknownNature", $"'{nature}' is not a recognised account nature."));
    }
}
