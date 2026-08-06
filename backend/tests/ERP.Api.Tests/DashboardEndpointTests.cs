using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Dashboards;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for dashboards, end to end through the real host.
/// </summary>
/// <remarks>
/// A dashboard crosses more layers than almost anything else here — it is seeded with
/// role assignments, resolved through the roles the caller holds, and its figures are
/// computed by the report readers and then filtered by permission. Every one of those
/// steps can produce an empty dashboard without producing an error, which is exactly
/// why this asks for one over HTTP as a signed-in administrator.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DashboardEndpointTests
{
    private const string Dashboards = "/api/v1/dashboards";

    private readonly ApiFactory _factory;

    public DashboardEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_seeded_dashboard_reaches_the_administrator_through_a_role()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse response =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        // Empty here means the seeded role assignment never linked up - the one
        // failure that produces a perfectly healthy, perfectly blank screen.
        DashboardView dashboard = response.Dashboards
            .ShouldHaveSingleItem();

        dashboard.Code.ShouldBe("accounting-overview");
        dashboard.Widgets.ShouldNotBeEmpty();
        dashboard.NameArabic.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_dashboard_appears_once_even_though_several_roles_carry_it()
    {
        // The seeded dashboard is assigned to three roles. Somebody holding more than
        // one of them must still see it once; overlapping audiences are the ordinary
        // case rather than a mistake.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse response =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        response.Dashboards
            .Count(dashboard => dashboard.Code == "accounting-overview")
            .ShouldBe(1);
    }

    [Fact]
    public async Task Every_panel_names_a_metric_the_server_can_compute()
    {
        // A widget naming a metric the registry does not hold would render as a
        // permanently empty panel, and nothing else in the system would complain.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse response =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        foreach (DashboardWidgetView widget in response.Dashboards.SelectMany(d => d.Widgets))
        {
            DashboardMetrics.IsKnown(widget.MetricCode)
                .ShouldBeTrue($"'{widget.MetricCode}' is not in the metric registry");
        }
    }

    [Fact]
    public async Task The_figures_are_computed_for_every_panel()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse dashboards =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;
        DashboardView dashboard = dashboards.Dashboards[0];

        DashboardDataResponse data = (await client
            .GetFromJsonAsync<DashboardDataResponse>($"{Dashboards}/{dashboard.Id}/data"))!;

        data.DashboardId.ShouldBe(dashboard.Id);
        data.Currency.ShouldNotBeNullOrWhiteSpace();

        // One entry per distinct metric, and the administrator holds the wildcard so
        // none of them is withheld.
        data.Metrics.Select(metric => metric.MetricCode).ShouldBe(
            dashboard.Widgets.Select(widget => widget.MetricCode).Distinct(),
            ignoreOrder: true);

        data.Metrics.ShouldAllBe(metric => metric.IsPermitted);
    }

    [Fact]
    public async Task A_trend_covers_every_month_including_the_quiet_ones()
    {
        // A series that omitted empty months would draw the line straight through a
        // gap in trading and report it as steady activity.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse dashboards =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        DashboardDataResponse data = (await client.GetFromJsonAsync<DashboardDataResponse>(
            $"{Dashboards}/{dashboards.Dashboards[0].Id}/data"))!;

        DashboardMetric trend = data.Metrics
            .Single(metric => metric.MetricCode == DashboardMetrics.MonthlyPostings);

        trend.Series.Count.ShouldBe(12);
        trend.Series.Select(point => point.Label).Distinct().Count().ShouldBe(12);
    }

    [Fact]
    public async Task The_figures_can_be_stated_as_at_an_earlier_date()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardsResponse dashboards =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        DashboardDataResponse data = (await client.GetFromJsonAsync<DashboardDataResponse>(
            $"{Dashboards}/{dashboards.Dashboards[0].Id}/data?asAt=2026-06-30"))!;

        data.AsAt.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task Asking_for_a_dashboard_that_was_never_assigned_is_not_found()
    {
        // Read through the caller's own dashboards, so an identifier somebody guessed
        // does not become a way to compute figures they were never given.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"{Dashboards}/{Guid.CreateVersion7()}/data");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboards_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync(Dashboards)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
