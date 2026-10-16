using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Users;
using ERP.Domain.Identity;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Security;

/// <summary>The EF Core implementation of <see cref="IUserAdministration"/>.</summary>
/// <remarks>
/// <para>
/// Two rules hold whatever is asked. The tenant always keeps at least one active user
/// with a role that grants everything, so nobody can lock the firm out of its own
/// administration; and nobody deactivates themselves, which is the same mistake made by
/// one person.
/// </para>
/// <para>
/// Every change to who holds what is followed by clearing the cached permission sets, so
/// a role taken away stops working on the next request rather than two minutes later.
/// </para>
/// </remarks>
public sealed class UserAdministration : IUserAdministration
{
    private readonly ErpDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="UserAdministration"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="passwordHasher">Hashes starting passwords.</param>
    /// <param name="permissionChecker">The permission cache, cleared on every change.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The administrator making the change.</param>
    /// <param name="clock">The clock.</param>
    public UserAdministration(
        ErpDbContext context,
        IPasswordHasher passwordHasher,
        IPermissionChecker permissionChecker,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _permissionChecker = permissionChecker;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        List<User> users = await _context.Users
            .AsNoTracking()
            .Include(user => user.Roles)
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);

        return [.. users.Select(Describe)];
    }

    /// <inheritdoc />
    public async Task<Result<UserSummary>> CreateUserAsync(
        SaveUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result policy = PasswordPolicy.Validate(request.Password);

        if (policy.IsFailure)
        {
            return Result.Failure<UserSummary>(policy.Error);
        }

        Result<IReadOnlyList<Role>> roles = await LoadRolesAsync(request.RoleIds, cancellationToken);

        if (roles.IsFailure)
        {
            return Result.Failure<UserSummary>(roles.Error);
        }

        Result<User> created = User.Create(
            _tenantContext.TenantId,
            request.UserName,
            request.Email,
            request.DisplayName,
            _passwordHasher.Hash(request.Password!));

        if (created.IsFailure)
        {
            return Result.Failure<UserSummary>(created.Error);
        }

        User user = created.Value;

        Result unique = await EnsureUniqueAsync(user.UserName, user.Email, null, cancellationToken);

        if (unique.IsFailure)
        {
            return Result.Failure<UserSummary>(unique.Error);
        }

        user.SetContactDetails(request.DisplayName, request.MobileNumber);

        foreach (Role role in roles.Value)
        {
            user.AssignRole(role.Id);
        }

        // Every firm and branch of the tenant. Most firms have one of each, and a user
        // who could sign in but reach no firm would see an empty screen with no reason.
        foreach (Firm firm in await _context.Firms.Include(firm => firm.Branches)
            .ToListAsync(cancellationToken))
        {
            foreach (Branch branch in firm.Branches)
            {
                user.GrantFirmAccess(firm.Id, branch.Id);
            }
        }

        if (!request.IsActive)
        {
            user.Deactivate();
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(user));
    }

    /// <inheritdoc />
    public async Task<Result<UserSummary>> UpdateUserAsync(
        Guid userId,
        SaveUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await _context.Users
            .Include(candidate => candidate.Roles)
            .FirstOrDefaultAsync(candidate => candidate.Id == UserId.From(userId), cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserSummary>(Error.NotFound(
                "User.NotFound", "That user does not exist."));
        }

        Result<IReadOnlyList<Role>> roles = await LoadRolesAsync(request.RoleIds, cancellationToken);

        if (roles.IsFailure)
        {
            return Result.Failure<UserSummary>(roles.Error);
        }

        if (!request.IsActive && user.Id == _currentUser.UserId)
        {
            return Result.Failure<UserSummary>(Error.BusinessRule(
                "User.CannotDeactivateSelf",
                "You cannot deactivate the account you are signed in with."));
        }

        Result email = user.ChangeEmail(request.Email);

        if (email.IsFailure)
        {
            return Result.Failure<UserSummary>(email.Error);
        }

        Result unique = await EnsureUniqueAsync(user.UserName, user.Email, user.Id, cancellationToken);

        if (unique.IsFailure)
        {
            return Result.Failure<UserSummary>(unique.Error);
        }

        user.SetContactDetails(request.DisplayName, request.MobileNumber);

        // The difference only: a role held before and after is left as it is, rather
        // than removed and added back as a second row with the same key.
        HashSet<RoleId> wanted = [.. roles.Value.Select(role => role.Id)];

        foreach (RoleId held in user.Roles.Select(link => link.RoleId).Where(id => !wanted.Contains(id)).ToList())
        {
            user.RemoveRole(held);
        }

        foreach (RoleId missing in wanted.Where(id => user.Roles.All(link => link.RoleId != id)).ToList())
        {
            user.AssignRole(missing);
        }

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        Result administered = await EnsureAnAdministratorRemainsAsync(user, roles.Value, cancellationToken);

        if (administered.IsFailure)
        {
            return Result.Failure<UserSummary>(administered.Error);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionChecker.InvalidateAsync(user.Id, cancellationToken);

        return Result.Success(Describe(user));
    }

    /// <inheritdoc />
    public async Task<Result> ResetPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        Result policy = PasswordPolicy.Validate(password);

        if (policy.IsFailure)
        {
            return policy;
        }

        User? user = await _context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == UserId.From(userId), cancellationToken);

        if (user is null)
        {
            return Result.Failure(Error.NotFound("User.NotFound", "That user does not exist."));
        }

        user.ResetPassword(_passwordHasher.Hash(password), _clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
        CancellationToken cancellationToken = default)
    {
        List<Role> roles = await _context.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .Include(role => role.Menus)
            .OrderByDescending(role => role.GrantsAllPermissions)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);

        Dictionary<PermissionId, string> codes = await PermissionCodesAsync(cancellationToken);

        Dictionary<RoleId, int> holders = await _context.Set<UserRole>()
            .AsNoTracking()
            .GroupBy(link => link.RoleId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        return [.. roles.Select(role => Describe(role, codes, holders.GetValueOrDefault(role.Id)))];
    }

    /// <inheritdoc />
    public async Task<Result<RoleSummary>> CreateRoleAsync(
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Role> created = Role.Create(
            _tenantContext.TenantId, request.Name, request.Description ?? string.Empty);

        if (created.IsFailure)
        {
            return Result.Failure<RoleSummary>(created.Error);
        }

        Role role = created.Value;

        Result named = await EnsureRoleNameFreeAsync(role.Name, null, cancellationToken);

        if (named.IsFailure)
        {
            return Result.Failure<RoleSummary>(named.Error);
        }

        Result granted = await ApplyAsync(role, request, cancellationToken);

        if (granted.IsFailure)
        {
            return Result.Failure<RoleSummary>(granted.Error);
        }

        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(role, await PermissionCodesAsync(cancellationToken), 0));
    }

    /// <inheritdoc />
    public async Task<Result<RoleSummary>> UpdateRoleAsync(
        Guid roleId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Role? role = await _context.Roles
            .Include(candidate => candidate.Permissions)
            .Include(candidate => candidate.Menus)
            .FirstOrDefaultAsync(candidate => candidate.Id == RoleId.From(roleId), cancellationToken);

        if (role is null)
        {
            return Result.Failure<RoleSummary>(Error.NotFound("Role.NotFound", "That role does not exist."));
        }

        Result renamed = role.Rename(request.Name, request.Description ?? string.Empty);

        if (renamed.IsFailure)
        {
            return Result.Failure<RoleSummary>(renamed.Error);
        }

        Result named = await EnsureRoleNameFreeAsync(role.Name, role.Id, cancellationToken);

        if (named.IsFailure)
        {
            return Result.Failure<RoleSummary>(named.Error);
        }

        Result granted = await ApplyAsync(role, request, cancellationToken);

        if (granted.IsFailure)
        {
            return Result.Failure<RoleSummary>(granted.Error);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionChecker.InvalidateTenantAsync(_tenantContext.TenantId, cancellationToken);

        int holders = await _context.Set<UserRole>().CountAsync(link => link.RoleId == role.Id, cancellationToken);

        return Result.Success(Describe(role, await PermissionCodesAsync(cancellationToken), holders));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        Role? role = await _context.Roles
            .FirstOrDefaultAsync(candidate => candidate.Id == RoleId.From(roleId), cancellationToken);

        if (role is null)
        {
            return Result.Failure(Error.NotFound("Role.NotFound", "That role does not exist."));
        }

        Result deletable = role.EnsureDeletable();

        if (deletable.IsFailure)
        {
            return deletable;
        }

        int holders = await _context.Set<UserRole>().CountAsync(link => link.RoleId == role.Id, cancellationToken);

        if (holders > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "Role.InUse",
                $"'{role.Name}' is held by {holders} user(s). Give them another role first."));
        }

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionDefinition>> ListPermissionsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.Permissions
            .AsNoTracking()
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Resource)
            .ThenBy(permission => permission.Verb)
            .Select(permission => new PermissionDefinition(
                permission.Code,
                permission.Module,
                permission.Resource,
                permission.Verb.ToString().ToLower(),
                permission.Description))
            .ToListAsync(cancellationToken);

    private static UserSummary Describe(User user) =>
        new(
            user.Id.Value,
            user.UserName,
            user.Email,
            user.DisplayName,
            user.MobileNumber,
            user.IsActive,
            user.MustChangePassword,
            user.LockedOutUntilUtc is { } until && until > DateTimeOffset.UtcNow,
            user.LastLoginAtUtc,
            [.. user.Roles.Select(link => link.RoleId.Value)]);

    private static RoleSummary Describe(
        Role role,
        IReadOnlyDictionary<PermissionId, string> codes,
        int holders) =>
        new(
            role.Id.Value,
            role.Name,
            role.Description,
            role.IsSystemRole,
            role.GrantsAllPermissions,
            [.. role.Permissions.Select(grant => codes.GetValueOrDefault(grant.PermissionId)).OfType<string>().Order(StringComparer.Ordinal)],
            role.RestrictsMenu,
            [.. role.Menus.Select(menu => menu.MenuCode).Order(StringComparer.Ordinal)],
            holders);

    private async Task<Dictionary<PermissionId, string>> PermissionCodesAsync(
        CancellationToken cancellationToken) =>
        await _context.Permissions
            .AsNoTracking()
            .ToDictionaryAsync(permission => permission.Id, permission => permission.Code, cancellationToken);

    /// <summary>Grants the permissions and chooses the menus a request names.</summary>
    private async Task<Result> ApplyAsync(
        Role role,
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        // A role that holds everything holds it by its flag, and a list of codes beside
        // it would only be a second, contradictory answer.
        if (!role.GrantsAllPermissions)
        {
            HashSet<string> wanted = [.. (request.PermissionCodes ?? []).Select(code => code.Trim().ToLowerInvariant())];

            List<Permission> found = await _context.Permissions
                .Where(permission => wanted.Contains(permission.Code))
                .ToListAsync(cancellationToken);

            string? unknown = wanted.FirstOrDefault(code => !found.Exists(permission => permission.Code == code));

            if (unknown is not null)
            {
                return Result.Failure(Error.Validation(
                    "Role.UnknownPermission", $"'{unknown}' is not a permission."));
            }

            role.ReplacePermissions(found.Select(permission => permission.Id));
        }

        role.ChooseMenus(request.MenuCodes);

        return Result.Success();
    }

    private async Task<Result<IReadOnlyList<Role>>> LoadRolesAsync(
        IReadOnlyList<Guid>? roleIds,
        CancellationToken cancellationToken)
    {
        List<RoleId> wanted = [.. (roleIds ?? []).Distinct().Select(RoleId.From)];

        if (wanted.Count == 0)
        {
            return Result.Failure<IReadOnlyList<Role>>(Error.Validation(
                "User.RoleRequired", "A user needs at least one role, or they can do nothing."));
        }

        List<Role> roles = await _context.Roles
            .Where(role => wanted.Contains(role.Id))
            .ToListAsync(cancellationToken);

        return roles.Count == wanted.Count
            ? Result.Success<IReadOnlyList<Role>>(roles)
            : Result.Failure<IReadOnlyList<Role>>(Error.NotFound(
                "Role.NotFound", "One of those roles does not exist."));
    }

    private async Task<Result> EnsureUniqueAsync(
        string userName,
        string email,
        UserId? except,
        CancellationToken cancellationToken)
    {
        bool nameTaken = await _context.Users.AnyAsync(
            user => user.UserName == userName && (except == null || user.Id != except),
            cancellationToken);

        if (nameTaken)
        {
            return Result.Failure(Error.Conflict(
                "User.UserNameTaken", $"The user name '{userName}' is already in use."));
        }

        bool emailTaken = await _context.Users.AnyAsync(
            user => user.Email == email && (except == null || user.Id != except),
            cancellationToken);

        return emailTaken
            ? Result.Failure(Error.Conflict(
                "User.EmailTaken", $"The email address '{email}' is already in use."))
            : Result.Success();
    }

    private async Task<Result> EnsureRoleNameFreeAsync(
        string name,
        RoleId? except,
        CancellationToken cancellationToken)
    {
        string lowered = name.ToLower();

        bool taken = await _context.Roles.AnyAsync(
            role => role.Name.ToLower() == lowered && (except == null || role.Id != except),
            cancellationToken);

        return taken
            ? Result.Failure(Error.Conflict("Role.NameTaken", $"A role named '{name}' already exists."))
            : Result.Success();
    }

    /// <summary>Refuses a change that would leave nobody able to administer the tenant.</summary>
    private async Task<Result> EnsureAnAdministratorRemainsAsync(
        User changed,
        IReadOnlyList<Role> itsRoles,
        CancellationToken cancellationToken)
    {
        if (changed.IsActive && itsRoles.Any(role => role.GrantsAllPermissions))
        {
            return Result.Success();
        }

        List<RoleId> everything = await _context.Roles
            .Where(role => role.GrantsAllPermissions)
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);

        bool another = await _context.Users.AnyAsync(
            user => user.Id != changed.Id
                && user.IsActive
                && _context.Set<UserRole>().Any(link => link.UserId == user.Id && everything.Contains(link.RoleId)),
            cancellationToken);

        return another
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "User.LastAdministrator",
                $"'{changed.UserName}' is the only active administrator. Make somebody else "
                + "an administrator first."));
    }
}
