using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Users;

/// <summary>A role as its holder sees it on their own profile.</summary>
/// <param name="Name">The role's name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="GrantsAllPermissions">Whether it holds every permission.</param>
public sealed record MyRole(string Name, string Description, bool GrantsAllPermissions);

/// <summary>One place the user is signed in.</summary>
/// <param name="SessionId">The sign-in, which is the family its refresh tokens share.</param>
/// <param name="SignedInAtUtc">When the user signed in there.</param>
/// <param name="LastActiveAtUtc">When it last renewed its access, which it does while in use.</param>
/// <param name="ExpiresAtUtc">When it ends unless the user signs in again.</param>
/// <param name="UserAgent">The browser that signed in, as it described itself.</param>
/// <param name="IpAddress">The address it signed in from.</param>
/// <param name="IsCurrent">Whether it is the sign-in asking.</param>
public sealed record MySession(
    Guid SessionId,
    DateTimeOffset SignedInAtUtc,
    DateTimeOffset LastActiveAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? UserAgent,
    string? IpAddress,
    bool IsCurrent);

/// <summary>The signed-in user's own account, as their profile shows it.</summary>
/// <param name="UserId">The user.</param>
/// <param name="UserName">What they sign in with.</param>
/// <param name="DisplayName">The name shown on screens and documents.</param>
/// <param name="Email">Their email address.</param>
/// <param name="MobileNumber">Their mobile number, if given.</param>
/// <param name="TenantCode">The company code they sign in to.</param>
/// <param name="TenantName">The company's name.</param>
/// <param name="Roles">The roles they hold.</param>
/// <param name="CreatedAtUtc">When the account was made.</param>
/// <param name="LastLoginAtUtc">When they last signed in, anywhere.</param>
/// <param name="PasswordChangedAtUtc">When the password was last set.</param>
/// <param name="MustChangePassword">Whether a new password is owed before anything else.</param>
/// <param name="Sessions">Where they are signed in now, the current one first.</param>
public sealed record MyAccountResponse(
    Guid UserId,
    string UserName,
    string DisplayName,
    string Email,
    string? MobileNumber,
    string TenantCode,
    string TenantName,
    IReadOnlyList<MyRole> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? PasswordChangedAtUtc,
    bool MustChangePassword,
    IReadOnlyList<MySession> Sessions);

/// <summary>The details a user may correct on their own account.</summary>
/// <param name="DisplayName">The name shown on screens and documents.</param>
/// <param name="Email">Their email address.</param>
/// <param name="MobileNumber">Their mobile number, or empty to clear it.</param>
public sealed record UpdateMyAccountRequest(string DisplayName, string Email, string? MobileNumber);

/// <summary>A user's own account: what their profile shows and what it lets them change.</summary>
/// <remarks>
/// Separate from <see cref="IUserAdministration"/> because it needs no permission beyond
/// being signed in, and because it can never reach anybody else's account: every member
/// takes the caller's own identifier, which the controller reads from their token.
/// </remarks>
public interface IMyAccount
{
    /// <summary>Reads the user's account and where they are signed in.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="currentSession">The sign-in asking, if its token names one.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The account, or nothing when the user no longer exists.</returns>
    Task<MyAccountResponse?> GetAsync(
        UserId userId,
        Guid? currentSession,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects the user's name, email and mobile number.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="currentSession">The sign-in asking, if its token names one.</param>
    /// <param name="request">The details as they should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The account as it now stands, or why the change was refused.</returns>
    Task<Result<MyAccountResponse>> UpdateAsync(
        UserId userId,
        Guid? currentSession,
        UpdateMyAccountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Signs the user out of one place.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="sessionId">The sign-in to end.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or not-found when it is not one of theirs or has already ended.</returns>
    Task<Result> EndSessionAsync(
        UserId userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Signs the user out everywhere except the sign-in asking.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="currentSession">The sign-in to keep.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>How many sign-ins were ended.</returns>
    Task<int> EndOtherSessionsAsync(
        UserId userId,
        Guid? currentSession,
        CancellationToken cancellationToken = default);
}
