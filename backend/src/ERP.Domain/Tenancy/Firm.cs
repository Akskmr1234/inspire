using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tenancy;

/// <summary>
/// An independent set of books within a tenant.
/// </summary>
/// <remarks>
/// <para>
/// The firm is the accounting boundary. Each one keeps its own chart of accounts,
/// financial data, inventory, users, permissions, and configuration, and
/// balances never cross between firms. A tenant with three firms is running
/// three separate businesses that happen to share a login.
/// </para>
/// <para>
/// The firm - not the tenant and not the deployment - owns the
/// <see cref="TaxRegime"/>. A single instance serves Gulf VAT firms and Indian
/// GST firms simultaneously, so the regime cannot be a global setting.
/// </para>
/// </remarks>
public sealed class Firm : AggregateRoot<FirmId>, ITenantScoped, IAuditable, ISoftDeletable
{
    private readonly List<Branch> _branches = [];

    private Firm(
        FirmId id,
        TenantId tenantId,
        string code,
        string name,
        CurrencyCode baseCurrency,
        TaxRegime taxRegime,
        string timeZoneId)
        : base(id)
    {
        TenantId = tenantId;
        Code = code;
        Name = name;
        BaseCurrency = baseCurrency;
        TaxRegime = taxRegime;
        TimeZoneId = timeZoneId;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Firm()
    {
        Code = string.Empty;
        Name = string.Empty;
        TimeZoneId = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the short code used in numbering and reports, for example <c>STARTECH</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the registered name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, shown when the interface is in RTL mode.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>
    /// Gets the currency the firm keeps its books in. Foreign-currency documents
    /// are converted to this for posting.
    /// </summary>
    public CurrencyCode BaseCurrency { get; private set; }

    /// <summary>Gets the statutory tax system this firm operates under.</summary>
    public TaxRegime TaxRegime { get; private set; }

    /// <summary>
    /// Gets the tax registration number - a VAT number under
    /// <see cref="TaxRegime.GccVat"/>, a GSTIN under
    /// <see cref="TaxRegime.IndiaGst"/>.
    /// </summary>
    public string? TaxRegistrationNumber { get; private set; }

    /// <summary>
    /// Gets the firm's own state or emirate code.
    /// </summary>
    /// <remarks>
    /// Compared against the customer's to decide IGST versus CGST + SGST. Only
    /// meaningful under <see cref="TaxRegime.IndiaGst"/>.
    /// </remarks>
    public string? StateCode { get; private set; }

    /// <summary>
    /// Gets the IANA time-zone identifier, for example <c>Asia/Qatar</c>.
    /// </summary>
    /// <remarks>
    /// Day-book and Z-report boundaries are evaluated here rather than on the
    /// server clock. A firm in Doha and one in Kerala are on different calendar
    /// dates for part of every day.
    /// </remarks>
    public string TimeZoneId { get; private set; }

    /// <summary>Gets a value indicating whether the firm may be transacted against.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the branches belonging to this firm.</summary>
    public IReadOnlyCollection<Branch> Branches => _branches.AsReadOnly();

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

    /// <summary>Creates a firm.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The short code, unique within the tenant.</param>
    /// <param name="name">The registered name.</param>
    /// <param name="baseCurrency">The currency the books are kept in.</param>
    /// <param name="taxRegime">The statutory tax system.</param>
    /// <param name="timeZoneId">An IANA time-zone identifier.</param>
    /// <returns>The firm, or a validation failure.</returns>
    public static Result<Firm> Create(
        TenantId tenantId,
        string code,
        string name,
        CurrencyCode baseCurrency,
        TaxRegime taxRegime,
        string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Firm>(Error.Validation(
                "Firm.CodeRequired", "A firm code is required."));
        }

        if (code.Trim().Length > 20)
        {
            return Result.Failure<Firm>(Error.Validation(
                "Firm.CodeTooLong", "A firm code cannot exceed 20 characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Firm>(Error.Validation(
                "Firm.NameRequired", "A firm name is required."));
        }

        if (!baseCurrency.IsSpecified)
        {
            return Result.Failure<Firm>(Error.Validation(
                "Firm.BaseCurrencyRequired", "A base currency is required."));
        }

        if (!IsKnownTimeZone(timeZoneId))
        {
            return Result.Failure<Firm>(Error.Validation(
                "Firm.UnknownTimeZone",
                $"'{timeZoneId}' is not a recognised time-zone identifier."));
        }

        return Result.Success(new Firm(
            FirmId.NewId(),
            tenantId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            baseCurrency,
            taxRegime,
            timeZoneId));
    }

    /// <summary>Adds a branch to this firm.</summary>
    /// <param name="code">The branch code, unique within the firm.</param>
    /// <param name="name">The branch name.</param>
    /// <param name="isHeadOffice">Whether this branch is the head office.</param>
    /// <returns>The branch, or a failure explaining why it could not be added.</returns>
    /// <remarks>
    /// Branches are created through the firm rather than independently because
    /// two invariants span the whole set: codes must be unique within the firm,
    /// and at most one branch may be the head office. Neither can be enforced by
    /// a branch that only knows about itself.
    /// </remarks>
    public Result<Branch> AddBranch(string code, string name, bool isHeadOffice = false)
    {
        if (!IsActive)
        {
            return Result.Failure<Branch>(Error.BusinessRule(
                "Firm.Inactive",
                $"Firm '{Code}' is inactive and cannot take new branches."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Branch>(Error.Validation(
                "Branch.CodeRequired", "A branch code is required."));
        }

        string normalisedCode = code.Trim().ToUpperInvariant();

        if (_branches.Exists(b => string.Equals(b.Code, normalisedCode, StringComparison.Ordinal)))
        {
            return Result.Failure<Branch>(Error.Conflict(
                "Branch.DuplicateCode",
                $"Firm '{Code}' already has a branch with code '{normalisedCode}'."));
        }

        if (isHeadOffice && _branches.Exists(b => b.IsHeadOffice))
        {
            return Result.Failure<Branch>(Error.Conflict(
                "Branch.HeadOfficeAlreadyExists",
                $"Firm '{Code}' already has a head office."));
        }

        Result<Branch> branch = Branch.Create(
            TenantId, Id, normalisedCode, name, isHeadOffice, TimeZoneId);

        if (branch.IsFailure)
        {
            return branch;
        }

        _branches.Add(branch.Value);
        Raise(new BranchAdded(Id, branch.Value.Id, normalisedCode));

        return branch;
    }

    /// <summary>Records the firm's tax registration details.</summary>
    /// <param name="registrationNumber">The VAT number or GSTIN.</param>
    /// <param name="stateCode">The firm's state code, required under Indian GST.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetTaxRegistration(string? registrationNumber, string? stateCode)
    {
        // Without the firm's own state there is nothing to compare the customer's
        // against, so every GST document would silently fall through to the
        // intra-state branch and under-charge on inter-state supply.
        if (TaxRegime == TaxRegime.IndiaGst && string.IsNullOrWhiteSpace(stateCode))
        {
            return Result.Failure(Error.Validation(
                "Firm.StateCodeRequiredForGst",
                "A state code is required under Indian GST to determine whether a " +
                "supply is inter-state."));
        }

        TaxRegistrationNumber = registrationNumber?.Trim();
        StateCode = stateCode?.Trim().ToUpperInvariant();

        return Result.Success();
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or <see langword="null"/> to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Deactivates the firm, preventing further transactions.</summary>
    /// <remarks>
    /// Existing data is untouched and remains readable for reporting and audit.
    /// </remarks>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new FirmDeactivated(Id, Code));
    }

    /// <summary>Reactivates a deactivated firm.</summary>
    public void Activate() => IsActive = true;

    private static bool IsKnownTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

/// <summary>Raised when a branch is added to a firm.</summary>
/// <param name="FirmId">The firm.</param>
/// <param name="BranchId">The new branch.</param>
/// <param name="Code">The branch code.</param>
public sealed record BranchAdded(FirmId FirmId, BranchId BranchId, string Code) : DomainEvent;

/// <summary>Raised when a firm is deactivated.</summary>
/// <param name="FirmId">The firm.</param>
/// <param name="Code">The firm code.</param>
public sealed record FirmDeactivated(FirmId FirmId, string Code) : DomainEvent;
