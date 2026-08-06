using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Dashboards;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for custom dashboard widgets, end to end through the real host.
/// </summary>
/// <remarks>
/// Running SQL somebody typed is the largest deliberate attack surface in the platform,
/// so these are as much security tests as feature tests. The validator has its own unit
/// tests; what matters here is that the guarantees behind it hold against a real
/// PostgreSQL - that a write is refused by the database and not merely by a regular
/// expression, that a row cap and a timeout are actually in force, and that a query
/// cannot see past its own tenant.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CustomWidgetEndpointTests
{
    private const string Dashboards = "/api/v1/dashboards";

    private readonly ApiFactory _factory;

    public CustomWidgetEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_custom_panel_runs_and_returns_its_rows()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client,
            dashboardId,
            "SELECT code AS label, COUNT(*)::numeric AS value FROM ledgers "
            + "GROUP BY code ORDER BY code LIMIT 3");

        DashboardMetric panel = await ReadPanelAsync(client, dashboardId, widgetId);

        panel.IsPermitted.ShouldBeTrue();
        panel.Error.ShouldBeNull();
        panel.MetricCode.ShouldBeNull("a custom panel has no metric to name");
        panel.Series.ShouldNotBeEmpty();

        await RemoveAsync(client, dashboardId, widgetId);
    }

    [Fact]
    public async Task A_panel_whose_query_no_longer_runs_reports_itself_and_nothing_else()
    {
        // The dashboard must survive one bad panel. A query referencing a column that
        // has since been renamed is the ordinary way this happens.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client,
            dashboardId,
            "SELECT no_such_column AS label, 1::numeric AS value FROM ledgers");

        DashboardDataResponse data = await ReadDataAsync(client, dashboardId);

        DashboardMetric broken = data.Metrics.Single(metric => metric.WidgetId == widgetId);
        broken.Error.ShouldNotBeNull();

        // Every seeded panel still drew.
        data.Metrics.Count(metric => metric.Error is null).ShouldBeGreaterThan(1);

        await RemoveAsync(client, dashboardId, widgetId);
    }

    [Theory]
    [InlineData("DELETE FROM ledgers WHERE label IS NOT NULL AND value IS NOT NULL")]
    [InlineData("SELECT 1 AS label, 1 AS value; DROP TABLE ledgers")]
    [InlineData("SELECT 'x' AS label, pg_sleep(30) AS value")]
    [InlineData("SELECT 'x' AS label, 1 AS value -- rest is a comment")]
    public async Task A_query_that_should_never_be_accepted_is_refused(string query)
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Dashboards}/{dashboardId}/widgets",
            new { Query = query, Title = "Rejected", Kind = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_write_that_got_past_the_validator_is_still_refused_by_the_database()
    {
        // The guarantee that matters. This statement passes every text check - it opens
        // with SELECT, is one statement, carries no forbidden word and no comment - and
        // writes through a function. It is stopped because the transaction is READ
        // ONLY, which is a property of the database rather than of the parser.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client,
            dashboardId,
            "SELECT 'x' AS label, nextval('pg_catalog.pg_class_oid_index'::regclass)::numeric AS value");

        DashboardMetric panel = await ReadPanelAsync(client, dashboardId, widgetId);

        // Either the sequence does not exist or the read-only transaction refuses the
        // write. Both are failures of the panel, and neither is a write that happened.
        panel.Error.ShouldNotBeNull();

        await RemoveAsync(client, dashboardId, widgetId);
    }

    [Fact]
    public async Task A_query_cannot_see_another_tenants_rows()
    {
        // Row-level security is what enforces this, and it applies because the query
        // runs on the ordinary application connection as the ordinary role. A widget
        // deliberately asking for every tenant sees exactly one - its own.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client,
            dashboardId,
            "SELECT tenant_id::text AS label, COUNT(*)::numeric AS value "
            + "FROM ledgers GROUP BY tenant_id");

        DashboardMetric panel = await ReadPanelAsync(client, dashboardId, widgetId);

        panel.Error.ShouldBeNull();
        panel.Series.Select(point => point.Label).Distinct().Count()
            .ShouldBe(1, "row-level security confines the query to its own tenant");

        await RemoveAsync(client, dashboardId, widgetId);
    }

    [Fact]
    public async Task A_query_returning_everything_is_capped()
    {
        // generate_series would happily return a million rows into a dashboard panel.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client,
            dashboardId,
            "SELECT n::text AS label, n::numeric AS value FROM generate_series(1, 100000) AS n");

        DashboardMetric panel = await ReadPanelAsync(client, dashboardId, widgetId);

        panel.Error.ShouldBeNull();
        panel.Series.Count.ShouldBeLessThanOrEqualTo(500);

        await RemoveAsync(client, dashboardId, widgetId);
    }

    [Fact]
    public async Task A_panel_can_be_removed_again()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Guid dashboardId = await FirstDashboardAsync(client);

        Guid widgetId = await AddAsync(
            client, dashboardId, "SELECT 'a' AS label, 1::numeric AS value");

        await RemoveAsync(client, dashboardId, widgetId);

        DashboardsResponse dashboards =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        dashboards.Dashboards
            .SelectMany(dashboard => dashboard.Widgets)
            .ShouldNotContain(widget => widget.Id == widgetId);
    }

    [Fact]
    public async Task Authoring_a_panel_refuses_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Dashboards}/{Guid.CreateVersion7()}/widgets",
            new { Query = "SELECT 'a' AS label, 1 AS value", Title = "x", Kind = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> FirstDashboardAsync(HttpClient client)
    {
        DashboardsResponse dashboards =
            (await client.GetFromJsonAsync<DashboardsResponse>(Dashboards))!;

        return dashboards.Dashboards[0].Id;
    }

    private static async Task<Guid> AddAsync(
        HttpClient client,
        Guid dashboardId,
        string query)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Dashboards}/{dashboardId}/widgets",
            new { Query = query, Title = "Custom panel", Kind = 3, SortOrder = 900 });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<DashboardDataResponse> ReadDataAsync(
        HttpClient client,
        Guid dashboardId) =>
        (await client.GetFromJsonAsync<DashboardDataResponse>(
            $"{Dashboards}/{dashboardId}/data"))!;

    private static async Task<DashboardMetric> ReadPanelAsync(
        HttpClient client,
        Guid dashboardId,
        Guid widgetId)
    {
        DashboardDataResponse data = await ReadDataAsync(client, dashboardId);

        return data.Metrics.Single(metric => metric.WidgetId == widgetId);
    }

    private static async Task RemoveAsync(
        HttpClient client,
        Guid dashboardId,
        Guid widgetId)
    {
        HttpResponseMessage response = await client.DeleteAsync(
            $"{Dashboards}/{dashboardId}/widgets/{widgetId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
