using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Identity;

/// <summary>
/// A named set of permissions that can be granted to users.
/// </summary>
/// <remarks>
/// <para>
/// Tenant-scoped, because what a "Branch Manager" may do is a decision each
/// customer makes for themselves. The specification's six roles - Super
/// Administrator, Firm Administrator, Branch Manager, Accountant, Sales
/// Executive, Store Keeper - are seeded per tenant as a starting point, not
/// imposed as a fixed set.
/// </para>
/// <para>
/// A role may be scoped further to a single firm, so a customer running three
/// firms can give someone an Accountant role in one of them without granting it
/// across the group.
/// </para>
/// </remarks>
public sealed class Role : AggregateRoot<RoleId>, ITenantScoped, IAuditable, ISoftDeletable
{
    private readonly List<RolePermission> _permissions = [];

    private Role(RoleId id, TenantId tenantId, string name, string description, bool isSystemRole)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Description = description;
        IsSystemRole = isSystemRole;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Role()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the role name, unique within the tenant.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the description shown on the roles screen.</summary>
    public string Description { get; private set; }

    /// <summary>
    /// Gets the firm this role is confined to, or <see langword="null"/> when it
    /// applies across every firm in the tenant.
    /// </summary>
    public FirmId? FirmId { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is a seeded role that cannot be
    /// deleted.
    /// </summary>
    /// <remarks>
    /// Deleting the Super Administrator role would leave a tenant with no way to
    /// administer itself. System roles may still be renamed and have their
    /// permissions adjusted - only removal is blocked.
    /// </remarks>
    public bool IsSystemRole { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this role holds every permission
    /// implicitly.
    /// </summary>
    /// <remarks>
    /// Reserved for Super Administrator. Without it, a permission added by a later
    /// release would be held by nobody until an administrator noticed and granted
    /// it - and the administrator might be locked out of the very screen needed to
    /// do so.
    /// </remarks>
    public bool GrantsAllPermissions { get; private set; }

    /// <summary>Gets the permissions granted by this role.</summary>
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? DeletedBy { get; private set; }

    /// <summary>Creates a role.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="name">The role name.</param>
    /// <param name="description">A description.</param>
    /// <param name="firmId">The firm to confine the role to, if any.</param>
    /// <param name="isSystemRole">Whether the role is seeded and undeletable.</param>
    /// <param name="grantsAllPermissions">Whether the role implicitly holds everything.</param>
    /// <returns>The role, or a validation failure.</returns>
    public static Result<Role> Create(
        TenantId tenantId,
        string name,
        string description,
        FirmId? firmId = null,
        bool isSystemRole = false,
        bool grantsAllPermissions = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Role>(Error.Validation(
                "Role.NameRequired", "A role name is required."));
        }

        if (name.Trim().Length > 100)
        {
            return Result.Failure<Role>(Error.Validation(
                "Role.NameTooLong", "A role name cannot exceed 100 characters."));
        }

        Role role = new(RoleId.NewId(), tenantId, name.Trim(), description?.Trim() ?? string.Empty, isSystemRole)
        {
            FirmId = firmId,
            GrantsAllPermissions = grantsAllPermissions,
        };

        return Result.Success(role);
    }

    /// <summary>Grants a permission to this role.</summary>
    /// <param name="permissionId">The permission to grant.</param>
    /// <returns>Success, including when the permission was already granted.</returns>
    /// <remarks>
    /// Granting twice is not an error. An administrator ticking an already-ticked
    /// box, or a seed re-running, should be a no-op rather than a failure.
    /// </remarks>
    public Result Grant(PermissionId permissionId)
    {
        if (_permissions.Exists(p => p.PermissionId == permissionId))
        {
            return Result.Success();
        }

        _permissions.Add(new RolePermission(Id, permissionId, TenantId));
        Raise(new RolePermissionsChanged(Id, TenantId));

        return Result.Success();
    }

    /// <summary>Revokes a permission from this role.</summary>
    /// <param name="permissionId">The permission to revoke.</param>
    public void Revoke(PermissionId permissionId)
    {
        int removed = _permissions.RemoveAll(p => p.PermissionId == permissionId);

        if (removed > 0)
        {
            Raise(new RolePermissionsChanged(Id, TenantId));
        }
    }

    /// <summary>Replaces the role's permissions wholesale.</summary>
    /// <param name="permissionIds">The permissions the role should end up with.</param>
    /// <remarks>
    /// What the permissions screen submits: the administrator sees a full grid of
    /// checkboxes and saves the resulting set, rather than a sequence of individual
    /// grants and revokes.
    /// </remarks>
    public void ReplacePermissions(IEnumerable<PermissionId> permissionIds)
    {
        ArgumentNullException.ThrowIfNull(permissionIds);

        _permissions.Clear();

        foreach (PermissionId permissionId in permissionIds.Distinct())
        {
            _permissions.Add(new RolePermission(Id, permissionId, TenantId));
        }

        Raise(new RolePermissionsChanged(Id, TenantId));
    }

    /// <summary>Renames the role and updates its description.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Role.NameRequired", "A role name is required."));
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;

        return Result.Success();
    }

    /// <summary>Checks whether the role may be deleted.</summary>
    /// <returns>Success when deletion is permitted.</returns>
    public Result EnsureDeletable() => IsSystemRole
        ? Result.Failure(Error.BusinessRule(
            "Role.SystemRoleUndeletable",
            $"'{Name}' is a system role and cannot be deleted. It can be renamed, " +
            $"and its permissions can be changed."))
        : Result.Success();
}

/// <summary>Links a role to a permission it grants.</summary>
/// <remarks>
/// Carries <see cref="TenantId"/> so the join table is covered by the same
/// isolation policies as everything else. Without it, the row that says who may
/// approve a payment would be the one table in the database readable across
/// tenants.
/// </remarks>
public sealed class RolePermission : ITenantScoped
{
    /// <summary>Initialises a new instance of the <see cref="RolePermission"/> class.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="permissionId">The permission granted.</param>
    /// <param name="tenantId">The owning tenant.</param>
    internal RolePermission(RoleId roleId, PermissionId permissionId, TenantId tenantId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        TenantId = tenantId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private RolePermission()
    {
    }

    /// <summary>Gets the role.</summary>
    public RoleId RoleId { get; private set; }

    /// <summary>Gets the permission granted.</summary>
    public PermissionId PermissionId { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }
}

/// <summary>
/// Raised when a role's permissions change, so cached permission sets can be
/// invalidated.
/// </summary>
/// <param name="RoleId">The role.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <remarks>
/// Permission lookups are cached per user; without this event a revoked
/// permission would keep working until the cache expired, which is precisely the
/// wrong direction for a security change to lag in.
/// </remarks>
public sealed record RolePermissionsChanged(RoleId RoleId, TenantId TenantId) : DomainEvent;
