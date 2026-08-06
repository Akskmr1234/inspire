using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Menus;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for editing the menu, end to end through the real host.
/// </summary>
/// <remarks>
/// The specification's claim is that an administrator can show, hide, reorder,
/// regroup, and extend the menu with no source-code change. These exercise exactly
/// that claim over HTTP, and then check the thing that makes it real: that the menu
/// the client renders actually reflects the edit.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class MenuAdministrationEndpointTests
{
    private const string AdminMenu = "/api/v1/admin/menu";

    private readonly ApiFactory _factory;

    public MenuAdministrationEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_administration_view_shows_system_entries_and_their_permissions()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuAdministrationResponse menu =
            (await client.GetFromJsonAsync<MenuAdministrationResponse>(AdminMenu))!;

        MenuAdministrationEntry reports =
            menu.Items.Single(item => item.Code == "accounts-reports");

        reports.IsSystem.ShouldBeTrue("the seeded menu is undeletable");
        reports.IsEnabled.ShouldBeTrue();
        reports.Children.ShouldContain(child =>
            child.RequiredPermission == "accounting:report:view");
    }

    [Fact]
    public async Task An_entry_can_be_added_renamed_moved_hidden_and_deleted()
    {
        // The whole administrative claim, in the order somebody would actually do it.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid id = await CreateAsync(client, "custom.link-one", "Custom link");

        // Renamed, and pointed somewhere.
        HttpResponseMessage updated = await client.PutAsJsonAsync(
            $"{AdminMenu}/{id}",
            new { Label = "Renamed link", Route = "/accounting/day-book" });
        updated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Moved beneath a seeded heading, which is the "regroup" part of the claim.
        MenuAdministrationResponse menu =
            (await client.GetFromJsonAsync<MenuAdministrationResponse>(AdminMenu))!;
        Guid reportsId = menu.Items.Single(item => item.Code == "accounts-reports").Id;

        HttpResponseMessage moved = await client.PostAsJsonAsync(
            $"{AdminMenu}/{id}/move", new { ParentId = reportsId, SortOrder = 5 });
        moved.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        MenuAdministrationResponse afterMove =
            (await client.GetFromJsonAsync<MenuAdministrationResponse>(AdminMenu))!;
        MenuAdministrationEntry reports =
            afterMove.Items.Single(item => item.Code == "accounts-reports");

        MenuAdministrationEntry entry =
            reports.Children.Single(child => child.Id == id);
        entry.Label.ShouldBe("Renamed link");
        entry.SortOrder.ShouldBe(5);
        entry.IsSystem.ShouldBeFalse("an entry an administrator added stays deletable");

        // Hidden, then gone.
        HttpResponseMessage hidden = await client.PostAsJsonAsync(
            $"{AdminMenu}/{id}/visibility", new { IsEnabled = false });
        hidden.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage deleted = await client.DeleteAsync($"{AdminMenu}/{id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Hiding_an_entry_takes_it_off_the_menu_the_client_renders()
    {
        // The edit is only real if the rendered menu changes. This is the one test
        // that ties the administration side to the side users actually see.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid id = await CreateAsync(
            client, "custom.hide-me", "Hide me", route: "/accounting/day-book");

        MenuResponse before = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;
        before.Items.ShouldContain(item => item.Code == "custom.hide-me");

        await client.PostAsJsonAsync($"{AdminMenu}/{id}/visibility", new { IsEnabled = false });

        MenuResponse after = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;
        after.Items.ShouldNotContain(item => item.Code == "custom.hide-me");

        await client.DeleteAsync($"{AdminMenu}/{id}");
    }

    [Fact]
    public async Task A_seeded_entry_cannot_be_deleted_but_can_be_relabelled()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuAdministrationResponse menu =
            (await client.GetFromJsonAsync<MenuAdministrationResponse>(AdminMenu))!;
        MenuAdministrationEntry cheques = menu.Items.Single(item => item.Code == "cheques");

        HttpResponseMessage deleted = await client.DeleteAsync($"{AdminMenu}/{cheques.Id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // But relabelling it is allowed, so a firm can call these what it calls them.
        HttpResponseMessage renamed = await client.PutAsJsonAsync(
            $"{AdminMenu}/{cheques.Id}",
            new
            {
                Label = "Instruments",
                cheques.Route,
                cheques.LabelArabic,
                cheques.Icon,
                cheques.RequiredPermission,
            });
        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Restored in full, every field of it. The endpoint rewrites the entry rather
        // than merging into it - which is what makes "clear the Arabic label"
        // expressible - so sending back only the label would silently drop the rest,
        // and the next test in this collection would be the one to discover it.
        HttpResponseMessage restored = await client.PutAsJsonAsync(
            $"{AdminMenu}/{cheques.Id}",
            new
            {
                cheques.Label,
                cheques.Route,
                cheques.LabelArabic,
                cheques.Icon,
                cheques.RequiredPermission,
            });
        restored.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_heading_that_still_holds_entries_cannot_be_deleted()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid parentId = await CreateAsync(client, "custom.parent", "Parent");
        Guid childId = await CreateAsync(
            client, "custom.child", "Child", parentId: parentId,
            route: "/accounting/day-book");

        HttpResponseMessage refused = await client.DeleteAsync($"{AdminMenu}/{parentId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Once the child is gone the heading may go too.
        await client.DeleteAsync($"{AdminMenu}/{childId}");
        (await client.DeleteAsync($"{AdminMenu}/{parentId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_entry_cannot_be_moved_beneath_its_own_child()
    {
        // Either direction of this would detach the subtree from the tree entirely.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid parentId = await CreateAsync(client, "custom.cycle-parent", "Cycle parent");
        Guid childId = await CreateAsync(
            client, "custom.cycle-child", "Cycle child", parentId: parentId,
            route: "/accounting/day-book");

        HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"{AdminMenu}/{parentId}/move", new { ParentId = childId, SortOrder = 0 });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await client.DeleteAsync($"{AdminMenu}/{childId}");
        await client.DeleteAsync($"{AdminMenu}/{parentId}");
    }

    [Fact]
    public async Task A_duplicate_code_is_refused_with_a_conflict()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid id = await CreateAsync(client, "custom.unique", "Unique");

        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            AdminMenu,
            new { Code = "custom.unique", Label = "Another", Module = "accounting" });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await client.DeleteAsync($"{AdminMenu}/{id}");
    }

    [Fact]
    public async Task Editing_the_menu_refuses_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync(AdminMenu)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Creates an entry and returns its identifier.</summary>
    private static async Task<Guid> CreateAsync(
        HttpClient client,
        string code,
        string label,
        Guid? parentId = null,
        string? route = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            AdminMenu,
            new
            {
                Code = code,
                Label = label,
                Module = "accounting",
                ParentId = parentId,
                Route = route,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
