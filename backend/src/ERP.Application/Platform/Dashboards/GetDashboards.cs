using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Dashboards;

/// <summary>Lists the dashboards the signed-in user has been given.</summary>
/// <remarks>
/// A dashboard is assigned to roles rather than filtered by permission, so this is a
/// read of what somebody was meant to look at. What they may actually see inside it is
/// still decided by the metrics' own authorisation - see
/// <see cref="GetDashboardDataQuery"/>.
/// </remarks>
public sealed record GetDashboardsQuery : IQuery<DashboardsResponse>;

/// <summary>One panel on a dashboard.</summary>
/// <param name="Id">The widget.</param>
/// <param name="MetricCode">The metric the server computes, or null when custom.</param>
/// <param name="IsCustom">Whether the panel runs a query of somebody's own.</param>
/// <param name="Title">The heading.</param>
/// <param name="TitleArabic">The heading in Arabic.</param>
/// <param name="Kind">How the figure is drawn.</param>
/// <param name="Span">How many grid columns it occupies.</param>
public sealed record DashboardWidgetView(
    Guid Id,
    string? MetricCode,
    string Title,
    string? TitleArabic,
    WidgetKind Kind,
    int Span,
    bool IsCustom = false);

/// <summary>One dashboard.</summary>
/// <param name="Id">The dashboard.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="Widgets">Its panels, in display order.</param>
public sealed record DashboardView(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    IReadOnlyList<DashboardWidgetView> Widgets);

/// <summary>The dashboards available to the caller.</summary>
/// <param name="Dashboards">The dashboards, in display order.</param>
public sealed record DashboardsResponse(IReadOnlyList<DashboardView> Dashboards);

/// <summary>Reads the dashboards a user has been assigned.</summary>
public interface IDashboardReader
{
    /// <summary>Reads the dashboards assigned to any role a user holds.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The dashboards with their panels, in display order.</returns>
    /// <remarks>
    /// A dashboard assigned to two of somebody's roles is returned once. Overlapping
    /// audiences are the ordinary case rather than a mistake - the specification's own
    /// worked example has them - so de-duplication belongs here rather than in every
    /// caller.
    /// </remarks>
    Task<IReadOnlyList<DashboardView>> ReadForUserAsync(
        FirmId firmId,
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the statements behind one dashboard's custom panels.</summary>
    /// <param name="dashboardId">The dashboard.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The query for each custom widget, keyed by widget.</returns>
    /// <remarks>
    /// Deliberately not part of <see cref="DashboardWidgetView"/>. The statement names
    /// tables and columns, and a reader who may see a panel is not thereby entitled to
    /// a map of the schema behind it - so the SQL never leaves the server.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> ReadWidgetQueriesAsync(
        Guid dashboardId,
        CancellationToken cancellationToken = default);
}

/// <summary>Adds a panel driven by a query of somebody's own.</summary>
/// <param name="DashboardId">The dashboard to add it to.</param>
/// <param name="Query">The statement. Must return <c>label</c> and <c>value</c>.</param>
/// <param name="Title">The heading shown on the panel.</param>
/// <param name="Kind">How the result is drawn.</param>
/// <param name="SortOrder">The position among the dashboard's panels.</param>
/// <param name="Span">How many grid columns it occupies.</param>
public sealed record AddCustomWidgetCommand(
    Guid DashboardId,
    string Query,
    string Title,
    WidgetKind Kind,
    int SortOrder = 0,
    int Span = 1) : ICommand<Guid>;

/// <summary>Removes a panel from a dashboard.</summary>
/// <param name="DashboardId">The dashboard.</param>
/// <param name="WidgetId">The panel.</param>
public sealed record RemoveWidgetCommand(Guid DashboardId, Guid WidgetId) : ICommand;

/// <summary>Reads and writes dashboards.</summary>
public interface IDashboardRepository
{
    /// <summary>Finds a dashboard with its panels.</summary>
    /// <param name="id">The dashboard.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The dashboard, or <see langword="null"/>.</returns>
    Task<Dashboard?> FindAsync(
        DashboardId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="AddCustomWidgetCommand"/>.</summary>
public sealed class AddCustomWidgetCommandHandler
    : ICommandHandler<AddCustomWidgetCommand, Guid>
{
    private readonly IDashboardRepository _dashboards;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="AddCustomWidgetCommandHandler"/> class.</summary>
    /// <param name="dashboards">The dashboard repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public AddCustomWidgetCommandHandler(
        IDashboardRepository dashboards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _dashboards = dashboards;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        AddCustomWidgetCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validated before the aggregate sees it, so a refusal names what is wrong with
        // the SQL rather than reporting a generic invalid-widget error.
        Result<string> validated = CustomWidgetQuery.Validate(request.Query);

        if (validated.IsFailure)
        {
            return Result.Failure<Guid>(validated.Error);
        }

        Result<Dashboard> found = await ResolveAsync(request.DashboardId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<Guid>(found.Error);
        }

        Result<DashboardWidget> added = found.Value.AddCustomWidget(
            validated.Value, request.Title, request.Kind, request.SortOrder, request.Span);

        if (added.IsFailure)
        {
            return Result.Failure<Guid>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(added.Value.Id.Value);
    }

    private async Task<Result<Dashboard>> ResolveAsync(
        Guid dashboardId,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Dashboard>(Error.Forbidden(
                "Dashboard.NoFirmSelected", "A firm must be selected to edit a dashboard."));
        }

        Dashboard? dashboard = await _dashboards.FindAsync(
            DashboardId.From(dashboardId), cancellationToken);

        return dashboard is null || dashboard.FirmId != firmId
            ? Result.Failure<Dashboard>(Error.NotFound(
                "Dashboard.NotFound", "No such dashboard in the selected firm."))
            : Result.Success(dashboard);
    }
}

/// <summary>Handles <see cref="RemoveWidgetCommand"/>.</summary>
public sealed class RemoveWidgetCommandHandler : ICommandHandler<RemoveWidgetCommand>
{
    private readonly IDashboardRepository _dashboards;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="RemoveWidgetCommandHandler"/> class.</summary>
    /// <param name="dashboards">The dashboard repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public RemoveWidgetCommandHandler(
        IDashboardRepository dashboards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _dashboards = dashboards;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        RemoveWidgetCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "Dashboard.NoFirmSelected", "A firm must be selected to edit a dashboard."));
        }

        Dashboard? dashboard = await _dashboards.FindAsync(
            DashboardId.From(request.DashboardId), cancellationToken);

        if (dashboard is null || dashboard.FirmId != firmId)
        {
            return Result.Failure(Error.NotFound(
                "Dashboard.NotFound", "No such dashboard in the selected firm."));
        }

        Result removed = dashboard.RemoveWidget(
            DashboardWidgetId.From(request.WidgetId));

        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="GetDashboardsQuery"/>.</summary>
public sealed class GetDashboardsQueryHandler
    : IQueryHandler<GetDashboardsQuery, DashboardsResponse>
{
    private readonly IDashboardReader _reader;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetDashboardsQueryHandler"/> class.</summary>
    /// <param name="reader">The dashboard reader.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetDashboardsQueryHandler(
        IDashboardReader reader,
        ICurrentUser currentUser,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<DashboardsResponse>> Handle(
        GetDashboardsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<DashboardsResponse>(Error.Forbidden(
                "Dashboard.NoFirmSelected", "A firm must be selected to show a dashboard."));
        }

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<DashboardsResponse>(Error.Forbidden(
                "Dashboard.NotSignedIn", "A dashboard is shown to a signed-in user."));
        }

        IReadOnlyList<DashboardView> dashboards = await _reader.ReadForUserAsync(
            firmId, _currentUser.UserId, cancellationToken);

        return Result.Success(new DashboardsResponse(dashboards));
    }
}
