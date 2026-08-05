using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Security;

/// <summary>What a client sends to sign in.</summary>
/// <param name="UserName">The sign-in name or email address.</param>
/// <param name="Password">The password.</param>
/// <param name="UserAgent">The client's user agent, recorded against the session.</param>
/// <param name="IpAddress">The client's IP address, recorded against the session.</param>
public sealed record SignInRequest(
    string UserName,
    string Password,
    string? UserAgent = null,
    string? IpAddress = null);

/// <summary>What a client sends to renew an access token.</summary>
/// <param name="RefreshToken">The refresh token issued previously.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="IpAddress">The client's IP address.</param>
public sealed record RefreshRequest(
    string RefreshToken,
    string? UserAgent = null,
    string? IpAddress = null);

/// <summary>The result of a successful sign-in or refresh.</summary>
/// <param name="AccessToken">The signed JWT.</param>
/// <param name="ExpiresAtUtc">When the access token expires.</param>
/// <param name="RefreshToken">
/// The refresh token, returned once and never stored in readable form.
/// </param>
/// <param name="MustChangePassword">
/// Whether the client must send the user to a password-change screen before
/// anything else.
/// </param>
/// <param name="DisplayName">The user's display name.</param>
public sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string RefreshToken,
    bool MustChangePassword,
    string DisplayName);

/// <summary>Signs users in, renews their tokens, and signs them out.</summary>
/// <remarks>
/// <para>
/// Every failure path returns the same generic error. The domain distinguishes
/// "no such user" from "wrong password" from "account locked" for the security
/// log, but revealing any of that to an unauthenticated caller confirms which
/// user names exist, which is exactly what an attacker enumerating accounts is
/// trying to learn.
/// </para>
/// </remarks>
public interface IAuthenticationService
{
    /// <summary>Signs a user in.</summary>
    /// <param name="request">The credentials.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The tokens, or a generic authentication failure.</returns>
    Task<Result<AuthenticationResponse>> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a new pair.</summary>
    /// <param name="request">The refresh token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new tokens, or a generic authentication failure.</returns>
    Task<Result<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Signs a user out by revoking a refresh token.</summary>
    /// <param name="refreshToken">The token to revoke.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, whether or not the token was found.</returns>
    /// <remarks>
    /// Signing out with an unknown token is not an error. The desired state -
    /// that token cannot be used - already holds, and reporting a failure would
    /// tell the caller whether a given token had ever existed.
    /// </remarks>
    Task<Result> SignOutAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Changes a signed-in user's password.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="currentPassword">The existing password.</param>
    /// <param name="newPassword">The replacement.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or a validation failure.</returns>
    Task<Result> ChangePasswordAsync(
        UserId userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}

/// <summary>Errors surfaced by authentication.</summary>
public static class AuthenticationErrors
{
    /// <summary>
    /// The single failure returned for every unsuccessful sign-in, whatever the
    /// underlying cause.
    /// </summary>
    /// <remarks>
    /// Wrong password, unknown user, disabled account, and locked account all
    /// produce this. Distinguishing them would let an unauthenticated caller
    /// enumerate valid user names, and the timing is equalised by verifying a
    /// dummy hash when no user is found.
    /// </remarks>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "Auth.InvalidCredentials",
        "The user name or password is incorrect.");

    /// <summary>The refresh token is unusable, whatever the reason.</summary>
    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "Auth.InvalidRefreshToken",
        "The refresh token is invalid or has expired. Sign in again.");

    /// <summary>The supplied current password did not match.</summary>
    public static readonly Error CurrentPasswordIncorrect = Error.Validation(
        "Auth.CurrentPasswordIncorrect",
        "The current password is incorrect.");

    /// <summary>The proposed password does not meet the policy.</summary>
    /// <param name="detail">What specifically is wrong with it.</param>
    /// <returns>The error.</returns>
    public static Error PasswordPolicy(string detail) =>
        Error.Validation("Auth.PasswordPolicy", detail);
}

/// <summary>
/// Validates a proposed password against the policy.
/// </summary>
/// <remarks>
/// Deliberately light on composition rules. Mandating a symbol and a digit
/// pushes people towards <c>Password1!</c>, which is weaker than a long
/// passphrase; length is the property that actually matters. NIST's current
/// guidance says the same, and it lets Arabic passphrases work as naturally as
/// English ones.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>The minimum accepted length.</summary>
    public const int MinimumLength = 12;

    /// <summary>The maximum accepted length.</summary>
    /// <remarks>
    /// A ceiling exists only to bound the work done by the hash function, so an
    /// enormous submission cannot be used to exhaust CPU on the sign-in path.
    /// </remarks>
    public const int MaximumLength = 256;

    /// <summary>Checks a proposed password.</summary>
    /// <param name="password">The proposed password.</param>
    /// <returns>Success, or the reason it was rejected.</returns>
    public static Result Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return Result.Failure(AuthenticationErrors.PasswordPolicy(
                "A password is required."));
        }

        if (password.Length < MinimumLength)
        {
            return Result.Failure(AuthenticationErrors.PasswordPolicy(
                $"A password must be at least {MinimumLength} characters."));
        }

        if (password.Length > MaximumLength)
        {
            return Result.Failure(AuthenticationErrors.PasswordPolicy(
                $"A password cannot exceed {MaximumLength} characters."));
        }

        // Catches the single worst case - a "password" made of one repeated
        // character - without pretending to be a strength meter.
        if (password.Distinct().Count() < 4)
        {
            return Result.Failure(AuthenticationErrors.PasswordPolicy(
                "A password must contain at least four different characters."));
        }

        return Result.Success();
    }
}
