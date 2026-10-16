using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Menus;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>Users are created with roles, and a role decides both what they do and see.</summary>
/// <remarks>
/// The firm asked for user creation with a role, and for roles with menus. These walk it
/// through the real host: a role that may raise sales invoices and shows only two menus, a
/// user given it, and that user signing in to find exactly that.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class UserAdministrationEndpointTests
{
    private const string Users = "/api/v1/platform/users";
    private const string Roles = "/api/v1/platform/roles";

    private static readonly string[] CounterPermissions =
        ["sales:invoice:view", "sales:invoice:create", "sales:customer:view"];

    private static readonly string[] CounterMenus = ["transactions.sales"];

    private readonly ApiFactory _factory;

    public UserAdministrationEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_role_chooses_the_menus_its_users_see_and_what_they_may_do()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6];

        HttpResponseMessage createdRole = await admin.PostAsJsonAsync(Roles, new
        {
            Name = $"Counter {tag}",
            Description = "Sells over the counter",
            PermissionCodes = CounterPermissions,
            MenuCodes = CounterMenus,
        });

        createdRole.StatusCode.ShouldBe(HttpStatusCode.Created, await createdRole.Content.ReadAsStringAsync());
        RoleSummary role = (await createdRole.Content.ReadFromJsonAsync<RoleSummary>())!;
        role.RestrictsMenu.ShouldBeTrue();
        role.PermissionCodes.ShouldContain("sales:invoice:create");

        string password = $"Counter-{tag}-Pass!";

        HttpResponseMessage createdUser = await admin.PostAsJsonAsync(Users, new
        {
            UserName = $"counter{tag}",
            Email = $"counter{tag}@example.com",
            DisplayName = "Counter Staff",
            Password = password,
            RoleIds = new[] { role.RoleId },
        });

        createdUser.StatusCode.ShouldBe(HttpStatusCode.Created, await createdUser.Content.ReadAsStringAsync());
        UserSummary user = (await createdUser.Content.ReadFromJsonAsync<UserSummary>())!;
        user.MustChangePassword.ShouldBeTrue();
        user.RoleIds.ShouldBe([role.RoleId]);

        // The same user name again is refused by name rather than by an index.
        (await admin.PostAsJsonAsync(Users, new
        {
            UserName = $"counter{tag}",
            Email = $"other{tag}@example.com",
            DisplayName = "Twin",
            Password = password,
            RoleIds = new[] { role.RoleId },
        })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        HttpClient staff = await _factory.CreateAuthenticatedClientAsync($"counter{tag}", password);

        MenuResponse menu = (await staff.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;
        List<string> routes = [.. Flatten(menu.Items).Select(entry => entry.Code)];

        routes.ShouldContain("transactions.sales");
        // Permitted by sales:customer:view, but not one of the role's menus.
        routes.ShouldNotContain("sales.customers");
        routes.ShouldNotContain("transactions.purchase");

        // What the role may do is still its permissions.
        (await staff.GetAsync("/api/v1/sales/invoices")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await staff.GetAsync("/api/v1/purchase/invoices")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.GetAsync(Users)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Given the menus back, the role shows everything its permissions reach.
        HttpResponseMessage widened = await admin.PutAsJsonAsync($"{Roles}/{role.RoleId}", new
        {
            Name = $"Counter {tag}",
            Description = "Sells over the counter",
            PermissionCodes = role.PermissionCodes,
            MenuCodes = (string[]?)null,
        });

        widened.StatusCode.ShouldBe(HttpStatusCode.OK, await widened.Content.ReadAsStringAsync());

        MenuResponse wider = (await staff.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;
        Flatten(wider.Items).Select(entry => entry.Code).ShouldContain("sales.customers");

        // A role somebody holds is not deleted from under them.
        (await admin.DeleteAsync($"{Roles}/{role.RoleId}")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_taken_away()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();

        List<UserSummary> users = (await admin.GetFromJsonAsync<List<UserSummary>>(Users))!;
        UserSummary me = users.Single(user => user.UserName == ApiFactory.AdminUserName);

        HttpResponseMessage deactivated = await admin.PutAsJsonAsync($"{Users}/{me.UserId}", new
        {
            me.UserName,
            me.Email,
            me.DisplayName,
            me.MobileNumber,
            Password = (string?)null,
            me.RoleIds,
            IsActive = false,
        });

        deactivated.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.SelectMany(entry => new[] { entry }.Concat(Flatten(entry.Children)));
}
