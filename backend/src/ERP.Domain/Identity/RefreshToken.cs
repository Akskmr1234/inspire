using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Identity;

/// <summary>Why a refresh token is no longer usable.</summary>
public enum RefreshTokenRevocationReason
{
    /// <summary>Still valid.</summary>
    None = 0,

    /// <summary>Exchanged for a new token during normal rotation.</summary>
    Rotated = 1,

    /// <summary>The user signed out.</summary>
    SignedOut = 2,

    /// <summary>
    /// Revoked because a token from the same family was presented after it had
    /// already been used - the signature of a stolen token.
    /// </summary>
    SuspectedTheft = 3,

    /// <summary>Revoked administratively, or because the account was disabled.</summary>
    Administrative = 4,
}

/// <summary>
/// A long-lived credential that can be exchanged for a new access token.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are deliberately short-lived, which means something must be able
/// to renew them without asking for the password again. That something is a
/// far more valuable credential, so it is handled carefully:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Only a hash is stored.</b> The raw token exists in the response to the
/// client and nowhere else. Read access to this table does not yield usable
/// sessions.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Every use rotates it.</b> Refreshing revokes the presented token and issues
/// a new one, so a captured token has a short useful life.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Reuse is treated as theft.</b> Because tokens rotate, a token presented
/// twice means two parties hold it. There is no way to tell which is the
/// legitimate user, so the entire family is revoked and both are made to sign in
/// again. Inconveniencing one user beats leaving an attacker with a live session.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class RefreshToken : AggregateRoot<RefreshTokenId>, ITenantScoped
{
    private RefreshToken(
        RefreshTokenId id,
        TenantId tenantId,
        UserId userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private RefreshToken() => TokenHash = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the user this token authenticates.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the hash of the token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>
    /// Gets the identifier shared by every token descended from one sign-in.
    /// </summary>
    /// <remarks>
    /// Rotation produces a chain of tokens from a single authentication. The
    /// family is what lets the whole chain be revoked at once when reuse is
    /// detected, rather than only the token that happened to be presented.
    /// </remarks>
    public Guid FamilyId { get; private set; }

    /// <summary>Gets when the token was issued.</summary>
    public DateTimeOffset IssuedAtUtc { get; private set; }

    /// <summary>Gets when the token expires.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Gets when the token was revoked, if it has been.</summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>Gets why the token was revoked.</summary>
    public RefreshTokenRevocationReason RevocationReason { get; private set; }

    /// <summary>Gets the client's user-agent string, for the device list.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>Gets the client's IP address at issue.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Gets a value indicating whether the token has been revoked.</summary>
    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Issues a refresh token at the head of a new family.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="tokenHash">The hash of the generated token.</param>
    /// <param name="issuedAtUtc">The current instant.</param>
    /// <param name="lifetime">How long the token remains valid.</param>
    /// <param name="userAgent">The client's user agent.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <returns>The token, or a validation failure.</returns>
    public static Result<RefreshToken> Issue(
        TenantId tenantId,
        UserId userId,
        string tokenHash,
        DateTimeOffset issuedAtUtc,
        TimeSpan lifetime,
        string? userAgent = null,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure<RefreshToken>(Error.Validation(
                "RefreshToken.HashRequired", "A token hash is required."));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            return Result.Failure<RefreshToken>(Error.Validation(
                "RefreshToken.InvalidLifetime", "A token lifetime must be positive."));
        }

        RefreshToken token = new(
            RefreshTokenId.NewId(),
            tenantId,
            userId,
            tokenHash,
            Guid.CreateVersion7(),
            issuedAtUtc,
            issuedAtUtc.Add(lifetime))
        {
            UserAgent = userAgent,
            IpAddress = ipAddress,
        };

        return Result.Success(token);
    }

    /// <summary>Determines whether the token can currently be exchanged.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success when the token is usable.</returns>
    public Result EnsureUsable(DateTimeOffset nowUtc)
    {
        if (IsRevoked)
        {
            return Result.Failure(Error.Unauthorized(
                "RefreshToken.Revoked",
                $"This refresh token was revoked ({RevocationReason})."));
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return Result.Failure(Error.Unauthorized(
                "RefreshToken.Expired", "This refresh token has expired."));
        }

        return Result.Success();
    }

    /// <summary>Exchanges this token for a successor in the same family.</summary>
    /// <param name="newTokenHash">The hash of the replacement token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="lifetime">The replacement's lifetime.</param>
    /// <param name="userAgent">The client's user agent.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <returns>The replacement token, or a failure if this one is not usable.</returns>
    public Result<RefreshToken> Rotate(
        string newTokenHash,
        DateTimeOffset nowUtc,
        TimeSpan lifetime,
        string? userAgent = null,
        string? ipAddress = null)
    {
        Result usable = EnsureUsable(nowUtc);

        if (usable.IsFailure)
        {
            return Result.Failure<RefreshToken>(usable.Error);
        }

        RevokedAtUtc = nowUtc;
        RevocationReason = RefreshTokenRevocationReason.Rotated;

        // The successor inherits the family, and deliberately does not extend the
        // expiry: a session has a maximum life regardless of how often it is
        // refreshed, so a stolen token cannot be renewed indefinitely.
        RefreshToken successor = new(
            RefreshTokenId.NewId(),
            TenantId,
            UserId,
            newTokenHash,
            FamilyId,
            nowUtc,
            Min(nowUtc.Add(lifetime), ExpiresAtUtc))
        {
            UserAgent = userAgent ?? UserAgent,
            IpAddress = ipAddress ?? IpAddress,
        };

        return Result.Success(successor);
    }

    /// <summary>Revokes this token.</summary>
    /// <param name="reason">Why it is being revoked.</param>
    /// <param name="nowUtc">The current instant.</param>
    public void Revoke(RefreshTokenRevocationReason reason, DateTimeOffset nowUtc)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
        RevocationReason = reason;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;
}
