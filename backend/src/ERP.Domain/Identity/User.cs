using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Identity;

/// <summary>How a user proves a second factor.</summary>
public enum MfaMethod
{
    /// <summary>No second factor.</summary>
    None = 0,

    /// <summary>A one-time code delivered by email.</summary>
    Email = 1,

    /// <summary>A one-time code delivered by SMS.</summary>
    Sms = 2,

    /// <summary>An RFC 6238 authenticator app.</summary>
    Totp = 3,
}

/// <summary>
/// A person who can sign in.
/// </summary>
/// <remarks>
/// <para>
/// Holds the credential and the lockout state, but knows nothing about how a
/// password is hashed or how a token is issued. Those are infrastructure
/// concerns; the aggregate is handed an already-hashed value and enforces the
/// rules around it. That keeps the algorithm replaceable without touching the
/// domain, and keeps the domain testable without a hashing implementation.
/// </para>
/// <para>
/// A user belongs to one tenant and may be granted access to several firms and
/// branches within it, switching between them in session.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot<UserId>, ITenantScoped, IAuditable, ISoftDeletable
{
    /// <summary>Failed attempts tolerated before the account locks.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long an account stays locked.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];
    private readonly List<UserFirmAccess> _firmAccess = [];

    private User(
        UserId id,
        TenantId tenantId,
        string userName,
        string email,
        string displayName,
        string passwordHash)
        : base(id)
    {
        TenantId = tenantId;
        UserName = userName;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = true;
        MustChangePassword = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private User()
    {
        UserName = string.Empty;
        Email = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the sign-in name, unique within the tenant and stored lower-case.</summary>
    public string UserName { get; private set; }

    /// <summary>Gets the email address, used for notifications and email OTP.</summary>
    public string Email { get; private set; }

    /// <summary>Gets the name shown in the interface and in audit records.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Gets the mobile number, used for SMS OTP.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>Gets the hashed password. Never the password itself.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Gets when the password was last changed, for expiry policies.</summary>
    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the user must change their password before
    /// doing anything else.
    /// </summary>
    /// <remarks>
    /// Set on creation and after an administrative reset, so a password chosen by
    /// somebody else is never a lasting credential.
    /// </remarks>
    public bool MustChangePassword { get; private set; }

    /// <summary>Gets the second-factor method in force.</summary>
    public MfaMethod MfaMethod { get; private set; }

    /// <summary>Gets the shared secret for an authenticator app, when TOTP is in use.</summary>
    public string? TotpSecret { get; private set; }

    /// <summary>Gets the count of consecutive failed sign-in attempts.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Gets the instant the current lockout expires, if the account is locked.</summary>
    public DateTimeOffset? LockedOutUntilUtc { get; private set; }

    /// <summary>Gets when the user last signed in successfully.</summary>
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    /// <summary>Gets a value indicating whether the account is enabled.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the roles granted to this user.</summary>
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    /// <summary>Gets the firms and branches this user may work in.</summary>
    public IReadOnlyCollection<UserFirmAccess> FirmAccess => _firmAccess.AsReadOnly();

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

    /// <summary>Creates a user.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="email">The email address.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="passwordHash">The already-hashed password.</param>
    /// <returns>The user, or a validation failure.</returns>
    public static Result<User> Create(
        TenantId tenantId,
        string userName,
        string email,
        string displayName,
        string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Result.Failure<User>(Error.Validation(
                "User.UserNameRequired", "A user name is required."));
        }

        if (userName.Trim().Length is < 3 or > 100)
        {
            return Result.Failure<User>(Error.Validation(
                "User.UserNameLength", "A user name must be between 3 and 100 characters."));
        }

        if (!LooksLikeEmail(email))
        {
            return Result.Failure<User>(Error.Validation(
                "User.EmailInvalid", $"'{email}' is not a valid email address."));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Result.Failure<User>(Error.Validation(
                "User.PasswordRequired", "A password hash is required."));
        }

        return Result.Success(new User(
            UserId.NewId(),
            tenantId,
            userName.Trim().ToLowerInvariant(),
            email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            passwordHash));
    }

    /// <summary>Determines whether the account is currently locked.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns><see langword="true"/> when locked.</returns>
    public bool IsLockedOut(DateTimeOffset nowUtc) =>
        LockedOutUntilUtc is { } until && until > nowUtc;

    /// <summary>
    /// Checks whether this account is in a state that permits signing in, before
    /// the password is even considered.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success when a sign-in attempt may proceed.</returns>
    /// <remarks>
    /// The returned errors are deliberately specific for logging, but the API must
    /// collapse them to one generic message before replying. Telling an
    /// unauthenticated caller that an account exists but is locked confirms the
    /// account exists, which is exactly what someone enumerating user names wants
    /// to know.
    /// </remarks>
    public Result EnsureCanAuthenticate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(Error.Unauthorized(
                "User.Inactive", $"Account '{UserName}' is disabled."));
        }

        if (IsLockedOut(nowUtc))
        {
            return Result.Failure(Error.Unauthorized(
                "User.LockedOut",
                $"Account '{UserName}' is locked until {LockedOutUntilUtc:u}."));
        }

        return Result.Success();
    }

    /// <summary>Records a failed sign-in, locking the account once the threshold is reached.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void RecordFailedLogin(DateTimeOffset nowUtc)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockedOutUntilUtc = nowUtc.Add(LockoutDuration);
            Raise(new UserLockedOut(Id, TenantId, UserName, LockedOutUntilUtc.Value));
        }
    }

    /// <summary>Records a successful sign-in and clears any failure state.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void RecordSuccessfulLogin(DateTimeOffset nowUtc)
    {
        FailedLoginAttempts = 0;
        LockedOutUntilUtc = null;
        LastLoginAtUtc = nowUtc;
    }

    /// <summary>Sets a new password.</summary>
    /// <param name="passwordHash">The already-hashed new password.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or a validation failure.</returns>
    /// <remarks>
    /// Clears the lockout as well. Someone who has just proved they know the
    /// current password, or who has been through a verified reset, should not stay
    /// locked out by earlier failed guesses.
    /// </remarks>
    public Result SetPassword(string passwordHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Result.Failure(Error.Validation(
                "User.PasswordRequired", "A password hash is required."));
        }

        PasswordHash = passwordHash;
        PasswordChangedAtUtc = nowUtc;
        MustChangePassword = false;
        FailedLoginAttempts = 0;
        LockedOutUntilUtc = null;

        Raise(new UserPasswordChanged(Id, TenantId, UserName));

        return Result.Success();
    }

    /// <summary>Forces a password change on next sign-in, after an administrative reset.</summary>
    /// <param name="passwordHash">The temporary password's hash.</param>
    /// <param name="nowUtc">The current instant.</param>
    public void ResetPassword(string passwordHash, DateTimeOffset nowUtc)
    {
        PasswordHash = passwordHash;
        PasswordChangedAtUtc = nowUtc;
        MustChangePassword = true;
        FailedLoginAttempts = 0;
        LockedOutUntilUtc = null;
    }

    /// <summary>Enables an authenticator app as the second factor.</summary>
    /// <param name="secret">The shared secret.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result EnableTotp(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.Failure(Error.Validation(
                "User.TotpSecretRequired", "A TOTP secret is required."));
        }

        TotpSecret = secret;
        MfaMethod = MfaMethod.Totp;

        return Result.Success();
    }

    /// <summary>Selects a delivered one-time code as the second factor.</summary>
    /// <param name="method">Email or SMS.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result EnableMfa(MfaMethod method)
    {
        if (method == MfaMethod.Totp)
        {
            return Result.Failure(Error.Validation(
                "User.UseEnableTotp",
                "An authenticator app is enabled through EnableTotp, which requires a secret."));
        }

        if (method == MfaMethod.Sms && string.IsNullOrWhiteSpace(MobileNumber))
        {
            return Result.Failure(Error.Validation(
                "User.MobileRequiredForSms",
                "A mobile number is required before SMS codes can be enabled."));
        }

        MfaMethod = method;
        TotpSecret = null;

        return Result.Success();
    }

    /// <summary>Turns off the second factor.</summary>
    public void DisableMfa()
    {
        MfaMethod = MfaMethod.None;
        TotpSecret = null;
    }

    /// <summary>Sets the contact details.</summary>
    /// <param name="displayName">The display name.</param>
    /// <param name="mobileNumber">The mobile number.</param>
    public void SetContactDetails(string displayName, string? mobileNumber)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            DisplayName = displayName.Trim();
        }

        MobileNumber = string.IsNullOrWhiteSpace(mobileNumber) ? null : mobileNumber.Trim();
    }

    /// <summary>Grants a role to this user.</summary>
    /// <param name="roleId">The role to grant.</param>
    public void AssignRole(RoleId roleId)
    {
        if (_roles.Exists(r => r.RoleId == roleId))
        {
            return;
        }

        _roles.Add(new UserRole(Id, roleId, TenantId));
        Raise(new UserRolesChanged(Id, TenantId));
    }

    /// <summary>Removes a role from this user.</summary>
    /// <param name="roleId">The role to remove.</param>
    public void RemoveRole(RoleId roleId)
    {
        if (_roles.RemoveAll(r => r.RoleId == roleId) > 0)
        {
            Raise(new UserRolesChanged(Id, TenantId));
        }
    }

    /// <summary>Grants access to a firm, and optionally to one branch within it.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">
    /// The branch, or <see langword="null"/> for access to every branch in the firm.
    /// </param>
    public void GrantFirmAccess(FirmId firmId, BranchId? branchId = null)
    {
        bool alreadyGranted = _firmAccess.Exists(
            a => a.FirmId == firmId && a.BranchId == branchId);

        if (alreadyGranted)
        {
            return;
        }

        _firmAccess.Add(new UserFirmAccess(Id, firmId, branchId, TenantId));
    }

    /// <summary>Withdraws access to a firm and all of its branches.</summary>
    /// <param name="firmId">The firm.</param>
    public void RevokeFirmAccess(FirmId firmId) => _firmAccess.RemoveAll(a => a.FirmId == firmId);

    /// <summary>Disables the account.</summary>
    public void Deactivate()
    {
        IsActive = false;
        Raise(new UserDeactivated(Id, TenantId, UserName));
    }

    /// <summary>Re-enables the account and clears any lockout.</summary>
    public void Activate()
    {
        IsActive = true;
        FailedLoginAttempts = 0;
        LockedOutUntilUtc = null;
    }

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Trim before inspecting. Surrounding whitespace is an artefact of how the
        // value was typed or pasted and is stripped on the way in, so validating
        // the untrimmed string would reject addresses that are about to become
        // perfectly valid. Interior spaces are still rejected below.
        string candidate = value.Trim();

        // Deliberately a shape check rather than an attempt at RFC 5322. Full
        // syntactic validation rejects addresses that work and accepts addresses
        // that bounce; the only real proof is sending a message to it.
        int at = candidate.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at < candidate.Length - 1
            && candidate.IndexOf('@', at + 1) < 0
            && candidate.Contains('.', StringComparison.Ordinal)
            && !candidate.Contains(' ', StringComparison.Ordinal);
    }
}

/// <summary>Links a user to a role.</summary>
public sealed class UserRole : ITenantScoped
{
    internal UserRole(UserId userId, RoleId roleId, TenantId tenantId)
    {
        UserId = userId;
        RoleId = roleId;
        TenantId = tenantId;
    }

    private UserRole()
    {
    }

    /// <summary>Gets the user.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the role granted.</summary>
    public RoleId RoleId { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }
}

/// <summary>Grants a user access to a firm, and optionally to one branch within it.</summary>
public sealed class UserFirmAccess : ITenantScoped
{
    internal UserFirmAccess(UserId userId, FirmId firmId, BranchId? branchId, TenantId tenantId)
    {
        UserId = userId;
        FirmId = firmId;
        BranchId = branchId;
        TenantId = tenantId;
    }

    private UserFirmAccess()
    {
    }

    /// <summary>Gets the user.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the firm.</summary>
    public FirmId FirmId { get; private set; }

    /// <summary>
    /// Gets the branch, or <see langword="null"/> for access to every branch in
    /// the firm.
    /// </summary>
    public BranchId? BranchId { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }
}

/// <summary>Raised when an account locks after repeated failed sign-ins.</summary>
/// <param name="UserId">The user.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="LockedUntilUtc">When the lockout expires.</param>
public sealed record UserLockedOut(
    UserId UserId,
    TenantId TenantId,
    string UserName,
    DateTimeOffset LockedUntilUtc) : DomainEvent;

/// <summary>Raised when a user changes their password.</summary>
/// <param name="UserId">The user.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="UserName">The sign-in name.</param>
public sealed record UserPasswordChanged(
    UserId UserId,
    TenantId TenantId,
    string UserName) : DomainEvent;

/// <summary>Raised when a user's roles change, so cached permissions can be dropped.</summary>
/// <param name="UserId">The user.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record UserRolesChanged(UserId UserId, TenantId TenantId) : DomainEvent;

/// <summary>Raised when an account is disabled, so active sessions can be ended.</summary>
/// <param name="UserId">The user.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="UserName">The sign-in name.</param>
public sealed record UserDeactivated(
    UserId UserId,
    TenantId TenantId,
    string UserName) : DomainEvent;
