using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Dashboards;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for the charts under the dashboard, end to end through the real host.
/// </summary>
/// <remarks>
/// Every chart can come back empty without anything having gone wrong - a firm with no
/// trading yet has nothing to draw - so these check the shape a chart is drawn on: every
/// day and every month present, the age-wise columns in order, and a chart the caller
/// may not read left out rather than drawn as a flat line.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DashboardChartEndpointTests
{
    private const string Charts = "/api/v1/dashboards/charts";

    private readonly ApiFactory _factory;

    public DashboardChartEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_chart_reaches_the_administrator()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardChartsResponse charts =
            (await client.GetFromJsonAsync<DashboardChartsResponse>(Charts))!;

        charts.Currency.ShouldNotBeNullOrWhiteSpace();
        charts.DailySales.ShouldNotBeNull();
        charts.PaymentModes.ShouldNotBeNull();
        charts.TopProducts.ShouldNotBeNull();
        charts.TopCategories.ShouldNotBeNull();
        charts.Receivables.ShouldNotBeNull();
        charts.Months.ShouldNotBeNull().ShouldAllBe(month =>
            month.Sales != null && month.Purchases != null && month.Profit != null);
    }

    [Fact]
    public async Task Every_day_and_every_month_is_drawn_the_quiet_ones_included()
    {
        // A chart that skipped the days and months without trading would close the gap
        // and show a quiet spell as steady business.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardChartsResponse charts = (await client.GetFromJsonAsync<DashboardChartsResponse>(
            $"{Charts}?asAt=2026-06-30"))!;

        charts.AsAt.ShouldBe(new DateOnly(2026, 6, 30));

        charts.DailySales.ShouldNotBeNull().Select(day => day.Date).ShouldBe(
            Enumerable.Range(0, 30).Select(offset => new DateOnly(2026, 6, 1).AddDays(offset)));

        charts.Months.ShouldNotBeNull().Select(month => month.Month).ShouldBe(
            [
                "2025-07", "2025-08", "2025-09", "2025-10", "2025-11", "2025-12",
                "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06",
            ]);
    }

    [Fact]
    public async Task What_customers_owe_is_aged_into_the_age_wise_reports_columns()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DashboardChartsResponse charts =
            (await client.GetFromJsonAsync<DashboardChartsResponse>(Charts))!;

        charts.Receivables.ShouldNotBeNull()
            .Select(age => (age.FromDays, age.ToDays))
            .ShouldBe([(0, 0), (1, 30), (31, 60), (61, 90), (91, (int?)null)]);
    }

    [Fact]
    public async Task A_chart_the_caller_may_not_read_is_left_out_rather_than_drawn_flat()
    {
        // A counter clerk who may open the sales bills sees what they add up to, but not
        // the books: no profit, no purchases, nothing about what customers owe.
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];

        HttpResponseMessage role = await admin.PostAsJsonAsync(
            "/api/v1/platform/roles",
            new SaveRoleRequest($"Counter {suffix}", null, ["sales:invoice:view"], null));
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        RoleSummary created = (await role.Content.ReadFromJsonAsync<RoleSummary>())!;

        const string password = "Counter-Clerk-Password-1!";
        string userName = $"counter{suffix}";

        HttpResponseMessage user = await admin.PostAsJsonAsync(
            "/api/v1/platform/users",
            new SaveUserRequest(
                userName, $"{userName}@example.test", "Counter clerk", null, password, [created.RoleId]));
        user.StatusCode.ShouldBe(HttpStatusCode.Created, await user.Content.ReadAsStringAsync());

        HttpClient clerk = await _factory.CreateAuthenticatedClientAsync(userName, password);

        DashboardChartsResponse charts =
            (await clerk.GetFromJsonAsync<DashboardChartsResponse>(Charts))!;

        charts.DailySales.ShouldNotBeNull().Count.ShouldBe(30);
        charts.PaymentModes.ShouldNotBeNull();
        charts.TopProducts.ShouldNotBeNull();
        charts.TopCategories.ShouldNotBeNull();
        charts.Months.ShouldBeNull();
        charts.Receivables.ShouldBeNull();
    }

    [Fact]
    public async Task The_charts_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync(Charts)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
