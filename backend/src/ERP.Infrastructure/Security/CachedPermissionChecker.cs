using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Identity;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Security;

/// <summary>
/// Resolves a user's permissions from the database, with a short-lived cache.
/// </summary>
/// <remarks>
/// <para>
/// Authorisation runs on every request, often several times, so an uncached
/// implementation would put a join across users, roles, and permissions in front
/// of everything. The cache keeps the common path in memory.
/// </para>
/// <para>
/// The expiry is deliberately short, and entries are dropped explicitly when
/// roles or role permissions change. Both matter: the eviction handles the normal
/// case promptly, and the expiry bounds the damage if an eviction is ever missed.
/// A stale <em>grant</em> is a security hole, unlike a stale denial, so the
/// failure mode is biased towards re-reading the database.
/// </para>
/// </remarks>
public sealed partial class CachedPermissionChecker : IPermissionChecker
{
    /// <summary>
    /// The pseudo-permission held by a role that grants everything.
    /// </summary>
    /// <remarks>
    /// Rather than expanding a Super Administrator's grant into every code in the
    /// catalogue - which would go stale the moment a release adds one - the
    /// wildcard is stored and matched directly.
    /// </remarks>
    private const string WildcardPermission = "*";

    /// <summary>
    /// How long a resolved permission set is cached.
    /// </summary>
    /// <remarks>
    /// Two minutes is the ceiling on how long a revoked permission could keep
    /// working if an eviction were missed. Long enough to absorb a burst of
    /// requests, short enough that nobody has to think hard about the exposure.
    /// </remarks>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<TenantId, int>
        TenantGenerations = new();

    private readonly ErpDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CachedPermissionChecker> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="CachedPermissionChecker"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="logger">The logger.</param>
    public CachedPermissionChecker(
        ErpDbContext context,
        IMemoryCache cache,
        ITenantContext tenantContext,
        ILogger<CachedPermissionChecker> logger)
    {
        _context = context;
        _cache = cache;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        UserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        IReadOnlySet<string> permissions = await GetPermissionsAsync(userId, cancellationToken);

        return permissions.Contains(WildcardPermission)
            || permissions.Contains(permissionCode.Trim().ToLowerInvariant());
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        string key = CacheKey(userId);

        if (_cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlySet<string> permissions = await LoadAsync(userId, cancellationToken);

        _cache.Set(key, permissions, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = permissions.Count,
        });

        return permissions;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        _cache.Remove(CacheKey(userId));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateTenantAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        // IMemoryCache cannot enumerate or evict by prefix, so a generation counter
        // is bumped instead: every cache key embeds the tenant's current
        // generation, and raising it orphans all of that tenant's entries at once.
        // They expire on their own shortly afterwards.
        TenantGenerations.AddOrUpdate(tenantId, 1, (_, current) => current + 1);

        LogTenantInvalidated(_logger, tenantId.Value);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Source-generated so the tenant identifier is not boxed into a params array
    /// on a path that runs whenever a role changes.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenantId">The tenant whose cached permissions were dropped.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Invalidated cached permissions for tenant {TenantId}")]
    private static partial void LogTenantInvalidated(ILogger logger, Guid tenantId);

    private string CacheKey(UserId userId)
    {
        int generation = _tenantContext.IsResolved
            && TenantGenerations.TryGetValue(_tenantContext.TenantId, out int value)
                ? value
                : 0;

        return $"permissions:{userId.Value}:{generation}";
    }

    private async Task<IReadOnlySet<string>> LoadAsync(
        UserId userId,
        CancellationToken cancellationToken)
    {
        // One round trip. The join is over small tables and is the reason the
        // result is worth caching rather than the reason to avoid the query.
        var grants = await (
            from user in _context.Users.AsNoTracking()
            join userRole in _context.Set<UserRole>() on user.Id equals userRole.UserId
            join role in _context.Roles on userRole.RoleId equals role.Id
            where user.Id == userId && user.IsActive
            select new
            {
                role.GrantsAllPermissions,
                Codes = role.Permissions
                    .Join(
                        _context.Permissions,
                        rolePermission => rolePermission.PermissionId,
                        permission => permission.Id,
                        (_, permission) => permission.Code),
            }).ToListAsync(cancellationToken);

        HashSet<string> permissions = new(StringComparer.Ordinal);

        foreach (var grant in grants)
        {
            if (grant.GrantsAllPermissions)
            {
                permissions.Add(WildcardPermission);
                continue;
            }

            foreach (string code in grant.Codes)
            {
                permissions.Add(code);
            }
        }

        return permissions;
    }
}
