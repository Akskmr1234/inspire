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
/// A closed registry rather than a query the widget carries. The specification asks
/// for custom SQL widgets eventually, and that is a decision with teeth: arbitrary SQL
/// arriving from a browser needs its own read-only role, a statement timeout, and
/// somebody to have vetted the views it can reach. None of that exists yet, and
/// shipping it without would hand every dashboard editor the whole database.
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

/// <summary>One computed metric.</summary>
/// <param name="MetricCode">The metric.</param>
/// <param name="Value">The headline figure.</param>
/// <param name="Count">How many things it counts, where that means something.</param>
/// <param name="Series">The points behind it, for a chart or a ranked list.</param>
/// <param name="IsPermitted">
/// Whether the caller may see this figure. When false, <see cref="Value"/> is zero and
/// the panel is drawn as withheld rather than as nil - a dashboard reporting nothing
/// owing and one refusing to say are different facts.
/// </param>
public sealed record DashboardMetric(
    string MetricCode,
    decimal Value,
    int Count,
    IReadOnlyList<MetricPoint> Series,
    bool IsPermitted);

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

    private readonly IDashboardReader _dashboards;
    private readonly IDashboardMetricReader _metrics;
    private readonly IPermissionChecker _permissions;
    private readonly IFirmRepository _firms;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="GetDashboardDataQueryHandler"/> class.</summary>
    /// <param name="dashboards">The dashboard reader.</param>
    /// <param name="metrics">The metric reader.</param>
    /// <param name="permissions">The permission checker.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    public GetDashboardDataQueryHandler(
        IDashboardReader dashboards,
        IDashboardMetricReader metrics,
        IPermissionChecker permissions,
        IFirmRepository firms,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IClock clock)
    {
        _dashboards = dashboards;
        _metrics = metrics;
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
                .Select(widget => widget.MetricCode)
                .Where(DashboardMetrics.IsKnown)
                .Distinct(StringComparer.Ordinal)
                .Where(code => IsPermitted(code, held, holdsEverything)),
        ];

        IReadOnlyDictionary<string, DashboardMetric> computed = requested.Count == 0
            ? new Dictionary<string, DashboardMetric>(StringComparer.Ordinal)
            : await _metrics.ReadAsync(firm.Value.Id, requested, asAt, cancellationToken);

        List<DashboardMetric> metrics = [];

        foreach (string code in dashboard.Widgets
            .Select(widget => widget.MetricCode)
            .Distinct(StringComparer.Ordinal))
        {
            // A metric the caller may not read comes back explicitly withheld rather
            // than missing. The panel then says so, instead of drawing a confident
            // zero that reads as "nothing is owed".
            if (!DashboardMetrics.IsKnown(code) || !IsPermitted(code, held, holdsEverything))
            {
                metrics.Add(new DashboardMetric(code, 0m, 0, [], IsPermitted: false));
                continue;
            }

            metrics.Add(computed.TryGetValue(code, out DashboardMetric? metric)
                ? metric
                : new DashboardMetric(code, 0m, 0, [], IsPermitted: true));
        }

        return Result.Success(new DashboardDataResponse(
            dashboard.Id, asAt, firm.Value.BaseCurrency.Code, metrics));
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
