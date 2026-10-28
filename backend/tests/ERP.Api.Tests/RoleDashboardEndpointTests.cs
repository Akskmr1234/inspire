using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Dashboards;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>A role's dashboard: chosen from the catalogue, ordered, sized and seen by its users.</summary>
/// <remarks>
/// Arranged for a role of its own, so the administrator's dashboard - which other tests
/// read - is never the one rearranged.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class RoleDashboardEndpointTests
{
    private const string Dashboards = "/api/v1/dashboards";

    private static readonly string[] CashierPermissions =
    [
        "sales:invoice:view", "inventory:report:view", "accounting:report:view",
    ];

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="RoleDashboardEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public RoleDashboardEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_role_is_given_the_widgets_it_was_arranged_and_its_users_see_them_in_order()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();

        List<DashboardWidgetDefinition> catalogue =
            (await admin.GetFromJsonAsync<List<DashboardWidgetDefinition>>($"{Dashboards}/catalogue"))!;
        catalogue.Select(widget => widget.MetricCode).ShouldContain(DashboardMetrics.SalesBilledToday);
        catalogue.Select(widget => widget.MetricCode).ShouldContain(DashboardMetrics.RecentSales);
        catalogue.Select(widget => widget.MetricCode).ShouldContain(DashboardMetrics.LowStock);

        string tag = Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage role = await admin.PostAsJsonAsync("/api/v1/platform/roles", new
        {
            Name = $"Cashier {tag}",
            Description = "A dashboard of its own",
            PermissionCodes = CashierPermissions,
            MenuCodes = Array.Empty<string>(),
        });
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        Guid roleId = (await role.Content.ReadFromJsonAsync<RoleSummary>())!.RoleId;

        // Nothing arranged yet, and nothing shared with it.
        (await admin.GetFromJsonAsync<RoleDashboardView>($"{Dashboards}/roles/{roleId}"))!
            .Widgets.ShouldBeEmpty();

        HttpResponseMessage arranged = await admin.PutAsJsonAsync($"{Dashboards}/roles/{roleId}", new
        {
            Widgets = new object[]
            {
                new { MetricCode = DashboardMetrics.RecentSales, Span = 2 },
                new { MetricCode = DashboardMetrics.SalesBilledToday },
                new { MetricCode = DashboardMetrics.LowStock },
            },
        });
        arranged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await arranged.Content.ReadAsStringAsync());

        RoleDashboardView view = (await admin.GetFromJsonAsync<RoleDashboardView>($"{Dashboards}/roles/{roleId}"))!;
        view.Widgets.Select(widget => widget.MetricCode).ShouldBe(
            [DashboardMetrics.RecentSales, DashboardMetrics.SalesBilledToday, DashboardMetrics.LowStock]);
        view.Widgets[0].Span.ShouldBe(2);

        // Moved, and one taken off.
        (await admin.PutAsJsonAsync($"{Dashboards}/roles/{roleId}", new
        {
            Widgets = new object[]
            {
                new { MetricCode = DashboardMetrics.SalesBilledToday },
                new { MetricCode = DashboardMetrics.RecentSales, Span = 4 },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        view = (await admin.GetFromJsonAsync<RoleDashboardView>($"{Dashboards}/roles/{roleId}"))!;
        view.Widgets.Select(widget => widget.MetricCode).ShouldBe(
            [DashboardMetrics.SalesBilledToday, DashboardMetrics.RecentSales]);
        view.Widgets[1].Span.ShouldBe(4);

        // A widget the catalogue does not have is refused, and the arrangement stands.
        (await admin.PutAsJsonAsync($"{Dashboards}/roles/{roleId}", new
        {
            Widgets = new object[] { new { MetricCode = "no.such-widget" } },
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetFromJsonAsync<RoleDashboardView>($"{Dashboards}/roles/{roleId}"))!
            .Widgets.Count.ShouldBe(2);

        // A user with the role sees it, and its figures.
        string userName = $"cashier{tag}";
        string password = $"Cashier-{tag}-Password!";
        HttpResponseMessage user = await admin.PostAsJsonAsync("/api/v1/platform/users", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            DisplayName = "Cashier",
            Password = password,
            RoleIds = new[] { roleId },
        });
        user.StatusCode.ShouldBe(HttpStatusCode.Created, await user.Content.ReadAsStringAsync());

        HttpClient cashier = await _factory.CreateAuthenticatedClientAsync(userName, password);
        DashboardView seen = (await cashier.GetFromJsonAsync<DashboardsResponse>(Dashboards))!
            .Dashboards.ShouldHaveSingleItem();
        seen.Code.ShouldBe($"role-{roleId:N}");
        seen.Widgets.Select(widget => widget.MetricCode).ShouldBe(
            [DashboardMetrics.SalesBilledToday, DashboardMetrics.RecentSales]);

        DashboardDataResponse data =
            (await cashier.GetFromJsonAsync<DashboardDataResponse>($"{Dashboards}/{seen.Id}/data"))!;
        data.Metrics.ShouldAllBe(metric => metric.IsPermitted && metric.Error == null);
        data.Metrics.Single(metric => metric.MetricCode == DashboardMetrics.RecentSales)
            .Series.Count.ShouldBeLessThanOrEqualTo(20);

        // The administrator's dashboard is untouched.
        (await admin.GetFromJsonAsync<DashboardsResponse>(Dashboards))!
            .Dashboards.ShouldHaveSingleItem().Code.ShouldBe("accounting-overview");
    }
}
