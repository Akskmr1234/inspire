using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Security;

/// <summary>Issues access tokens and the opaque refresh tokens that renew them.</summary>
public interface ITokenService
{
    /// <summary>Gets how long an issued refresh token remains valid.</summary>
    /// <remarks>
    /// Exposed here so callers that persist a token do not need the JWT
    /// configuration type - which lives in ERP.Identity and is not visible from
    /// Infrastructure. The service already owns the setting; surfacing it keeps
    /// the layering intact.
    /// </remarks>
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Issues a signed access token.</summary>
    /// <param name="request">What the token should assert.</param>
    /// <returns>The token and the instant it expires.</returns>
    AccessToken CreateAccessToken(AccessTokenRequest request);

    /// <summary>Generates a refresh token.</summary>
    /// <returns>
    /// The raw token, to be returned to the client once and never stored, together
    /// with the hash that is persisted in its place.
    /// </returns>
    /// <remarks>
    /// Opaque and random rather than a JWT. A refresh token carries no claims and
    /// is only ever checked against the database, so signing it would add
    /// structure that must then be validated without buying anything. Storing only
    /// the hash means read access to the token table yields no usable sessions.
    /// </remarks>
    (string Token, string Hash) CreateRefreshToken();

    /// <summary>Hashes a refresh token presented by a client, for lookup.</summary>
    /// <param name="token">The raw token.</param>
    /// <returns>The hash to match against stored values.</returns>
    string HashRefreshToken(string token);
}

/// <summary>What an access token should assert.</summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="TenantId">The tenant the user belongs to.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The name to show in the interface.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirmIds">Every firm the user may work in.</param>
/// <param name="BranchIds">Every branch the user may work in.</param>
/// <param name="Roles">The user's role names.</param>
/// <param name="MustChangePassword">
/// Whether the client must send the user to a password-change screen before
/// anything else.
/// </param>
public sealed record AccessTokenRequest(
    UserId UserId,
    TenantId TenantId,
    string UserName,
    string DisplayName,
    string Email,
    IReadOnlyCollection<FirmId> FirmIds,
    IReadOnlyCollection<BranchId> BranchIds,
    IReadOnlyCollection<string> Roles,
    bool MustChangePassword);

/// <summary>A signed access token and its expiry.</summary>
/// <param name="Value">The encoded JWT.</param>
/// <param name="ExpiresAtUtc">When it stops being accepted.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
