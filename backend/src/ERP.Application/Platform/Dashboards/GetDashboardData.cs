using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Dashboards;

/// <summary>
/// The metrics a dashboard widget may name, and what each requires to read.
/// </summary>
/// <remarks>
/// <para>
/// A closed registry of figures the server knows how to compute, reviewed like any
/// other code. Custom queries are supported too - see <see cref="CustomWidgetQuery"/> -
/// but they are a separate path with its own guards, and the two are deliberately not
/// the same mechanism: a metric is something somebody signed off, a custom query is
/// text somebody typed this morning.
/// </para>
/// <para>
/// Each metric names the permission its underlying report requires, so a dashboard
/// cannot become a way around authorisation. Being assigned a dashboard says what you
/// are meant to look at; the permission still says what you may see.
/// </para>
/// </remarks>
public static class DashboardMetrics
{
    /// <summary>What customers owe, as at today.</summary>
    public const string Receivables = "accounting.receivables";

    /// <summary>What the firm owes suppliers, as at today.</summary>
    public const string Payables = "accounting.payables";

    /// <summary>What is in the cash and bank accounts.</summary>
    public const string CashAndBank = "accounting.cash-and-bank";

    /// <summary>Post-dated cheques the firm is holding.</summary>
    public const string PostDatedReceivable = "accounting.pdc-receivable";

    /// <summary>Post-dated cheques the firm has written.</summary>
    public const string PostDatedPayable = "accounting.pdc-payable";

    /// <summary>Vouchers posted per month over the last year.</summary>
    public const string MonthlyPostings = "accounting.monthly-postings";

    /// <summary>The customers owing the most.</summary>
    public const string TopDebtors = "accounting.top-debtors";

    /// <summary>Every metric the server knows how to compute, with its permission.</summary>
    public static IReadOnlyDictionary<string, string> Permissions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Receivables] = "accounting:report:view",
            [Payables] = "accounting:report:view",
            [CashAndBank] = "accounting:report:view",
            [PostDatedReceivable] = "accounting:report:view",
            [PostDatedPayable] = "accounting:report:view",
            [MonthlyPostings] = "accounting:report:view",
            [TopDebtors] = "accounting:report:view",
        };

    /// <summary>Whether the server knows how to compute a metric.</summary>
    /// <param name="metricCode">The code named by a widget.</param>
    /// <returns><see langword="true"/> when it is in the registry.</returns>
    public static bool IsKnown(string metricCode) => Permissions.ContainsKey(metricCode);
}

/// <summary>Computes the figures behind one dashboard's panels.</summary>
/// <param name="DashboardId">The dashboard.</param>
/// <param name="AsAt">
/// The date the figures are stated as at. Defaults to today when omitted.
/// </param>
public sealed record GetDashboardDataQuery(Guid DashboardId, DateOnly? AsAt = null)
    : IQuery<DashboardDataResponse>;

/// <summary>One point of a series.</summary>
/// <param name="Label">What the point covers, for example <c>2026-03</c>.</param>
/// <param name="Value">Its value.</param>
public sealed record MetricPoint(string Label, decimal Value);

/// <summary>One computed panel.</summary>
/// <param name="WidgetId">
/// The panel this belongs to. Keyed by widget rather than by metric because a custom
/// panel has no metric to key on, and two panels may draw the same metric differently.
/// </param>
/// <param name="MetricCode">The metric, or null on a custom panel.</param>
/// <param name="Value">The headline figure.</param>
/// <param name="Count">How many things it counts, where that means something.</param>
/// <param name="Series">The points behind it, for a chart or a ranked list.</param>
/// <param name="IsPermitted">
/// Whether the caller may see this figure. When false, <see cref="Value"/> is zero and
/// the panel is drawn as withheld rather than as nil - a dashboard reporting nothing
/// owing and one refusing to say are different facts.
/// </param>
/// <param name="Error">
/// Why this panel could not be drawn, when it could not. A widget whose query no longer
/// runs reports its own failure and leaves the rest of the dashboard standing.
/// </param>
public sealed record DashboardMetric(
    Guid WidgetId,
    string? MetricCode,
    decimal Value,
    int Count,
    IReadOnlyList<MetricPoint> Series,
    bool IsPermitted,
    string? Error = null);

/// <summary>A dashboard's figures.</summary>
/// <param name="DashboardId">The dashboard.</param>
/// <param name="AsAt">The date the figures are stated as at.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Metrics">One entry per distinct metric on the dashboard.</param>
public sealed record DashboardDataResponse(
    Guid DashboardId,
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<DashboardMetric> Metrics);

/// <summary>Validates a <see cref="GetDashboardDataQuery"/>.</summary>
public sealed class GetDashboardDataQueryValidator
    : AbstractValidator<GetDashboardDataQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetDashboardDataQueryValidator"/> class.</summary>
    public GetDashboardDataQueryValidator() => RuleFor(q => q.DashboardId).NotEmpty();
}

/// <summary>Computes dashboard metrics.</summary>
public interface IDashboardMetricReader
{
    /// <summary>Computes a set of metrics for a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="metricCodes">The metrics to compute.</param>
    /// <param name="asAt">The date the figures are stated as at.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The computed figures, keyed by metric code.</returns>
    /// <remarks>
    /// Takes the whole set rather than one at a time. A dashboard of eight panels
    /// asking eight times would be eight round trips on every page load, and several
    /// of these metrics read the same tables.
    /// </remarks>
    Task<IReadOnlyDictionary<string, DashboardMetric>> ReadAsync(
        FirmId firmId,
        IReadOnlyCollection<string> metricCodes,
        DateOnly asAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetDashboardDataQuery"/>.</summary>
public sealed class GetDashboardDataQueryHandler
    : IQueryHandler<GetDashboardDataQuery, DashboardDataResponse>
{
    /// <summary>The permission code standing for every permission.</summary>
    private const string WildcardPermission = "*";

    /// <summary>
    /// The permission needed to read a custom panel, which is the same one needed to
    /// author it.
    /// </summary>
    /// <remarks>
    /// A statement somebody wrote can reach anything row-level security allows the
    /// tenant to see - which is far more than any single report exposes. Being assigned
    /// a dashboard that happens to carry one is not consent to that.
    /// </remarks>
    private const string CustomWidgetPermission = "reporting:dashboard:create";

    private readonly IDashboardReader _dashboards;
    private readonly IDashboardMetricReader _metrics;
    private readonly ICustomWidgetExecutor _custom;
    private readonly IPermissionChecker _permissions;
    private readonly IFirmRepository _firms;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="GetDashboardDataQueryHandler"/> class.</summary>
    /// <param name="dashboards">The dashboard reader.</param>
    /// <param name="metrics">The metric reader.</param>
    /// <param name="custom">The custom query executor.</param>
    /// <param name="permissions">The permission checker.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    public GetDashboardDataQueryHandler(
        IDashboardReader dashboards,
        IDashboardMetricReader metrics,
        ICustomWidgetExecutor custom,
        IPermissionChecker permissions,
        IFirmRepository firms,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IClock clock)
    {
        _dashboards = dashboards;
        _metrics = metrics;
        _custom = custom;
        _permissions = permissions;
        _firms = firms;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<DashboardDataResponse>> Handle(
        GetDashboardDataQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<DashboardDataResponse>(firm.Error);
        }

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<DashboardDataResponse>(Error.Forbidden(
                "Dashboard.NotSignedIn", "A dashboard is shown to a signed-in user."));
        }

        // Read through the user's own dashboards, so asking for one they were never
        // given reports not-found rather than computing it for them.
        IReadOnlyList<DashboardView> available = await _dashboards.ReadForUserAsync(
            firm.Value.Id, _currentUser.UserId, cancellationToken);

        DashboardView? dashboard = available
            .FirstOrDefault(candidate => candidate.Id == request.DashboardId);

        if (dashboard is null)
        {
            return Result.Failure<DashboardDataResponse>(Error.NotFound(
                "Dashboard.NotFound", "No such dashboard is available to you."));
        }

        DateOnly asAt = request.AsAt
            ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        IReadOnlySet<string> held = await _permissions.GetPermissionsAsync(
            _currentUser.UserId, cancellationToken);

        bool holdsEverything = held.Contains(WildcardPermission);

        // Distinct, because two panels may draw the same figure differently - a
        // headline and a trend of the same thing - and computing it twice would cost
        // twice as much to reach the same number.
        List<string> requested =
        [
            .. dashboard.Widgets
                .Where(widget => !widget.IsCustom && widget.MetricCode is not null)
                .Select(widget => widget.MetricCode!)
                .Where(DashboardMetrics.IsKnown)
                .Distinct(StringComparer.Ordinal)
                .Where(code => IsPermitted(code, held, holdsEverything)),
        ];

        IReadOnlyDictionary<string, DashboardMetric> computed = requested.Count == 0
            ? new Dictionary<string, DashboardMetric>(StringComparer.Ordinal)
            : await _metrics.ReadAsync(firm.Value.Id, requested, asAt, cancellationToken);

        IReadOnlyDictionary<Guid, string> queries = dashboard.Widgets.Any(w => w.IsCustom)
            ? await _dashboards.ReadWidgetQueriesAsync(dashboard.Id, cancellationToken)
            : new Dictionary<Guid, string>();

        List<DashboardMetric> metrics = [];

        foreach (DashboardWidgetView widget in dashboard.Widgets)
        {
            if (widget.IsCustom)
            {
                metrics.Add(await RunCustomAsync(
                    widget, queries, held, holdsEverything, cancellationToken));

                continue;
            }

            string? code = widget.MetricCode;

            // A metric the caller may not read comes back explicitly withheld rather
            // than missing. The panel then says so, instead of drawing a confident
            // zero that reads as "nothing is owed".
            if (code is null
                || !DashboardMetrics.IsKnown(code)
                || !IsPermitted(code, held, holdsEverything))
            {
                metrics.Add(new DashboardMetric(
                    widget.Id, code, 0m, 0, [], IsPermitted: false));

                continue;
            }

            metrics.Add(computed.TryGetValue(code, out DashboardMetric? metric)
                ? metric with { WidgetId = widget.Id }
                : new DashboardMetric(widget.Id, code, 0m, 0, [], IsPermitted: true));
        }

        return Result.Success(new DashboardDataResponse(
            dashboard.Id, asAt, firm.Value.BaseCurrency.Code, metrics));
    }

    /// <summary>Runs one custom panel's query.</summary>
    /// <param name="widget">The panel.</param>
    /// <param name="queries">The statements, keyed by widget.</param>
    /// <param name="held">The permissions the caller holds.</param>
    /// <param name="holdsEverything">Whether the caller holds the wildcard.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The panel's figures, or why it could not be drawn.</returns>
    /// <remarks>
    /// Reading a custom panel requires the same permission as authoring one. A query
    /// somebody wrote can reach anything row-level security allows the tenant to see,
    /// which is a good deal more than any single report exposes, so it is not something
    /// to hand to a reader who was merely assigned the dashboard.
    /// </remarks>
    private async Task<DashboardMetric> RunCustomAsync(
        DashboardWidgetView widget,
        IReadOnlyDictionary<Guid, string> queries,
        IReadOnlySet<string> held,
        bool holdsEverything,
        CancellationToken cancellationToken)
    {
        if (!holdsEverything && !held.Contains(CustomWidgetPermission))
        {
            return new DashboardMetric(widget.Id, null, 0m, 0, [], IsPermitted: false);
        }

        if (!queries.TryGetValue(widget.Id, out string? query))
        {
            return new DashboardMetric(
                widget.Id, null, 0m, 0, [], IsPermitted: true,
                Error: "This panel has no query.");
        }

        Result<IReadOnlyList<MetricPoint>> executed =
            await _custom.ExecuteAsync(query, cancellationToken);

        if (executed.IsFailure)
        {
            return new DashboardMetric(
                widget.Id, null, 0m, 0, [], IsPermitted: true,
                Error: executed.Error.Description);
        }

        IReadOnlyList<MetricPoint> points = executed.Value;

        // A headline panel takes the first row's value; a chart or a list takes them
        // all. The same query therefore serves either, and somebody changing how a
        // panel is drawn does not have to rewrite the SQL behind it.
        return new DashboardMetric(
            widget.Id,
            null,
            points.Count > 0 ? points[0].Value : 0m,
            points.Count,
            points,
            IsPermitted: true);
    }

    /// <summary>Whether the caller may read one metric.</summary>
    private static bool IsPermitted(
        string metricCode,
        IReadOnlySet<string> held,
        bool holdsEverything) =>
        holdsEverything
        || !DashboardMetrics.Permissions.TryGetValue(metricCode, out string? required)
        || held.Contains(required);
}
