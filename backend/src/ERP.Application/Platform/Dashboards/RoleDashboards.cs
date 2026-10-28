using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Identity;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Dashboards;

/// <summary>Lists every widget a dashboard can be made of.</summary>
public sealed record GetDashboardCatalogueQuery : IQuery<IReadOnlyList<DashboardWidgetDefinition>>;

/// <summary>Reads the dashboard a role sees.</summary>
/// <param name="RoleId">The role.</param>
public sealed record GetRoleDashboardQuery(Guid RoleId) : IQuery<RoleDashboardView>;

/// <summary>The dashboard a role sees.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="RoleName">Its name.</param>
/// <param name="Widgets">Its widgets, in order; none when the role has no dashboard yet.</param>
public sealed record RoleDashboardView(Guid RoleId, string RoleName, IReadOnlyList<DashboardWidgetView> Widgets);

/// <summary>One widget of a role's dashboard, as it is to be arranged.</summary>
/// <param name="MetricCode">A widget from the catalogue, by its metric.</param>
/// <param name="WidgetId">A custom widget the role's dashboard already has, kept as it is.</param>
/// <param name="Span">How many columns it takes, of four; the catalogue's own when omitted.</param>
public sealed record RoleDashboardItem(string? MetricCode = null, Guid? WidgetId = null, int? Span = null);

/// <summary>Sets which widgets a role's dashboard shows, and in what order.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Widgets">The widgets, first to last; leaving one out removes it.</param>
/// <remarks>
/// Each role is given a dashboard of its own the first time it is arranged, starting
/// from whatever it saw before, and stops seeing the dashboard it shared. Every user with
/// the role sees the arrangement.
/// </remarks>
public sealed record SetRoleDashboardCommand(Guid RoleId, IReadOnlyList<RoleDashboardItem> Widgets) : ICommand;

/// <summary>Reads and writes the dashboards roles see.</summary>
public interface IRoleDashboardRepository
{
    /// <summary>The name of a role, or null when there is no such role.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The name.</returns>
    Task<string?> FindRoleNameAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every dashboard of the firm assigned to a role, with their widgets and roles, the
    /// one the role sees first.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The dashboards, in the order the role's users see them.</returns>
    Task<IReadOnlyList<Dashboard>> ListForRoleAsync(
        FirmId firmId,
        RoleId roleId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new dashboard.</summary>
    /// <param name="dashboard">The dashboard.</param>
    void Add(Dashboard dashboard);
}

/// <summary>What a role's own dashboard is called by code.</summary>
public static class RoleDashboardCodes
{
    /// <summary>The code of the dashboard a role was given of its own.</summary>
    /// <param name="roleId">The role.</param>
    /// <returns>The code.</returns>
    public static string For(RoleId roleId) => $"role-{roleId.Value:N}";
}

/// <summary>Handles the role dashboard queries and command.</summary>
public sealed class RoleDashboardHandlers
    : IQueryHandler<GetDashboardCatalogueQuery, IReadOnlyList<DashboardWidgetDefinition>>,
      IQueryHandler<GetRoleDashboardQuery, RoleDashboardView>,
      ICommandHandler<SetRoleDashboardCommand>
{
    private readonly IRoleDashboardRepository _dashboards;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="RoleDashboardHandlers"/> class.</summary>
    /// <param name="dashboards">The dashboards.</param>
    /// <param name="tenantContext">The caller's firm.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public RoleDashboardHandlers(
        IRoleDashboardRepository dashboards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _dashboards = dashboards;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<DashboardWidgetDefinition>>> Handle(
        GetDashboardCatalogueQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(DashboardMetrics.Catalogue));

    /// <inheritdoc />
    public async Task<Result<RoleDashboardView>> Handle(
        GetRoleDashboardQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<RoleDashboardView>(NoFirm());
        }

        RoleId roleId = RoleId.From(request.RoleId);
        string? name = await _dashboards.FindRoleNameAsync(roleId, cancellationToken);

        if (name is null)
        {
            return Result.Failure<RoleDashboardView>(NoRole());
        }

        IReadOnlyList<Dashboard> dashboards = await _dashboards.ListForRoleAsync(firmId, roleId, cancellationToken);

        return Result.Success(new RoleDashboardView(
            request.RoleId,
            name,
            dashboards.Count == 0 ? [] : Describe(dashboards[0])));
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetRoleDashboardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(NoFirm());
        }

        RoleId roleId = RoleId.From(request.RoleId);
        string? name = await _dashboards.FindRoleNameAsync(roleId, cancellationToken);

        if (name is null)
        {
            return Result.Failure(NoRole());
        }

        IReadOnlyList<Dashboard> assigned = await _dashboards.ListForRoleAsync(firmId, roleId, cancellationToken);
        string ownCode = RoleDashboardCodes.For(roleId);

        // The custom widgets it can keep are those of the dashboard it sees now.
        Dictionary<DashboardWidgetId, DashboardWidget> custom = assigned.Count == 0
            ? []
            : assigned[0].Widgets
                .Where(widget => widget.Query is not null)
                .ToDictionary(widget => widget.Id);

        Dashboard? own = assigned.FirstOrDefault(dashboard => dashboard.Code == ownCode);

        if (own is null)
        {
            Result<Dashboard> created = Dashboard.Create(_tenantContext.TenantId, firmId, ownCode, name, sortOrder: 0);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            own = created.Value;
            own.AssignToRole(roleId);
            _dashboards.Add(own);

            // Its own from now on: the dashboard it shared stays for the other roles.
            foreach (Dashboard shared in assigned)
            {
                shared.RemoveFromRole(roleId);
            }
        }

        // Laid out afresh, in the order given.
        foreach (DashboardWidgetId existing in own.Widgets.Select(widget => widget.Id).ToList())
        {
            own.RemoveWidget(existing);
        }

        for (int index = 0; index < request.Widgets.Count; index++)
        {
            Result placed = Place(own, request.Widgets[index], (index + 1) * 10, custom);

            if (placed.IsFailure)
            {
                return placed;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result Place(
        Dashboard dashboard,
        RoleDashboardItem item,
        int sortOrder,
        Dictionary<DashboardWidgetId, DashboardWidget> custom)
    {
        if (item.WidgetId is { } widgetId)
        {
            if (!custom.TryGetValue(DashboardWidgetId.From(widgetId), out DashboardWidget? kept))
            {
                return Result.Failure(Error.NotFound(
                    "Dashboard.WidgetNotFound", "That custom widget is not on this role's dashboard."));
            }

            Result<DashboardWidget> copied = dashboard.AddCustomWidget(
                kept.Query!, kept.Title, kept.Kind, sortOrder, item.Span ?? kept.Span);

            if (copied.IsSuccess)
            {
                copied.Value.SetArabicTitle(kept.TitleArabic);
            }

            return copied.IsSuccess ? Result.Success() : Result.Failure(copied.Error);
        }

        DashboardWidgetDefinition? definition = DashboardMetrics.Catalogue.FirstOrDefault(entry =>
            string.Equals(entry.MetricCode, item.MetricCode, StringComparison.Ordinal));

        if (definition is null)
        {
            return Result.Failure(Error.Validation(
                "Dashboard.UnknownWidget", $"'{item.MetricCode}' is not a widget a dashboard can show."));
        }

        Result<DashboardWidget> added = dashboard.AddWidget(
            definition.MetricCode, definition.Title, definition.Kind, sortOrder, item.Span ?? definition.Span);

        if (added.IsSuccess)
        {
            added.Value.SetArabicTitle(definition.TitleArabic);
        }

        return added.IsSuccess ? Result.Success() : Result.Failure(added.Error);
    }

    private static List<DashboardWidgetView> Describe(Dashboard dashboard) =>
    [
        .. dashboard.Widgets
            .OrderBy(widget => widget.SortOrder)
            .Select(widget => new DashboardWidgetView(
                widget.Id.Value,
                widget.MetricCode,
                widget.Title,
                widget.TitleArabic,
                widget.Kind,
                widget.Span,
                widget.Query is not null)),
    ];

    private static Error NoFirm() =>
        Error.Forbidden("Dashboard.NoFirmSelected", "A firm must be selected to arrange its dashboards.");

    private static Error NoRole() => Error.NotFound("Dashboard.RoleNotFound", "No such role.");
}

/// <summary>Validates <see cref="SetRoleDashboardCommand"/>.</summary>
public sealed class SetRoleDashboardCommandValidator : AbstractValidator<SetRoleDashboardCommand>
{
    /// <summary>The most widgets one dashboard holds.</summary>
    public const int MaximumWidgets = 40;

    /// <summary>Initialises a new instance of the <see cref="SetRoleDashboardCommandValidator"/> class.</summary>
    public SetRoleDashboardCommandValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.Widgets)
            .NotNull()
            .Must(widgets => widgets.Count <= MaximumWidgets)
            .WithMessage($"A dashboard holds at most {MaximumWidgets} widgets.");
        RuleForEach(command => command.Widgets)
            .Must(item => (item.MetricCode is null) != (item.WidgetId is null))
            .WithMessage("Each widget names either a metric or a custom widget, not both.")
            .Must(item => item.Span is null or (>= 1 and <= 4))
            .WithMessage("A widget spans one to four columns.");
    }
}
