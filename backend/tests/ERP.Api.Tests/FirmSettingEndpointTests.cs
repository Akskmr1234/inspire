using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Settings;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for the firm's shared settings, end to end through the real host.
/// </summary>
/// <remarks>
/// The screens kept these in each browser, so a firm set itself up once per counter.
/// What matters is the same contract the grid layouts have - the document comes back
/// exactly as it was written, and a block nobody has saved is an absence rather than
/// an error - with one difference: it belongs to the firm, not to whoever saved it.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FirmSettingEndpointTests
{
    private const string Settings = "/api/v1/settings";

    private readonly ApiFactory _factory;

    public FirmSettingEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_block_nobody_has_saved_answers_with_no_value()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        FirmSettingResponse setting =
            (await client.GetFromJsonAsync<FirmSettingResponse>($"{Settings}/never-saved"))!;

        setting.Key.ShouldBe("never-saved");
        setting.Value.ShouldBeNull();
    }

    [Fact]
    public async Task A_document_comes_back_exactly_as_it_was_written()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        const string value = """{"enableSerialNumbers":true,"defaultTaxRate":5,"note":"سلام"}""";

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"{Settings}/round-trip", new { Value = value });
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        FirmSettingResponse setting =
            (await client.GetFromJsonAsync<FirmSettingResponse>($"{Settings}/round-trip"))!;

        setting.Value.ShouldBe(value);
        setting.ModifiedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Saving_twice_replaces_rather_than_duplicates()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync($"{Settings}/upsert", new { Value = """{"a":1}""" });
        HttpResponseMessage second = await client.PutAsJsonAsync(
            $"{Settings}/upsert", new { Value = """{"a":2}""" });

        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        FirmSettingResponse setting =
            (await client.GetFromJsonAsync<FirmSettingResponse>($"{Settings}/upsert"))!;

        setting.Value.ShouldBe("""{"a":2}""");
    }

    [Fact]
    public async Task One_user_reads_what_another_saved()
    {
        // The point of holding these on the server: set once, read at every counter.
        // Two separate sign-ins stand in for two workstations.
        HttpClient first = await _factory.CreateAuthenticatedClientAsync();
        HttpClient second = await _factory.CreateAuthenticatedClientAsync();

        await first.PutAsJsonAsync($"{Settings}/shared", new { Value = """{"shared":true}""" });

        FirmSettingResponse setting =
            (await second.GetFromJsonAsync<FirmSettingResponse>($"{Settings}/shared"))!;

        setting.Value.ShouldBe("""{"shared":true}""");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("dot.ted")]
    public async Task A_name_that_would_need_escaping_is_refused(string key)
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{Settings}/{Uri.EscapeDataString(key)}", new { Value = "{}" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_anonymous_caller_reads_nothing()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync($"{Settings}/preferences");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
