using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Security;

/// <summary>Resolves what a user is allowed to do.</summary>
/// <remarks>
/// <para>
/// Permissions are resolved from the database rather than carried in the access
/// token. A realistic ERP role holds hundreds of them; putting that set in a JWT
/// would produce a token measured in kilobytes on every request, and - worse -
/// one that keeps working with its old permissions until it expires. Revoking a
/// permission has to take effect promptly, which a self-contained token cannot
/// offer.
/// </para>
/// <para>
/// The lookup is cached, and the cache is dropped when a role's permissions or a
/// user's roles change, so the common path stays a memory read without the
/// staleness.
/// </para>
/// </remarks>
public interface IPermissionChecker
{
    /// <summary>Determines whether a user holds a permission.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="permissionCode">The canonical <c>module:resource:verb</c> code.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the permission is held.</returns>
    Task<bool> HasPermissionAsync(
        UserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default);

    /// <summary>Returns every permission a user holds.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The permission codes.</returns>
    /// <remarks>
    /// Used to build the menu and to enable or disable actions in the interface,
    /// so a user is not offered buttons that will refuse them.
    /// </remarks>
    Task<IReadOnlySet<string>> GetPermissionsAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the cached permissions for a user.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task InvalidateAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Drops cached permissions for everyone in a tenant.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// Used when a role changes: the affected users cannot be known without a
    /// query, and clearing the tenant is cheaper and safer than leaving anyone
    /// holding a permission that was just revoked.
    /// </remarks>
    Task InvalidateTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
