using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERP.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ERP.Api.Tests;

/// <summary>
/// Boots the real API in memory against a real PostgreSQL container.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is stubbed. The application's own startup applies its migrations and
/// runs its own seeder, then requests go through the genuine pipeline -
/// authentication, tenant resolution, the authorisation policy provider, the
/// MediatR behaviours, and EF Core. These are the only tests that prove the
/// pieces are wired to each other rather than merely correct in isolation.
/// </para>
/// <para>
/// What they deliberately do <em>not</em> prove is row-level security. The API
/// connects as the container's bootstrap user, which is a superuser, and
/// PostgreSQL exempts superusers from RLS entirely. Proving isolation needs a
/// non-superuser role, and that is exactly what
/// <c>ERP.Infrastructure.Tests</c> does. Splitting it this way keeps each
/// suite honest about what it demonstrates.
/// </para>
/// <para>
/// Requires a running Docker daemon.
/// </para>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The seeded administrator's sign-in name.</summary>
    public const string AdminUserName = "admin";

    /// <summary>
    /// The seeded administrator's password.
    /// </summary>
    /// <remarks>
    /// A fixed value so tests can sign in. It reaches the application through
    /// configuration, exactly as a real deployment's would, and never leaves this
    /// container.
    /// </remarks>
    public const string AdminPassword = "integration-test-password";

    /// <summary>The company code supplied at sign-in.</summary>
    public const string TenantCode = "inspire";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("inspire_erp_api_tests")
        .WithUsername("erp_owner")
        .WithPassword("erp_owner")
        .Build();

    /// <summary>Starts the container.</summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Touching Services forces the host to build, which is what applies the
        // migrations and runs the seeder. Doing it here means a test that fails
        // does so on its own assertion rather than on start-up.
        _ = Services;
    }

    /// <summary>Stops the container and disposes the host.</summary>
    /// <returns>A task representing the operation.</returns>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    /// <summary>Creates a client with no credentials.</summary>
    /// <returns>An anonymous client.</returns>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>Signs in and returns a client carrying the resulting bearer token.</summary>
    /// <param name="userName">The sign-in name, defaulting to the seeded administrator.</param>
    /// <param name="password">The password, defaulting to the seeded administrator's.</param>
    /// <returns>An authenticated client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when sign-in fails.</exception>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string userName = AdminUserName,
        string password = AdminPassword)
    {
        HttpClient client = CreateClient();
        SignInResult signIn = await SignInAsync(client, userName, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", signIn.AccessToken);

        return client;
    }

    /// <summary>Signs in through the real endpoint.</summary>
    /// <param name="client">The client to use.</param>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="password">The password.</param>
    /// <returns>The tokens.</returns>
    /// <exception cref="InvalidOperationException">Thrown when sign-in fails.</exception>
    public static async Task<SignInResult> SignInAsync(
        HttpClient client,
        string userName = AdminUserName,
        string password = AdminPassword)
    {
        ArgumentNullException.ThrowIfNull(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { userName, password, tenantCode = TenantCode });

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"Sign-in failed with {(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<SignInResult>())!;
    }

    /// <summary>Runs work against the application's own service provider.</summary>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    /// <param name="work">The work to run.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// Used to arrange state the API exposes no endpoint for yet - creating a user
    /// with a deliberately limited role, for instance.
    /// </remarks>
    public async Task WithServiceAsync<TService>(Func<TService, Task> work)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(work);

        using IServiceScope scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _container.GetConnectionString(),

                // A key that exists only for this run. The real one lives in a
                // secret store; hard-coding that here would put it in git.
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-used-anywhere-else",
                ["Jwt:Issuer"] = "inspire-erp",
                ["Jwt:Audience"] = "inspire-erp-api",

                ["Erp:Seed:Enabled"] = "true",
                ["Erp:Seed:TenantCode"] = TenantCode,
                ["Erp:Seed:AdministratorUserName"] = AdminUserName,
                ["Erp:Seed:AdministratorPassword"] = AdminPassword,

                // The seeded administrator would otherwise be forced to change
                // password before doing anything, which every test would have to
                // work around before it could test what it is actually about.
                ["Erp:Seed:RequirePasswordChange"] = "false",
            }));

        return base.CreateHost(builder);
    }

    /// <summary>The payload returned by a successful sign-in.</summary>
    /// <param name="AccessToken">The bearer token.</param>
    /// <param name="RefreshToken">The refresh token.</param>
    /// <param name="ExpiresAtUtc">When the access token expires.</param>
    /// <param name="MustChangePassword">Whether a password change is outstanding.</param>
    /// <param name="DisplayName">The user's display name.</param>
    public sealed record SignInResult(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset ExpiresAtUtc,
        bool MustChangePassword,
        string DisplayName);
}

/// <summary>Shares one API host and container across the suite.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    /// <summary>The collection name.</summary>
    public const string Name = "api";
}
