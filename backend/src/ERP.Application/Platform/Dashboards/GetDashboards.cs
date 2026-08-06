using ERP.Application.Abstractions.Messaging;
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
/// <param name="MetricCode">The metric the server computes for it.</param>
/// <param name="Title">The heading.</param>
/// <param name="TitleArabic">The heading in Arabic.</param>
/// <param name="Kind">How the figure is drawn.</param>
/// <param name="Span">How many grid columns it occupies.</param>
public sealed record DashboardWidgetView(
    Guid Id,
    string MetricCode,
    string Title,
    string? TitleArabic,
    WidgetKind Kind,
    int Span);

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
