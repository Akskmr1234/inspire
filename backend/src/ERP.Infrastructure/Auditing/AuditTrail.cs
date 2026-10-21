using System.Globalization;
using System.Text.Json;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Auditing;

/// <summary>Which records the trail follows, and the module each is shown under.</summary>
/// <remarks>
/// Masters and transactions only - what somebody creates, changes or deletes on purpose.
/// The bookkeeping a posting does behind them (stock positions, the stock ledger, numbering
/// series, bills) is left out: it is the consequence of an audited change, and recording it
/// too would bury the change under its own echoes.
/// </remarks>
internal static class AuditCatalogue
{
    private static readonly Dictionary<Type, Func<EntityEntry, string>> Modules = new()
    {
        [typeof(Ledger)] = entry => entry.Property(nameof(Ledger.Kind)).CurrentValue switch
        {
            LedgerKind.Customer => "Customer",
            LedgerKind.Supplier => "Supplier",
            LedgerKind.Employee => "Employee",
            _ => "Ledger",
        },
        [typeof(AccountGroup)] = _ => "Account group",
        [typeof(AdditionalLedger)] = _ => "Additional ledger",
        [typeof(Product)] = _ => "Product",
        [typeof(Category)] = _ => "Category",
        [typeof(Brand)] = _ => "Brand",
        [typeof(UnitOfMeasure)] = _ => "Unit of measure",
        [typeof(Warehouse)] = _ => "Warehouse",
        [typeof(TaxSlab)] = _ => "Tax rate",
        [typeof(BillOfMaterials)] = _ => "Bill of materials",
        [typeof(Country)] = _ => "Country",
        [typeof(Region)] = _ => "Region",
        [typeof(Territory)] = _ => "Area",
        [typeof(Firm)] = _ => "Firm",
        [typeof(Branch)] = _ => "Branch",
        [typeof(FinancialYear)] = _ => "Financial year",
        [typeof(FirmSetting)] = _ => "Settings",
        [typeof(InventoryAccountMap)] = _ => "Inventory accounts",
        [typeof(TaxAccountMap)] = _ => "Tax accounts",
        [typeof(User)] = _ => "User",
        [typeof(Role)] = _ => "Role",
        [typeof(Voucher)] = _ => "Voucher",
        [typeof(Cheque)] = _ => "Cheque",
        [typeof(SalesInvoice)] = entry =>
            Equals(entry.Property(nameof(SalesInvoice.Kind)).CurrentValue, SalesDocumentKind.Return)
                ? "Sales return"
                : "Sales",
        [typeof(SalesOrder)] = _ => "Sales order",
        [typeof(PurchaseInvoice)] = entry =>
            Equals(entry.Property(nameof(PurchaseInvoice.Kind)).CurrentValue, PurchaseDocumentKind.Return)
                ? "Purchase return"
                : "Purchase",
        [typeof(PurchaseOrder)] = _ => "Purchase order",
        [typeof(StockDocument)] = entry =>
            $"Stock: {entry.Property(nameof(StockDocument.Type)).CurrentValue}",
        [typeof(StockTransferRequest)] = _ => "Stock transfer request",
        [typeof(JobCard)] = _ => "Job card",
        [typeof(Rma)] = _ => "RMA",
    };

    /// <summary>Says whether a record is followed in its own right.</summary>
    /// <param name="type">The record's type.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsAudited(Type type) => Modules.ContainsKey(type);

    /// <summary>Names the module a record is shown under.</summary>
    /// <param name="entry">The record.</param>
    /// <returns>The module.</returns>
    internal static string ModuleOf(EntityEntry entry) => Modules[entry.Metadata.ClrType](entry);
}

/// <summary>
/// Turns what a save is about to change into audit entries, and holds them until the change
/// is committed.
/// </summary>
/// <remarks>
/// <para>
/// Collected before the save, because only then are the old values still known; written
/// after the commit, because a change that rolled back did not happen and must not be in
/// the trail. A save outside any transaction is its own commit and is written at once.
/// </para>
/// <para>
/// A record's own lines - an invoice's products, a voucher's legs - are reported with the
/// record rather than on their own, so one edit to an invoice is one entry that says which
/// lines came, went or changed.
/// </para>
/// </remarks>
public sealed partial class AuditTrail
{
    /// <summary>Strings longer than this are cut short: a photograph is evidence, not an audit value.</summary>
    private const int MaximumValueLength = 500;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "Version",
        nameof(IAuditable.CreatedAtUtc),
        nameof(IAuditable.CreatedBy),
        nameof(IAuditable.ModifiedAtUtc),
        nameof(IAuditable.ModifiedBy),
        "TenantId",
        "DeletedAtUtc",
        "DeletedBy",
        "FailedLoginAttempts",
        "LockedOutUntilUtc",
        "LastLoginAtUtc",
    };

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IDbContextFactory<AuditDbContext> _audit;
    private readonly ILogger<AuditTrail> _logger;
    private readonly List<AuditEntry> _staged = [];
    private readonly List<AuditEntry> _pending = [];

    /// <summary>Initialises a new instance of the <see cref="AuditTrail"/> class.</summary>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="audit">Opens the audit database.</param>
    /// <param name="logger">The logger.</param>
    public AuditTrail(
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IDbContextFactory<AuditDbContext> audit,
        ILogger<AuditTrail> logger)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Records what a save is about to change.</summary>
    /// <param name="context">The context being saved.</param>
    public void Stage(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _staged.Clear();

        // Nobody to hold to account: seeding, sign-in bookkeeping, background work.
        if (!_tenantContext.IsResolved || !_currentUser.IsAuthenticated)
        {
            return;
        }

        List<EntityEntry> changed =
        [
            .. context.ChangeTracker.Entries().Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
        ];

        Dictionary<(Type Type, object Key), Change> changes = [];

        foreach (EntityEntry entry in changed)
        {
            if (AuditCatalogue.IsAudited(entry.Metadata.ClrType))
            {
                Change change = ChangeFor(changes, entry.Metadata.ClrType, KeyOf(entry));
                change.Root = entry;
            }
            else if (OwnerOf(entry) is { } owner)
            {
                ChangeFor(changes, owner.Type, owner.Key).Children.Add(entry);
            }
        }

        Guid batch = Guid.CreateVersion7();
        DateTimeOffset now = _clock.UtcNow;

        foreach (((Type type, object key), Change change) in changes)
        {
            change.Root ??= context.ChangeTracker.Entries()
                .FirstOrDefault(entry => entry.Metadata.ClrType == type && Equals(KeyOf(entry), key));

            if (Describe(type, key, change, batch, now) is { } entry)
            {
                _staged.Add(entry);
            }
        }
    }

    /// <summary>The save went through: what it changed waits for the commit.</summary>
    /// <param name="inTransaction">Whether the save ran inside a transaction still to commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when anything that can be written has been.</returns>
    public async Task SavedAsync(bool inTransaction, CancellationToken cancellationToken)
    {
        _pending.AddRange(_staged);
        _staged.Clear();

        if (!inTransaction)
        {
            await FlushAsync(cancellationToken);
        }
    }

    /// <summary>The save failed: nothing it was going to change happened.</summary>
    public void Failed() => _staged.Clear();

    /// <summary>The transaction rolled back: nothing saved inside it happened.</summary>
    public void Discard()
    {
        _staged.Clear();
        _pending.Clear();
    }

    /// <summary>Writes what was committed to the audit database.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the entries are written.</returns>
    /// <remarks>
    /// The change itself is already committed by now, so a failure here is logged rather
    /// than thrown: refusing the user's save after the fact would leave them believing it
    /// had not happened.
    /// </remarks>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        List<AuditEntry> entries = [.. _pending];
        _pending.Clear();

        try
        {
            await using AuditDbContext audit = await _audit.CreateDbContextAsync(cancellationToken);
            audit.Entries.AddRange(entries);
            await audit.SaveChangesAsync(cancellationToken);
        }
#pragma warning disable CA1031 // The change is committed; losing its trail must not undo it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogWriteFailed(_logger, exception, entries.Count);
        }
    }

    private static Change ChangeFor(Dictionary<(Type Type, object Key), Change> changes, Type type, object key)
    {
        if (!changes.TryGetValue((type, key), out Change? change))
        {
            change = new Change();
            changes[(type, key)] = change;
        }

        return change;
    }

    private static object KeyOf(EntityEntry entry)
    {
        IKey? primary = entry.Metadata.FindPrimaryKey();

        return primary is null || primary.Properties.Count != 1
            ? entry.Entity
            : entry.Property(primary.Properties[0].Name).CurrentValue ?? entry.Entity;
    }

    /// <summary>The audited record a line belongs to, through the relationship that holds its lines.</summary>
    private static (Type Type, object Key)? OwnerOf(EntityEntry entry)
    {
        foreach (IForeignKey key in entry.Metadata.GetForeignKeys())
        {
            if (key.PrincipalToDependent is null
                || !AuditCatalogue.IsAudited(key.PrincipalEntityType.ClrType)
                || key.Properties.Count != 1)
            {
                continue;
            }

            PropertyEntry property = entry.Property(key.Properties[0].Name);

            if ((property.CurrentValue ?? property.OriginalValue) is { } owner)
            {
                return (key.PrincipalEntityType.ClrType, owner);
            }
        }

        return null;
    }

    private static bool IsSensitive(string name) =>
        name.Contains("Password", StringComparison.Ordinal)
        || name.EndsWith("Hash", StringComparison.Ordinal)
        || name.Contains("Secret", StringComparison.Ordinal)
        || name.Contains("Token", StringComparison.Ordinal);

    private static IEnumerable<(string Name, PropertyEntry Property)> Properties(EntityEntry entry)
    {
        foreach (PropertyEntry property in entry.Properties)
        {
            string name = property.Metadata.Name;

            if (!property.Metadata.IsShadowProperty() && !Skipped.Contains(name) && !IsSensitive(name))
            {
                yield return (name, property);
            }
        }

        foreach (ComplexPropertyEntry complex in entry.ComplexProperties)
        {
            foreach (PropertyEntry property in complex.Properties)
            {
                yield return ($"{complex.Metadata.Name}.{property.Metadata.Name}", property);
            }
        }
    }

    private static Dictionary<string, object?> Values(EntityEntry entry, bool original) =>
        Properties(entry).ToDictionary(
            pair => pair.Name,
            pair => Format(original ? pair.Property.OriginalValue : pair.Property.CurrentValue),
            StringComparer.Ordinal);

    private static (Dictionary<string, object?> Before, Dictionary<string, object?> After) Differences(EntityEntry entry)
    {
        Dictionary<string, object?> before = new(StringComparer.Ordinal);
        Dictionary<string, object?> after = new(StringComparer.Ordinal);

        foreach ((string name, PropertyEntry property) in Properties(entry))
        {
            object? old = Format(property.OriginalValue);
            object? now = Format(property.CurrentValue);

            if (property.IsModified && !Equals(old, now))
            {
                before[name] = old;
                after[name] = now;
            }
        }

        return (before, after);
    }

    private static object? Format(object? value) => value switch
    {
        null => null,
        string text => text.Length <= MaximumValueLength
            ? text
            : string.Create(CultureInfo.InvariantCulture, $"{text[..60]}… ({text.Length} characters)"),
        bool or int or long or short or decimal or double or float => value,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        Enum choice => choice.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string? Label(EntityEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        foreach (string name in new[] { "Number", "Code", "Name", "UserName", "Key", "ChequeNumber" })
        {
            if (entry.Metadata.FindProperty(name) is not null
                && Format(entry.Property(name).CurrentValue ?? entry.Property(name).OriginalValue) is string label
                && label.Length > 0)
            {
                return label.Length <= 300 ? label : label[..300];
            }
        }

        return null;
    }

    private static string? Serialise(Dictionary<string, object?> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values, Json);

    private AuditEntry? Describe(Type type, object key, Change change, Guid batch, DateTimeOffset now)
    {
        EntityEntry? root = change.Root;
        string action;
        Dictionary<string, object?> before = new(StringComparer.Ordinal);
        Dictionary<string, object?> after = new(StringComparer.Ordinal);

        if (root?.State == EntityState.Added)
        {
            action = "Create";
            after = Values(root, original: false);
        }
        else if (root?.State == EntityState.Deleted)
        {
            action = "Delete";
            before = Values(root, original: true);
        }
        else
        {
            bool softDeleted = root is { Entity: ISoftDeletable }
                && root.Property(nameof(ISoftDeletable.IsDeleted)) is { IsModified: true, CurrentValue: true };

            action = softDeleted ? "Delete" : "Update";

            if (root?.State == EntityState.Modified)
            {
                (before, after) = Differences(root);
            }
        }

        foreach (IGrouping<string, EntityEntry> lines in change.Children.GroupBy(child => child.Metadata.ClrType.Name))
        {
            List<Dictionary<string, object?>> went = [];
            List<Dictionary<string, object?>> came = [];

            foreach (EntityEntry line in lines)
            {
                switch (line.State)
                {
                    case EntityState.Added:
                        came.Add(Values(line, original: false));
                        break;
                    case EntityState.Deleted:
                        went.Add(Values(line, original: true));
                        break;
                    default:
                        (Dictionary<string, object?> old, Dictionary<string, object?> now2) = Differences(line);

                        if (old.Count > 0)
                        {
                            old["Id"] = Format(KeyOf(line));
                            now2["Id"] = Format(KeyOf(line));
                            went.Add(old);
                            came.Add(now2);
                        }

                        break;
                }
            }

            if (went.Count > 0)
            {
                before[lines.Key] = went;
            }

            if (came.Count > 0)
            {
                after[lines.Key] = came;
            }
        }

        // Only the stamps moved: nothing anybody would call a change.
        if (before.Count == 0 && after.Count == 0)
        {
            return null;
        }

        Guid? firm = root?.Entity is IFirmScoped scoped ? scoped.FirmId.Value : _tenantContext.FirmId?.Value;

        return new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantContext.TenantId.Value,
            FirmId = firm,
            OccurredAtUtc = now,
            UserId = _currentUser.UserId.Value,
            UserName = _currentUser.UserName ?? string.Empty,
            Module = root is null ? type.Name : AuditCatalogue.ModuleOf(root),
            EntityType = type.Name,
            RecordId = Convert.ToString(Format(key), CultureInfo.InvariantCulture) ?? string.Empty,
            RecordLabel = Label(root),
            Action = action,
            OldValues = Serialise(before),
            NewValues = Serialise(after),
            BatchId = batch,
        };
    }

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Error,
        Message = "Writing {Count} audit entries failed; the changes they describe are committed.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, int count);

    private sealed class Change
    {
        public EntityEntry? Root { get; set; }

        public List<EntityEntry> Children { get; } = [];
    }
}

/// <summary>Feeds every save of the application's context to the audit trail.</summary>
public sealed class AuditTrailInterceptor : SaveChangesInterceptor
{
    private readonly AuditTrail _trail;

    /// <summary>Initialises a new instance of the <see cref="AuditTrailInterceptor"/> class.</summary>
    /// <param name="trail">The audit trail.</param>
    public AuditTrailInterceptor(AuditTrail trail) => _trail = trail;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            _trail.Stage(context);
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            _trail.Stage(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _trail.SavedAsync(eventData.Context?.Database.CurrentTransaction is not null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await _trail.SavedAsync(eventData.Context?.Database.CurrentTransaction is not null, cancellationToken);

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _trail.Failed();
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _trail.Failed();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
