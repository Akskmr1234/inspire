using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Grids;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for saved grid layouts, end to end through the real host.
/// </summary>
/// <remarks>
/// The server treats a layout as opaque text, which is the whole point of the design -
/// the grid can grow grouping and column widths without a migration. These pin the two
/// consequences that matter: that whatever the client wrote comes back byte for byte,
/// and that a grid nobody has arranged answers with an absence rather than an error.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class GridLayoutEndpointTests
{
    private const string Layouts = "/api/v1/grid-layouts";

    private readonly ApiFactory _factory;

    public GridLayoutEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_grid_nobody_has_arranged_answers_with_no_layout()
    {
        // Not a 404: never having customised a grid is the ordinary case, and the
        // client asks on every mount before falling back to the grid's defaults.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        GridLayoutResponse layout =
            (await client.GetFromJsonAsync<GridLayoutResponse>($"{Layouts}/never-arranged"))!;

        layout.GridKey.ShouldBe("never-arranged");
        layout.State.ShouldBeNull();
    }

    [Fact]
    public async Task An_arrangement_comes_back_exactly_as_it_was_written()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        const string state =
            """{"order":["code","name"],"hidden":["groupCode"],"sortKey":"name","frozen":1}""";

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"{Layouts}/round-trip", new { State = state });
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        GridLayoutResponse layout =
            (await client.GetFromJsonAsync<GridLayoutResponse>($"{Layouts}/round-trip"))!;

        // Byte for byte. The server stores this document and hands it back unread, so
        // anything it did to the text on the way through would be a bug in a feature
        // whose entire contract is "give me back what I gave you".
        layout.State.ShouldBe(state);

        await client.DeleteAsync($"{Layouts}/round-trip");
    }

    [Fact]
    public async Task Saving_twice_replaces_rather_than_duplicates()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync($"{Layouts}/upsert", new { State = """{"frozen":0}""" });
        await client.PutAsJsonAsync($"{Layouts}/upsert", new { State = """{"frozen":2}""" });

        GridLayoutResponse layout =
            (await client.GetFromJsonAsync<GridLayoutResponse>($"{Layouts}/upsert"))!;

        layout.State.ShouldBe("""{"frozen":2}""");

        await client.DeleteAsync($"{Layouts}/upsert");
    }

    [Fact]
    public async Task A_grid_key_is_matched_however_it_was_capitalised()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync($"{Layouts}/Mixed-Case", new { State = """{"frozen":1}""" });

        GridLayoutResponse layout =
            (await client.GetFromJsonAsync<GridLayoutResponse>($"{Layouts}/mixed-case"))!;

        layout.State.ShouldBe("""{"frozen":1}""");

        await client.DeleteAsync($"{Layouts}/mixed-case");
    }

    [Fact]
    public async Task Resetting_forgets_the_arrangement()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync($"{Layouts}/forget-me", new { State = """{"frozen":1}""" });

        HttpResponseMessage reset = await client.DeleteAsync($"{Layouts}/forget-me");
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        GridLayoutResponse layout =
            (await client.GetFromJsonAsync<GridLayoutResponse>($"{Layouts}/forget-me"))!;

        layout.State.ShouldBeNull();
    }

    [Fact]
    public async Task Resetting_a_grid_that_was_never_arranged_succeeds()
    {
        // It is what the caller asked for, and already true.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage reset = await client.DeleteAsync($"{Layouts}/never-touched");

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_empty_arrangement_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{Layouts}/empty", new { State = string.Empty });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Layouts_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync($"{Layouts}/ledgers")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }
}
