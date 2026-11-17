using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Security;

/// <summary>One of a user's second factors, as their security screen lists it.</summary>
/// <param name="FactorId">The factor.</param>
/// <param name="Kind">What kind it is.</param>
/// <param name="Label">The user's name for it.</param>
/// <param name="IsActive">Whether sign-in asks for it; false while still being set up.</param>
/// <param name="CreatedAtUtc">When it was added.</param>
/// <param name="LastUsedAtUtc">When it last passed a sign-in.</param>
public sealed record MfaFactorView(
    Guid FactorId,
    MfaFactorKind Kind,
    string Label,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

/// <summary>A user's second factors, and which kinds this server offers.</summary>
/// <param name="Available">Whether a second-factor server is configured at all.</param>
/// <param name="PushAvailable">Whether push approval is offered.</param>
/// <param name="Factors">The user's factors.</param>
public sealed record MfaOverview(bool Available, bool PushAvailable, IReadOnlyList<MfaFactorView> Factors);

/// <summary>A factor being set up, and what the user needs to finish it.</summary>
/// <param name="FactorId">The factor.</param>
/// <param name="Kind">What kind it is.</param>
/// <param name="QrImage">The QR code to scan, as a <c>data:</c> URI.</param>
/// <param name="SetupUrl">The link in the QR code.</param>
/// <param name="BackupCodes">The backup codes, shown this once.</param>
/// <param name="IsActive">Whether it is already in use (backup codes are, at once).</param>
public sealed record MfaEnrolmentStarted(
    Guid FactorId,
    MfaFactorKind Kind,
    string? QrImage,
    string? SetupUrl,
    IReadOnlyList<string>? BackupCodes,
    bool IsActive);

/// <summary>A user's own second factors, and an administrator's reset of them.</summary>
public interface IMfaAdministration
{
    /// <summary>Lists a user's factors.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The factors, and what this server offers.</returns>
    Task<MfaOverview> ListAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Starts adding a factor.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="kind">What kind.</param>
    /// <param name="label">The user's name for it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What the user needs to finish, or why it could not start.</returns>
    Task<Result<MfaEnrolmentStarted>> BeginAsync(
        UserId userId,
        MfaFactorKind kind,
        string? label,
        CancellationToken cancellationToken = default);

    /// <summary>Finishes adding a factor: checks a first code, or that the phone registered.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="factorId">The factor.</param>
    /// <param name="code">A code from the authenticator app; not needed for push.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True once active; false while a phone has still to register.</returns>
    Task<Result<bool>> ConfirmAsync(
        UserId userId,
        Guid factorId,
        string? code,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one of the user's factors.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="factorId">The factor.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or why it failed.</returns>
    Task<Result> RemoveAsync(UserId userId, Guid factorId, CancellationToken cancellationToken = default);

    /// <summary>Removes every factor a user has, for one who lost their phone.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>How many factors were removed.</returns>
    Task<Result<int>> ResetAsync(UserId userId, CancellationToken cancellationToken = default);
}
