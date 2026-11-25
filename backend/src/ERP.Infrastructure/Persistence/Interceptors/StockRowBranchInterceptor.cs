using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Puts on each new stock movement the branch it was made in.
/// </summary>
/// <remarks>
/// The database then prefers the warehouse's own branch, where the warehouse has one, and
/// adds the batch's expiry date: a movement into a godown of another branch belongs to
/// that branch.
/// </remarks>
public sealed class StockRowBranchInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="StockRowBranchInterceptor"/> class.</summary>
    /// <param name="tenantContext">The ambient tenant, firm and branch.</param>
    public StockRowBranchInterceptor(ITenantContext tenantContext) => _tenantContext = tenantContext;

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
        if (context is null || _tenantContext.BranchId is not { } branch)
        {
            return;
        }

        foreach (EntityEntry<StockLedgerEntry> entry in context.ChangeTracker.Entries<StockLedgerEntry>())
        {
            if (entry.State == EntityState.Added && entry.Property(StockRows.BranchId).CurrentValue is null)
            {
                entry.Property(StockRows.BranchId).CurrentValue = branch.Value;
            }
        }
    }
}
