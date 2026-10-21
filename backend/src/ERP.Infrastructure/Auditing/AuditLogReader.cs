using ERP.Application.Platform.Audit;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Auditing;

/// <summary>Reads the audit trail from its own database.</summary>
public sealed class AuditLogReader : IAuditLogReader
{
    private readonly IDbContextFactory<AuditDbContext> _audit;
    private readonly AuditStore _store;

    /// <summary>Initialises a new instance of the <see cref="AuditLogReader"/> class.</summary>
    /// <param name="audit">Opens the audit database.</param>
    /// <param name="store">Where the trail is kept.</param>
    public AuditLogReader(IDbContextFactory<AuditDbContext> audit, AuditStore store)
    {
        _audit = audit;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<AuditLogPage> ListAsync(
        TenantId tenantId,
        FirmId? firmId,
        ListAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using AuditDbContext audit = await _audit.CreateDbContextAsync(cancellationToken);

        Guid tenant = tenantId.Value;
        Guid? firm = firmId?.Value;

        IQueryable<AuditEntry> entries = audit.Entries
            .AsNoTracking()
            .Where(entry => entry.TenantId == tenant && (firm == null || entry.FirmId == null || entry.FirmId == firm));

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            entries = entries.Where(entry => entry.Module == query.Module);
        }

        if (query.UserId is { } user)
        {
            entries = entries.Where(entry => entry.UserId == user);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (query.From is { } from)
        {
            DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            entries = entries.Where(entry => entry.OccurredAtUtc >= start);
        }

        if (query.To is { } to)
        {
            DateTimeOffset end = new(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            entries = entries.Where(entry => entry.OccurredAtUtc < end);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = $"%{query.Search.Trim()}%";

            entries = entries.Where(entry =>
                (entry.RecordLabel != null && EF.Functions.ILike(entry.RecordLabel, term))
                || EF.Functions.ILike(entry.RecordId, term)
                || EF.Functions.ILike(entry.UserName, term));
        }

        int total = await entries.CountAsync(cancellationToken);

        List<AuditLogEntry> page = await entries
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(entry => new AuditLogEntry(
                entry.Id,
                entry.OccurredAtUtc,
                entry.UserId,
                entry.UserName,
                entry.Module,
                entry.EntityType,
                entry.RecordId,
                entry.RecordLabel,
                entry.Action,
                entry.OldValues,
                entry.NewValues,
                entry.BatchId))
            .ToListAsync(cancellationToken);

        return new AuditLogPage(page, query.Page, query.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<AuditLogFilters> FiltersAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        await using AuditDbContext audit = await _audit.CreateDbContextAsync(cancellationToken);

        Guid tenant = tenantId.Value;

        List<string> modules = await audit.Entries
            .Where(entry => entry.TenantId == tenant)
            .Select(entry => entry.Module)
            .Distinct()
            .OrderBy(module => module)
            .ToListAsync(cancellationToken);

        List<AuditLogUser> users = await audit.Entries
            .Where(entry => entry.TenantId == tenant)
            .GroupBy(entry => entry.UserId)
            .Select(group => new AuditLogUser(group.Key, group.Max(entry => entry.UserName)!))
            .ToListAsync(cancellationToken);

        return new AuditLogFilters(
            modules,
            [.. users.OrderBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)],
            _store.IsSeparate);
    }
}
