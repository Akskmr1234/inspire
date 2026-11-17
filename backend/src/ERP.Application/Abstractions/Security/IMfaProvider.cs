using ERP.Domain.Identity;
using ERP.SharedKernel.Results;

namespace ERP.Application.Abstractions.Security;

/// <summary>What the second-factor server handed back when a factor was enrolled.</summary>
/// <param name="Serial">The serial it knows the factor by.</param>
/// <param name="QrImage">
/// A QR code as a <c>data:</c> URI, for the phone to scan: the authenticator's secret,
/// or the push app's registration link. Null for backup codes.
/// </param>
/// <param name="SetupUrl">The link inside the QR code, for typing in by hand.</param>
/// <param name="BackupCodes">The backup codes, shown once. Null for other kinds.</param>
public sealed record MfaEnrolment(
    string Serial,
    string? QrImage,
    string? SetupUrl,
    IReadOnlyList<string>? BackupCodes);

/// <summary>A second-factor server: enrols factors and checks them at sign-in.</summary>
/// <remarks>
/// Kept behind this interface so sign-in does not depend on the server chosen. The one
/// in use is privacyIDEA, free and open source, which holds every secret; the ERP keeps
/// only each factor's serial.
/// </remarks>
public interface IMfaProvider
{
    /// <summary>Gets a value indicating whether a second-factor server is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>Starts enrolling a factor.</summary>
    /// <param name="kind">What kind of factor.</param>
    /// <param name="label">The account name the authenticator app shows.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What the phone needs to finish enrolling, or why it failed.</returns>
    Task<Result<MfaEnrolment>> EnrolAsync(
        MfaFactorKind kind,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a push factor's phone has finished registering.</summary>
    /// <param name="serial">The factor's serial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True once the phone is registered.</returns>
    Task<Result<bool>> IsRegisteredAsync(string serial, CancellationToken cancellationToken = default);

    /// <summary>Checks a code: an authenticator code or a backup code.</summary>
    /// <param name="serial">The factor's serial.</param>
    /// <param name="code">What the user typed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the code is right.</returns>
    Task<Result<bool>> CheckCodeAsync(
        string serial,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a sign-in request to the phone behind a push factor.</summary>
    /// <param name="serial">The factor's serial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The transaction to poll for the answer.</returns>
    Task<Result<string>> SendPushAsync(string serial, CancellationToken cancellationToken = default);

    /// <summary>Whether the phone approved a push.</summary>
    /// <param name="transactionId">The transaction <see cref="SendPushAsync"/> returned.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True once approved; false while waiting, or when denied or expired.</returns>
    Task<Result<bool>> IsPushApprovedAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a factor from the server, so it can never pass again.</summary>
    /// <param name="serial">The factor's serial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or why it failed.</returns>
    Task<Result> RemoveAsync(string serial, CancellationToken cancellationToken = default);
}
