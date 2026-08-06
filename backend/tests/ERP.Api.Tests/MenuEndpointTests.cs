using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Menus;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for the menu endpoint, end to end through the real host.
/// </summary>
/// <remarks>
/// The menu crosses more layers than most reads - it is seeded into the database,
/// fetched by a reader, assembled into a tree, and filtered against the caller's
/// permissions - and every one of those steps is capable of producing an empty menu
/// without producing an error. Booting the application against a real database and
/// asking for the menu as a signed-in administrator is the only test that catches all
/// of them at once.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class MenuEndpointTests
{
    private readonly ApiFactory _factory;

    public MenuEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_menu_is_seeded_and_served_to_a_signed_in_user()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuResponse menu = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;

        // Seeding created a tree, so an empty menu here means one of the steps
        // between the catalogue and the response quietly produced nothing.
        menu.Items.ShouldNotBeEmpty();
        menu.Items.ShouldContain(item => item.Code == "accounts-reports");
    }

    [Fact]
    public async Task Headings_carry_the_screens_beneath_them()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuResponse menu = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;

        MenuEntry reports = menu.Items.Single(item => item.Code == "accounts-reports");

        reports.Route.ShouldBeNull("a heading navigates nowhere itself");
        reports.Children.ShouldContain(child => child.Route == "/accounting/trial-balance");
    }

    [Fact]
    public async Task The_administrators_wildcard_opens_every_entry()
    {
        // Super Administrator holds "*" rather than several hundred enumerated codes.
        // A filter testing set membership alone would hand this account an empty menu.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuResponse menu = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;

        menu.Items.SelectMany(item => item.Children)
            .ShouldContain(child => child.Route == "/accounting/cheque-register");
    }

    [Fact]
    public async Task Entries_carry_an_Arabic_label_for_the_right_to_left_interface()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        MenuResponse menu = (await client.GetFromJsonAsync<MenuResponse>("/api/v1/menu"))!;

        menu.Items.ShouldAllBe(item => item.LabelArabic != null);
    }

    [Fact]
    public async Task The_menu_refuses_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/menu");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
