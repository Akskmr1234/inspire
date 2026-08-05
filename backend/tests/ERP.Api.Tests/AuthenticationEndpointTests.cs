using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ERP.Api.Tests;

/// <summary>
/// Tests the authentication endpoints through the real HTTP pipeline.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationEndpointTests
{
    private readonly ApiFactory _factory;

    public AuthenticationEndpointTests(ApiFactory factory) => _factory = factory;

    // ------------------------------------------------------------ sign-in

    [Fact]
    public async Task The_seeded_administrator_can_sign_in()
    {
        // Also proves the seeder ran: without it there is no account at all, and
        // every other test in this file would fail for the wrong reason.
        HttpClient client = _factory.CreateAnonymousClient();

        ApiFactory.SignInResult result = await ApiFactory.SignInAsync(client);

        result.AccessToken.ShouldNotBeNullOrWhiteSpace();
        result.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        result.ExpiresAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task An_access_token_is_short_lived()
    {
        // It cannot be revoked before it expires, so its lifetime is the window a
        // stolen one stays useful. Anything measured in hours would be wrong.
        HttpClient client = _factory.CreateAnonymousClient();

        ApiFactory.SignInResult result = await ApiFactory.SignInAsync(client);

        (result.ExpiresAtUtc - DateTimeOffset.UtcNow).ShouldBeLessThan(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData("admin", "wrong-password")]
    [InlineData("no-such-user", ApiFactory.AdminPassword)]
    [InlineData("no-such-user", "wrong-password")]
    public async Task Every_failed_sign_in_returns_the_same_response(
        string userName,
        string password)
    {
        // The whole point of the uniform failure. If an unknown user produced a
        // different status, body, or error code from a wrong password, an
        // unauthenticated caller could enumerate valid user names.
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { userName, password, tenantCode = ApiFactory.TenantCode });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("title").GetString()
            .ShouldBe("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task A_failed_sign_in_never_reveals_which_part_was_wrong()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { userName = "admin", password = "wrong", tenantCode = ApiFactory.TenantCode });

        string body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();

        // None of these words should ever reach an unauthenticated caller.
        body.ShouldNotContain("locked");
        body.ShouldNotContain("disabled");
        body.ShouldNotContain("does not exist");
        body.ShouldNotContain("not found");
    }

    // ------------------------------------------------------------ refresh

    [Fact]
    public async Task A_refresh_token_can_be_exchanged_for_a_new_pair()
    {
        HttpClient client = _factory.CreateAnonymousClient();
        ApiFactory.SignInResult first = await ApiFactory.SignInAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = first.RefreshToken, tenantCode = ApiFactory.TenantCode });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        ApiFactory.SignInResult second =
            (await response.Content.ReadFromJsonAsync<ApiFactory.SignInResult>())!;

        // Rotation: the replacement must be a different token, or a captured one
        // would stay valid indefinitely.
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Re_using_a_refresh_token_revokes_the_whole_session()
    {
        // The theft-detection path, end to end. Because every exchange rotates,
        // presenting a token twice means two parties hold it - and there is no way
        // to tell which is the legitimate user. Both are signed out.
        HttpClient client = _factory.CreateAnonymousClient();
        ApiFactory.SignInResult original = await ApiFactory.SignInAsync(client);

        HttpResponseMessage firstUse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = original.RefreshToken, tenantCode = ApiFactory.TenantCode });

        firstUse.StatusCode.ShouldBe(HttpStatusCode.OK);

        ApiFactory.SignInResult rotated =
            (await firstUse.Content.ReadFromJsonAsync<ApiFactory.SignInResult>())!;

        // Present the original a second time.
        HttpResponseMessage replay = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = original.RefreshToken, tenantCode = ApiFactory.TenantCode });

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the legitimate successor is now dead too - the entire family went.
        HttpResponseMessage successor = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken, tenantCode = ApiFactory.TenantCode });

        successor.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "reuse revokes the whole family, not just the replayed token");
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = "not-a-real-token", tenantCode = ApiFactory.TenantCode });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ sign-out

    [Fact]
    public async Task Signing_out_invalidates_the_refresh_token()
    {
        HttpClient client = _factory.CreateAnonymousClient();
        ApiFactory.SignInResult session = await ApiFactory.SignInAsync(client);

        HttpResponseMessage logout = await client.PostAsJsonAsync(
            "/api/v1/auth/logout", new { refreshToken = session.RefreshToken, tenantCode = ApiFactory.TenantCode });

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage afterLogout = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = session.RefreshToken, tenantCode = ApiFactory.TenantCode });

        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_with_an_unknown_token_still_succeeds()
    {
        // The desired state already holds. Reporting a failure would tell the
        // caller whether a given token had ever existed.
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/logout", new { refreshToken = "never-issued", tenantCode = ApiFactory.TenantCode });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
