using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes on each voucher the transaction its entries came from, as the transaction that
/// names it is saved.
/// </summary>
/// <remarks>
/// <para>
/// A posting raises its voucher and records it on the document in the same unit of work,
/// so the voucher is usually being saved alongside: its link is set before it is written.
/// One saved earlier is linked straight afterwards, in a single statement that only fills
/// a link that is still empty.
/// </para>
/// <para>
/// A link, once written, is never changed: a voucher came from one transaction.
/// </para>
/// </remarks>
public sealed class VoucherSourceInterceptor : SaveChangesInterceptor
{
    private const string Sql =
        "UPDATE vouchers SET source_type = {1}, source_id = {2}, source_number = {3} "
        + "WHERE id = {0} AND source_id IS NULL";

    private readonly Dictionary<DbContext, List<VoucherSource>> _pending = [];
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Link(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Link(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            foreach (VoucherSource source in Take(context))
            {
                context.Database.ExecuteSqlRaw(Sql, Parameters(source));
            }
        }

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            foreach (VoucherSource source in Take(context))
            {
                await context.Database.ExecuteSqlRawAsync(Sql, Parameters(source), cancellationToken);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _ = Take(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _ = Take(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static object[] Parameters(VoucherSource source) =>
        [source.VoucherId!.Value.Value, source.Type, source.Id, source.Number];

    private void Link(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // The best claim on each voucher named by a document being written.
        Dictionary<VoucherId, VoucherSource> claims = [];

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (VoucherSource source in VoucherSources.For(entry.Entity))
            {
                if (source.VoucherId is { } id
                    && (!claims.TryGetValue(id, out VoucherSource? held) || source.Priority < held.Priority))
                {
                    claims[id] = source;
                }
            }
        }

        if (claims.Count == 0)
        {
            return;
        }

        Dictionary<VoucherId, EntityEntry<Voucher>> tracked = context.ChangeTracker.Entries<Voucher>()
            .Where(entry => entry.State != EntityState.Deleted)
            .ToDictionary(entry => entry.Entity.Id);

        List<VoucherSource> later = [];

        foreach ((VoucherId id, VoucherSource source) in claims)
        {
            if (tracked.TryGetValue(id, out EntityEntry<Voucher>? voucher))
            {
                if (voucher.Property(VoucherSources.Id).CurrentValue is null)
                {
                    voucher.Property(VoucherSources.Type).CurrentValue = Clip(source.Type, VoucherSources.MaximumTypeLength);
                    voucher.Property(VoucherSources.Id).CurrentValue = source.Id;
                    voucher.Property(VoucherSources.Number).CurrentValue = Clip(source.Number, VoucherSources.MaximumNumberLength);
                }
            }
            else
            {
                later.Add(source with
                {
                    Type = Clip(source.Type, VoucherSources.MaximumTypeLength),
                    Number = Clip(source.Number, VoucherSources.MaximumNumberLength),
                });
            }
        }

        if (later.Count > 0)
        {
            lock (_gate)
            {
                _pending[context] = later;
            }
        }
    }

    private List<VoucherSource> Take(DbContext? context)
    {
        if (context is null)
        {
            return [];
        }

        lock (_gate)
        {
            return _pending.Remove(context, out List<VoucherSource>? later) ? later : [];
        }
    }

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
}
