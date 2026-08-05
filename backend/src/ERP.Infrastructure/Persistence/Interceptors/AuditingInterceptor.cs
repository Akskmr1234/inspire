using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps tenancy and audit columns, and converts deletes into soft deletes,
/// immediately before changes are written.
/// </summary>
/// <remarks>
/// <para>
/// Doing this in an interceptor rather than in each command handler is what makes
/// the guarantees real. A handler that forgets to set <c>TenantId</c> writes a
/// row belonging to nobody, which the tenant query filter then hides from
/// everyone - a bug that presents as "my data vanished" long after the cause.
/// Here it cannot be forgotten.
/// </para>
/// <para>
/// Deletes are rewritten to updates for anything implementing
/// <see cref="ISoftDeletable"/>. An accounting system must be able to answer what
/// a deleted voucher contained, and a hard <c>DELETE</c> destroys that.
/// </para>
/// </remarks>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="AuditingInterceptor"/> class.</summary>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    public AuditingInterceptor(
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        UserId actor = _currentUser.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampTenant(entry);
                    SetIfPresent(entry, nameof(IAuditable.CreatedAtUtc), now);
                    SetIfPresent(entry, nameof(IAuditable.CreatedBy), actor);
                    break;

                case EntityState.Modified:
                    SetIfPresent(entry, nameof(IAuditable.ModifiedAtUtc), now);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedBy), actor);
                    GuardTenantImmutability(entry);
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletable:
                    entry.State = EntityState.Modified;
                    SetIfPresent(entry, nameof(ISoftDeletable.IsDeleted), true);
                    SetIfPresent(entry, nameof(ISoftDeletable.DeletedAtUtc), now);
                    SetIfPresent(entry, nameof(ISoftDeletable.DeletedBy), actor);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedAtUtc), now);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedBy), actor);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Stamps the owning tenant on a newly-added entity.</summary>
    /// <param name="entry">The change-tracker entry.</param>
    private void StampTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantScoped)
        {
            return;
        }

        PropertyEntry property = entry.Property(nameof(ITenantScoped.TenantId));

        // An aggregate created through a factory that already took the tenant
        // will have it set. Only fill in the gap, so a deliberate value is never
        // overwritten.
        if (property.CurrentValue is TenantId existing && existing.Value != Guid.Empty)
        {
            return;
        }

        if (!_tenantContext.IsResolved)
        {
            throw new InvalidOperationException(
                $"Cannot save {entry.Entity.GetType().Name}: it is tenant-scoped but no " +
                $"tenant has been resolved. Wrap the operation in " +
                $"ITenantContext.BeginScope when running outside a request.");
        }

        property.CurrentValue = _tenantContext.TenantId;
    }

    /// <summary>Prevents a row being moved between tenants by an update.</summary>
    /// <param name="entry">The change-tracker entry.</param>
    /// <remarks>
    /// Reassigning <c>TenantId</c> would hand one customer's record to another.
    /// There is no legitimate reason to do it, so an attempt is treated as a bug
    /// and stopped before it reaches the database.
    /// </remarks>
    private static void GuardTenantImmutability(EntityEntry entry)
    {
        if (entry.Entity is not ITenantScoped)
        {
            return;
        }

        PropertyEntry property = entry.Property(nameof(ITenantScoped.TenantId));

        if (property.IsModified)
        {
            throw new InvalidOperationException(
                $"The tenant of a {entry.Entity.GetType().Name} cannot be changed " +
                $"(attempted {property.OriginalValue} -> {property.CurrentValue}).");
        }
    }

    /// <summary>Sets a property when the entity actually declares it.</summary>
    /// <param name="entry">The change-tracker entry.</param>
    /// <param name="propertyName">The property to set.</param>
    /// <param name="value">The value to assign.</param>
    private static void SetIfPresent(EntityEntry entry, string propertyName, object? value)
    {
        if (entry.Metadata.FindProperty(propertyName) is not null)
        {
            entry.Property(propertyName).CurrentValue = value;
        }
    }
}
