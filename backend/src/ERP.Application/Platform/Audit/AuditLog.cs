using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Audit;

/// <summary>Searches the audit trail.</summary>
/// <param name="Module">Only changes to records of this module.</param>
/// <param name="UserId">Only changes made by this user.</param>
/// <param name="Action">Only this kind of change: Create, Update or Delete.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Search">Text in the record's number, code or name, or its identifier.</param>
/// <param name="Page">The page, from one.</param>
/// <param name="PageSize">How many entries a page holds.</param>
public sealed record ListAuditLogQuery(
    string? Module = null,
    Guid? UserId = null,
    string? Action = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IQuery<AuditLogPage>;

/// <summary>Lists what the audit trail can be filtered by.</summary>
public sealed record GetAuditLogFiltersQuery : IQuery<AuditLogFilters>;

/// <summary>One change as the trail shows it.</summary>
/// <param name="Id">The entry.</param>
/// <param name="OccurredAtUtc">When.</param>
/// <param name="UserId">Who.</param>
/// <param name="UserName">Their sign-in name at the time.</param>
/// <param name="Module">The module.</param>
/// <param name="EntityType">The kind of record.</param>
/// <param name="RecordId">The record's identifier.</param>
/// <param name="RecordLabel">Its number, code or name.</param>
/// <param name="Action">Create, Update or Delete.</param>
/// <param name="OldValues">The values before, as JSON.</param>
/// <param name="NewValues">The values after, as JSON.</param>
/// <param name="BatchId">The save it was part of.</param>
public sealed record AuditLogEntry(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid UserId,
    string UserName,
    string Module,
    string EntityType,
    string RecordId,
    string? RecordLabel,
    string Action,
    string? OldValues,
    string? NewValues,
    Guid BatchId);

/// <summary>One page of the trail.</summary>
/// <param name="Items">The entries, newest first.</param>
/// <param name="Page">The page, from one.</param>
/// <param name="PageSize">How many a page holds.</param>
/// <param name="TotalCount">How many match in all.</param>
public sealed record AuditLogPage(IReadOnlyList<AuditLogEntry> Items, int Page, int PageSize, int TotalCount);

/// <summary>A user who appears in the trail.</summary>
/// <param name="UserId">The user.</param>
/// <param name="UserName">Their sign-in name.</param>
public sealed record AuditLogUser(Guid UserId, string UserName);

/// <summary>What the trail can be filtered by.</summary>
/// <param name="Modules">The modules that appear in it.</param>
/// <param name="Users">The users who appear in it.</param>
/// <param name="IsSeparateDatabase">Whether the trail has a database of its own.</param>
public sealed record AuditLogFilters(
    IReadOnlyList<string> Modules,
    IReadOnlyList<AuditLogUser> Users,
    bool IsSeparateDatabase);

/// <summary>Reads the audit trail.</summary>
public interface IAuditLogReader
{
    /// <summary>Searches a tenant's trail for one firm.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="firmId">The firm; changes not tied to any firm are included too.</param>
    /// <param name="query">The filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page.</returns>
    Task<AuditLogPage> ListAsync(
        TenantId tenantId,
        FirmId? firmId,
        ListAuditLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Lists what a tenant's trail can be filtered by.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The modules and users that appear in it.</returns>
    Task<AuditLogFilters> FiltersAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Handles the audit trail queries.</summary>
/// <remarks>
/// There are no commands. Nothing in the application changes or removes an entry, and the
/// database refuses it from anywhere else.
/// </remarks>
public sealed class AuditLogHandlers
    : IQueryHandler<ListAuditLogQuery, AuditLogPage>,
      IQueryHandler<GetAuditLogFiltersQuery, AuditLogFilters>
{
    private const int MaximumPageSize = 200;

    private readonly IAuditLogReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="AuditLogHandlers"/> class.</summary>
    /// <param name="reader">The trail reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public AuditLogHandlers(IAuditLogReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<AuditLogPage>> Handle(ListAuditLogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ListAuditLogQuery bounded = request with
        {
            Page = Math.Max(1, request.Page),
            PageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize),
        };

        return Result.Success(await _reader.ListAsync(
            _tenantContext.TenantId, _tenantContext.FirmId, bounded, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<AuditLogFilters>> Handle(
        GetAuditLogFiltersQuery request,
        CancellationToken cancellationToken) =>
        Result.Success(await _reader.FiltersAsync(_tenantContext.TenantId, cancellationToken));
}
