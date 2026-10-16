using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Users;

/// <summary>A user as the administration screen lists them.</summary>
/// <param name="UserId">The user.</param>
/// <param name="UserName">What they sign in with.</param>
/// <param name="Email">Their email address.</param>
/// <param name="DisplayName">The name shown on screens and documents.</param>
/// <param name="MobileNumber">Their mobile number, if given.</param>
/// <param name="IsActive">Whether they may sign in.</param>
/// <param name="MustChangePassword">Whether their next sign-in asks for a new password.</param>
/// <param name="IsLockedOut">Whether too many wrong passwords have locked them out for now.</param>
/// <param name="LastLoginAtUtc">When they last signed in.</param>
/// <param name="RoleIds">The roles they hold.</param>
public sealed record UserSummary(
    Guid UserId,
    string UserName,
    string Email,
    string DisplayName,
    string? MobileNumber,
    bool IsActive,
    bool MustChangePassword,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<Guid> RoleIds);

/// <summary>A role as the administration screen lists it.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="IsSystemRole">Whether it was seeded, and so cannot be deleted.</param>
/// <param name="GrantsAllPermissions">Whether it holds every permission.</param>
/// <param name="PermissionCodes">The permissions it grants, as codes.</param>
/// <param name="RestrictsMenu">Whether it shows only the menus it names.</param>
/// <param name="MenuCodes">The menus it shows, when it restricts its menu.</param>
/// <param name="UserCount">How many users hold it.</param>
public sealed record RoleSummary(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    bool GrantsAllPermissions,
    IReadOnlyList<string> PermissionCodes,
    bool RestrictsMenu,
    IReadOnlyList<string> MenuCodes,
    int UserCount);

/// <summary>One permission in the catalogue.</summary>
/// <param name="Code">Its code, as a role grants it: module:resource:verb.</param>
/// <param name="Module">The module.</param>
/// <param name="Resource">What it guards.</param>
/// <param name="Verb">What it allows: view, create, edit, delete, approve, print, export.</param>
/// <param name="Description">What the resource is.</param>
public sealed record PermissionDefinition(
    string Code,
    string Module,
    string Resource,
    string Verb,
    string Description);

/// <summary>A user as somebody enters or corrects them.</summary>
/// <param name="UserName">What they sign in with. Fixed once created.</param>
/// <param name="Email">Their email address.</param>
/// <param name="DisplayName">The name shown on screens and documents.</param>
/// <param name="MobileNumber">Their mobile number.</param>
/// <param name="Password">
/// A starting password, required for a new user and ignored on a correction; it has to be
/// changed at their first sign-in.
/// </param>
/// <param name="RoleIds">The roles they hold. At least one.</param>
/// <param name="IsActive">Whether they may sign in.</param>
public sealed record SaveUserRequest(
    string UserName,
    string Email,
    string DisplayName,
    string? MobileNumber,
    string? Password,
    IReadOnlyList<Guid> RoleIds,
    bool IsActive = true);

/// <summary>A role as somebody enters or corrects it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PermissionCodes">Every permission it grants.</param>
/// <param name="MenuCodes">
/// The menus it shows, or <see langword="null"/> to show every menu its permissions reach.
/// </param>
public sealed record SaveRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> PermissionCodes,
    IReadOnlyList<string>? MenuCodes);

/// <summary>Users, the roles they hold, and what each role may see and do.</summary>
/// <remarks>
/// Kept beside authentication rather than in a command handler per operation, for the
/// reason sign-in is: it works the identity tables directly, and every change it makes to
/// who may do what has to reach the cached permission sets at once.
/// </remarks>
public interface IUserAdministration
{
    /// <summary>Lists the tenant's users.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every user.</returns>
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a user with a starting password and their roles.</summary>
    /// <param name="request">The user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The user, or why they were refused.</returns>
    Task<Result<UserSummary>> CreateUserAsync(
        SaveUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects a user's details, roles and whether they may sign in.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="request">The user as they should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The user, or why the change was refused.</returns>
    Task<Result<UserSummary>> UpdateUserAsync(
        Guid userId,
        SaveUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a new starting password, to be changed at the next sign-in.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ResetPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's roles.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every role.</returns>
    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a role.</summary>
    /// <param name="request">The role.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The role, or why it was refused.</returns>
    Task<Result<RoleSummary>> CreateRoleAsync(
        SaveRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects a role: its name, permissions and menus.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="request">The role as it should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The role, or why the change was refused.</returns>
    Task<Result<RoleSummary>> UpdateRoleAsync(
        Guid roleId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a role nobody holds.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Lists every permission a role can grant.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The catalogue.</returns>
    Task<IReadOnlyList<PermissionDefinition>> ListPermissionsAsync(
        CancellationToken cancellationToken = default);
}
