using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Copies each transaction's totals onto its master row as it is saved.
/// </summary>
/// <remarks>
/// <para>
/// Only a master whose lines and charges are in hand is touched: one loaded without them
/// - to post it, say, or cancel it - would add up to nothing, and nothing is exactly what
/// must not be written over a total that was right. A new master always has them.
/// </para>
/// <para>
/// Runs before the auditing interceptor, so a master whose only change is in its lines
/// is still stamped as modified.
/// </para>
/// </remarks>
public sealed class StoredTotalsInterceptor : SaveChangesInterceptor
{
    private const string Lines = "Lines";
    private const string Charges = "Charges";

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

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Deleted or EntityState.Detached
                || !StoredTotals.MasterTypes.Contains(entry.Metadata.ClrType))
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                // Everything a new master has is in hand; said so, a second save of it in
                // the same unit of work is trusted too.
                entry.Navigation(Lines).IsLoaded = true;
                entry.Navigation(Charges).IsLoaded = true;
            }
            else if (!entry.Navigation(Lines).IsLoaded || !entry.Navigation(Charges).IsLoaded)
            {
                continue;
            }

            if (StoredTotals.For(entry.Entity) is not { } totals)
            {
                continue;
            }

            for (int index = 0; index < totals.Length; index++)
            {
                PropertyEntry property = entry.Property(StoredTotals.MasterProperties[index]);

                if (property.CurrentValue is not decimal current || current != totals[index])
                {
                    property.CurrentValue = totals[index];
                }
            }
        }
    }
}
