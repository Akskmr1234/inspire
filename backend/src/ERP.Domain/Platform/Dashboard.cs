using ERP.Domain.Identity;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>Identifies a dashboard.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct DashboardId(Guid Value) : IStronglyTypedId<DashboardId>
{
    /// <inheritdoc />
    public static DashboardId From(Guid value) => new(value);

    /// <inheritdoc />
    public static DashboardId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a widget on a dashboard.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct DashboardWidgetId(Guid Value)
    : IStronglyTypedId<DashboardWidgetId>
{
    /// <inheritdoc />
    public static DashboardWidgetId From(Guid value) => new(value);

    /// <inheritdoc />
    public static DashboardWidgetId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>How a widget presents the figure behind it.</summary>
public enum WidgetKind
{
    /// <summary>A single headline figure, with an optional comparison.</summary>
    Kpi = 1,

    /// <summary>A figure per period, drawn as a small chart.</summary>
    Series = 2,

    /// <summary>A ranked list: the largest few of something, with their amounts.</summary>
    Breakdown = 3,
}

/// <summary>
/// One panel on a dashboard: a metric, and how to draw it.
/// </summary>
/// <remarks>
/// A widget names a metric rather than carrying a query. The specification does ask
/// for custom SQL widgets eventually, and that is a decision with real consequences -
/// arbitrary SQL from a browser needs a read-only role, a statement timeout, and a
/// view somebody has vetted, none of which exist yet. Naming a metric the server
/// already knows how to compute gets the dashboard working now without opening that
/// door prematurely.
/// </remarks>
public sealed class DashboardWidget : Entity<DashboardWidgetId>
{
    /// <summary>The longest a metric code may be.</summary>
    public const int MaximumMetricCodeLength = 100;

    /// <summary>The longest a title may be.</summary>
    public const int MaximumTitleLength = 100;

    internal DashboardWidget(
        DashboardWidgetId id,
        DashboardId dashboardId,
        TenantId tenantId,
        string metricCode,
        string title,
        WidgetKind kind,
        int sortOrder,
        int span)
        : base(id)
    {
        DashboardId = dashboardId;
        TenantId = tenantId;
        MetricCode = metricCode;
        Title = title;
        Kind = kind;
        SortOrder = sortOrder;
        Span = span;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private DashboardWidget()
    {
        MetricCode = string.Empty;
        Title = string.Empty;
    }

    /// <summary>Gets the dashboard this widget belongs to.</summary>
    public DashboardId DashboardId { get; private set; }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the metric the server computes for this widget.</summary>
    public string MetricCode { get; private set; }

    /// <summary>Gets the heading shown on the panel.</summary>
    public string Title { get; private set; }

    /// <summary>Gets the heading in Arabic.</summary>
    public string? TitleArabic { get; private set; }

    /// <summary>Gets how the figure is drawn.</summary>
    public WidgetKind Kind { get; private set; }

    /// <summary>Gets the position among the dashboard's widgets.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets how many grid columns the panel occupies.</summary>
    public int Span { get; private set; }

    /// <summary>Sets the Arabic heading.</summary>
    /// <param name="titleArabic">The Arabic heading, or null to clear it.</param>
    public void SetArabicTitle(string? titleArabic) =>
        TitleArabic = string.IsNullOrWhiteSpace(titleArabic) ? null : titleArabic.Trim();
}

/// <summary>A dashboard's assignment to a role.</summary>
/// <remarks>
/// A join rather than a column on either side, because the specification's own worked
/// example has ten dashboards with overlapping audiences - four to the accountant,
/// a partly-overlapping five to sales, all of them to the administrator. Anything that
/// gave a dashboard one role, or a role one dashboard, could not express that.
/// </remarks>
public sealed class DashboardRole : ITenantScoped
{
    internal DashboardRole(DashboardId dashboardId, RoleId roleId, TenantId tenantId)
    {
        DashboardId = dashboardId;
        RoleId = roleId;
        TenantId = tenantId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private DashboardRole()
    {
    }

    /// <summary>Gets the dashboard.</summary>
    public DashboardId DashboardId { get; private set; }

    /// <summary>Gets the role it is shown to.</summary>
    public RoleId RoleId { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }
}

/// <summary>
/// A dashboard: a named set of panels, shown to whichever roles it is assigned to.
/// </summary>
/// <remarks>
/// <para>
/// Assigned to roles rather than filtered by permission, which is the opposite of how
/// the navigation menu decides what to show. The difference is deliberate and follows
/// from what each is for. A menu entry leads to a screen, so the question "may I open
/// this" already has an answer and inventing a second one would let the two disagree.
/// A dashboard is a curated view - somebody chose these eight figures for this
/// audience - and that choice is editorial rather than a consequence of access.
/// </para>
/// <para>
/// The metrics behind the panels still answer to the reports' own permissions. A
/// dashboard assigned to somebody who cannot read accounting reports shows them a
/// heading and no figures, which is the correct outcome: the assignment says what
/// they are meant to look at, and authorisation still says what they may see.
/// </para>
/// </remarks>
public sealed class Dashboard : AggregateRoot<DashboardId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a dashboard code may be.</summary>
    public const int MaximumCodeLength = 100;

    /// <summary>The longest a dashboard name may be.</summary>
    public const int MaximumNameLength = 100;

    private readonly List<DashboardWidget> _widgets = [];
    private readonly List<DashboardRole> _roles = [];

    private Dashboard(
        DashboardId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        int sortOrder,
        bool isSystem)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        SortOrder = sortOrder;
        IsSystem = isSystem;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Dashboard()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the stable code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the dashboard's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets the position among the firm's dashboards.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets whether the dashboard was seeded and cannot be deleted.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>Gets the panels, in display order.</summary>
    public IReadOnlyCollection<DashboardWidget> Widgets => _widgets.AsReadOnly();

    /// <summary>Gets the roles this dashboard is shown to.</summary>
    public IReadOnlyCollection<DashboardRole> Roles => _roles.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a dashboard.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="name">The dashboard's name.</param>
    /// <param name="sortOrder">The position among the firm's dashboards.</param>
    /// <param name="isSystem">Whether the dashboard is seeded and undeletable.</param>
    /// <returns>The dashboard, or a validation failure.</returns>
    public static Result<Dashboard> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        int sortOrder = 0,
        bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Dashboard>(Error.Validation(
                "Dashboard.CodeRequired", "A dashboard code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<Dashboard>(Error.Validation(
                "Dashboard.CodeTooLong",
                $"A dashboard code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Dashboard>(Error.Validation(
                "Dashboard.NameRequired", "A dashboard name is required."));
        }

        return sortOrder < 0
            ? Result.Failure<Dashboard>(Error.Validation(
                "Dashboard.SortOrderNegative", "A sort order cannot be negative."))
            : Result.Success(new Dashboard(
                DashboardId.NewId(), tenantId, firmId,
                code.Trim().ToLowerInvariant(), name.Trim(), sortOrder, isSystem));
    }

    /// <summary>Sets the Arabic name.</summary>
    /// <param name="nameArabic">The Arabic name, or null to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Adds a panel.</summary>
    /// <param name="metricCode">The metric the server computes.</param>
    /// <param name="title">The heading shown on the panel.</param>
    /// <param name="kind">How the figure is drawn.</param>
    /// <param name="sortOrder">The position among the dashboard's panels.</param>
    /// <param name="span">How many grid columns it occupies.</param>
    /// <returns>The widget, or a validation failure.</returns>
    public Result<DashboardWidget> AddWidget(
        string metricCode,
        string title,
        WidgetKind kind,
        int sortOrder = 0,
        int span = 1)
    {
        if (string.IsNullOrWhiteSpace(metricCode))
        {
            return Result.Failure<DashboardWidget>(Error.Validation(
                "DashboardWidget.MetricRequired", "A widget must name a metric."));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<DashboardWidget>(Error.Validation(
                "DashboardWidget.TitleRequired", "A widget title is required."));
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<DashboardWidget>(Error.Validation(
                "DashboardWidget.UnknownKind", $"'{kind}' is not a recognised widget kind."));
        }

        // One to four columns. A panel wider than the grid would be drawn at the
        // grid's width anyway, so refusing it here keeps the stored layout honest
        // about what the screen will actually do.
        if (span is < 1 or > 4)
        {
            return Result.Failure<DashboardWidget>(Error.Validation(
                "DashboardWidget.SpanOutOfRange",
                "A widget must span between one and four columns."));
        }

        DashboardWidget widget = new(
            DashboardWidgetId.NewId(), Id, TenantId, metricCode.Trim().ToLowerInvariant(),
            title.Trim(), kind, sortOrder, span);

        _widgets.Add(widget);

        return Result.Success(widget);
    }

    /// <summary>Shows this dashboard to a role.</summary>
    /// <param name="roleId">The role.</param>
    /// <remarks>
    /// Assigning a role that already holds it does nothing, so a reseed adding an
    /// audience does not have to know which ones are already there.
    /// </remarks>
    public void AssignToRole(RoleId roleId)
    {
        if (!_roles.Exists(assignment => assignment.RoleId == roleId))
        {
            _roles.Add(new DashboardRole(Id, roleId, TenantId));
        }
    }

    /// <summary>Stops showing this dashboard to a role.</summary>
    /// <param name="roleId">The role.</param>
    public void RemoveFromRole(RoleId roleId) =>
        _roles.RemoveAll(assignment => assignment.RoleId == roleId);

    /// <summary>Checks whether the dashboard may be deleted.</summary>
    /// <returns>Success, or the reason it may not.</returns>
    public Result EnsureDeletable() => IsSystem
        ? Result.Failure(Error.BusinessRule(
            "Dashboard.SystemDashboard",
            $"'{Name}' is a system dashboard and cannot be deleted."))
        : Result.Success();
}
