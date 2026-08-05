using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Tenancy;

/// <summary>
/// The user on whose behalf the current operation is running.
/// </summary>
/// <remarks>
/// Supplies the actor recorded in audit stamps and the audit trail. Background
/// work with no signed-in user reports <see cref="UserId.System"/>, so an audit
/// row always names someone - a recognisable sentinel rather than an empty
/// identifier that reads like a defect.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the acting user, or <see cref="UserId.System"/> for platform-initiated
    /// work.
    /// </summary>
    UserId UserId { get; }

    /// <summary>Gets the user's display name, when signed in.</summary>
    string? UserName { get; }

    /// <summary>Gets a value indicating whether a real user is signed in.</summary>
    bool IsAuthenticated { get; }
}
