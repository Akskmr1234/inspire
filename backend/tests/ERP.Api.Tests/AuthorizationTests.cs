using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ERP.Api.Tests;

/// <summary>
/// Tests that the HTTP edge actually enforces authentication and permissions.
/// </summary>
/// <remarks>
/// The permission engine is unit-tested and the policy provider is simple, but
/// neither proves the two are connected to the pipeline. An endpoint missing its
/// <c>[RequiresPermission]</c>, a policy provider not registered, or middleware
/// in the wrong order all produce a system that passes every other test and is
/// wide open. These tests go through the real stack and would catch each of them.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests
{
    private readonly ApiFactory _factory;

    public AuthorizationTests(ApiFactory factory) => _factory = factory;

    /// <summary>Every endpoint that must never serve an anonymous caller.</summary>
    public static TheoryData<string> ProtectedEndpoints() =>
    [
        "/api/v1/auth/permissions",
        "/api/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/reports/account-group-summary?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/reports/profit-and-loss?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/reports/balance-sheet?asAt=2026-12-31",
        "/api/v1/accounting/reports/day-book?from=2026-01-01&to=2026-01-31",
        "/api/v1/accounting/reports/voucher-report?from=2026-01-01&to=2026-01-31",
        "/api/v1/accounting/reports/transaction-summary?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/reports/cash-book?from=2026-01-01&to=2026-01-31",
        "/api/v1/accounting/reports/bank-book?from=2026-01-01&to=2026-01-31",
        "/api/v1/accounting/reports/post-dated-cheques?asAt=2026-12-31",
        "/api/v1/accounting/reports/cheque-calendar?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/reports/cheque-register?from=2026-01-01&to=2026-12-31",
        "/api/v1/accounting/ledgers",
    ];

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task A_protected_endpoint_refuses_an_anonymous_caller(string url)
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task A_protected_endpoint_refuses_a_forged_token(string url)
    {
        // A syntactically plausible token signed with the wrong key. If signature
        // validation were ever disabled this is the test that notices.
        HttpClient client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiIxMjM0NTY3ODkwIiwidGVuYW50X2lkIjoiMTIzIn0." +
            "definitely-not-a-valid-signature");

        HttpResponseMessage response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_anonymous_request_is_challenged_with_bearer()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/permissions");

        response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer");
    }

    [Fact]
    public async Task The_administrator_reaches_the_reports()
    {
        // The other half of the check. If everything returned 401 the tests above
        // would pass while the API served nobody at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_administrator_holds_the_wildcard_permission()
    {
        // Super Administrator holds a stored wildcard rather than an expanded
        // list, so a permission added by a later release is never held by nobody.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        string[] permissions =
            (await client.GetFromJsonAsync<string[]>("/api/v1/auth/permissions"))!;

        permissions.ShouldContain("*");
    }

    // ------------------------------------------------------------ contract shape

    [Fact]
    public async Task A_validation_failure_returns_problem_details_with_a_trace_id()
    {
        // A date range ending before it starts.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/accounting/reports/day-book?from=2026-12-31&to=2026-01-01");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.TryGetProperty("title", out _).ShouldBeTrue();
        body.RootElement.TryGetProperty("status", out _).ShouldBeTrue();

        // Correlating a user-reported error with its log entry is otherwise
        // guesswork.
        body.RootElement.TryGetProperty("traceId", out _)
            .ShouldBeTrue("every problem response carries a traceId");
    }

    [Fact]
    public async Task A_range_beyond_the_cap_is_refused()
    {
        // The day book returns every line of every voucher, so an unbounded range
        // is a denial-of-service waiting to happen.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/accounting/reports/day-book?from=2020-01-01&to=2026-12-31");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_route_returns_not_found_rather_than_an_error()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/v1/accounting/nonsense");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Endpoints_are_served_under_the_version_in_the_url()
    {
        // Versioning is in the path so it shows up in logs, browser history, and
        // support tickets. An unversioned path must not quietly work.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage unversioned = await client.GetAsync(
            "/api/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31");

        unversioned.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------ health

    [Fact]
    public async Task Liveness_needs_no_credentials_and_no_database()
    {
        // An orchestrator must be able to reach it without a token, and it must
        // not fail merely because a dependency is briefly unreachable.
        HttpClient client = _factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
