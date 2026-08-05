using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tenancy;

/// <summary>
/// A trading location within a firm, surfaced to users as "Stock Location" or
/// "Store Location".
/// </summary>
/// <remarks>
/// <para>
/// Branches share their firm's books and chart of accounts but keep their own
/// stock, document numbering, print formats, dashboards, permissions, and theme.
/// A document belongs to exactly one branch; a master usually belongs to the firm
/// and is shared across all of them.
/// </para>
/// <para>
/// Created through <see cref="Firm.AddBranch"/> rather than directly, because
/// code uniqueness and the single-head-office rule are firm-wide invariants.
/// </para>
/// </remarks>
public sealed class Branch : Entity<BranchId>, IFirmScoped, IAuditable, ISoftDeletable
{
    private Branch(
        BranchId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        bool isHeadOffice,
        string timeZoneId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        IsHeadOffice = isHeadOffice;
        TimeZoneId = timeZoneId;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Branch()
    {
        Code = string.Empty;
        Name = string.Empty;
        TimeZoneId = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch code, unique within the firm and used in document numbering.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the branch name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, shown when the interface is in RTL mode.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets a value indicating whether this branch is the firm's head office.</summary>
    public bool IsHeadOffice { get; private set; }

    /// <summary>
    /// Gets the IANA time-zone identifier, defaulted from the firm but
    /// overridable.
    /// </summary>
    /// <remarks>
    /// A firm may legitimately span time zones. The day-book cut-off and Z-report
    /// boundary follow the branch, because that is where the till actually closes.
    /// </remarks>
    public string TimeZoneId { get; private set; }

    /// <summary>Gets a value indicating whether the branch may be transacted against.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the first address line.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>Gets the second address line.</summary>
    public string? AddressLine2 { get; private set; }

    /// <summary>Gets the contact telephone number.</summary>
    public string? Phone { get; private set; }

    /// <summary>Gets the contact email address.</summary>
    public string? Email { get; private set; }

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

    /// <summary>Creates a branch.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The branch code, already normalised by the firm.</param>
    /// <param name="name">The branch name.</param>
    /// <param name="isHeadOffice">Whether this is the head office.</param>
    /// <param name="timeZoneId">The time zone, defaulted from the firm.</param>
    /// <returns>The branch, or a validation failure.</returns>
    /// <remarks>
    /// Internal so branches can only be created through
    /// <see cref="Firm.AddBranch"/>, which owns the firm-wide invariants.
    /// </remarks>
    internal static Result<Branch> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        bool isHeadOffice,
        string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Branch>(Error.Validation(
                "Branch.NameRequired", "A branch name is required."));
        }

        if (code.Length > 20)
        {
            return Result.Failure<Branch>(Error.Validation(
                "Branch.CodeTooLong", "A branch code cannot exceed 20 characters."));
        }

        return Result.Success(new Branch(
            BranchId.NewId(), tenantId, firmId, code, name.Trim(), isHeadOffice, timeZoneId));
    }

    /// <summary>Sets the branch's contact details.</summary>
    /// <param name="addressLine1">The first address line.</param>
    /// <param name="addressLine2">The second address line.</param>
    /// <param name="phone">The telephone number.</param>
    /// <param name="email">The email address.</param>
    public void SetContactDetails(
        string? addressLine1,
        string? addressLine2,
        string? phone,
        string? email)
    {
        AddressLine1 = Trimmed(addressLine1);
        AddressLine2 = Trimmed(addressLine2);
        Phone = Trimmed(phone);
        Email = Trimmed(email);
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or <see langword="null"/> to clear it.</param>
    public void SetArabicName(string? nameArabic) => NameArabic = Trimmed(nameArabic);

    /// <summary>Overrides the time zone inherited from the firm.</summary>
    /// <param name="timeZoneId">An IANA time-zone identifier.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Result.Failure(Error.Validation(
                "Branch.UnknownTimeZone",
                $"'{timeZoneId}' is not a recognised time-zone identifier."));
        }

        TimeZoneId = timeZoneId;
        return Result.Success();
    }

    /// <summary>Deactivates the branch, preventing further transactions.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reactivates a deactivated branch.</summary>
    public void Activate() => IsActive = true;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
