using ERP.Application.Abstractions.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Interceptors;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Time;
using ERP.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// A real PostgreSQL instance in a throwaway container, shared by every test in
/// the collection.
/// </summary>
/// <remarks>
/// <para>
/// Row-level security, partial unique indexes, <c>xmin</c> concurrency tokens and
/// snake_case identifier folding are all PostgreSQL behaviours. None of them exist
/// in the EF Core in-memory provider, so a test using it would pass while the real
/// system was broken. These tests are slower and worth every second.
/// </para>
/// <para>
/// Two roles are used, and the distinction is essential rather than cosmetic:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// the <b>owner</b> (the container's bootstrap superuser) creates the schema and
/// runs migrations;
/// </description>
/// </item>
/// <item>
/// <description>
/// a separate <b>application role</b> runs everything else.
/// </description>
/// </item>
/// </list>
/// <para>
/// PostgreSQL lets superusers, and any role holding BYPASSRLS, ignore row-level
/// security completely - <c>FORCE ROW LEVEL SECURITY</c> does not bind them. A
/// test that connected as the bootstrap superuser would therefore see every
/// tenant's rows and report the isolation layer as broken; worse, an application
/// deployed against a superuser connection string would have no row-level security
/// at all while appearing to. Mirroring the production role split here is what
/// makes these tests meaningful.
/// </para>
/// <para>
/// Requires a running Docker daemon.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DatabaseName = "inspire_erp_tests";
    private const string AppRole = "erp_app";
    private const string AppPassword = "erp_app_test_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername("erp_owner")
        .WithPassword("erp_owner")
        .Build();

    /// <summary>
    /// Gets the owner connection string: superuser, used for migrations and for
    /// test set-up that must not be constrained by row-level security.
    /// </summary>
    public string OwnerConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Gets the application connection string: a non-superuser role, subject to
    /// row-level security exactly as the deployed application will be.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Starts the container, applies migrations, and provisions the app role.</summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Applying the real migrations, rather than EnsureCreated, is the point:
        // it proves the migrations themselves work against PostgreSQL. A schema
        // conjured from the model would hide any defect in them.
        await using (ErpDbContext migrator = CreateContextFor(
            OwnerConnectionString, new AmbientTenantContext()))
        {
            await migrator.Database.MigrateAsync();
        }

        await ProvisionApplicationRoleAsync();

        NpgsqlConnectionStringBuilder builder = new(OwnerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword,
        };

        ConnectionString = builder.ConnectionString;
    }

    /// <summary>Stops and removes the container.</summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a tenant context already scoped to the given tenant.</summary>
    /// <param name="tenantId">The tenant to act as.</param>
    /// <returns>A scoped tenant context.</returns>
    /// <remarks>
    /// The returned scope is intentionally never disposed. The context is a
    /// throwaway used for the lifetime of one test, and disposing the scope would
    /// only unwind an ambient value nothing else observes.
    /// </remarks>
    public static AmbientTenantContext ScopedTo(SharedKernel.Tenancy.TenantId tenantId)
    {
        AmbientTenantContext context = new();
        context.BeginScope(tenantId);
        return context;
    }

    /// <summary>
    /// Creates a context connected as the application role, using the supplied
    /// tenant scope.
    /// </summary>
    /// <param name="tenantContext">The tenant scope the context should observe.</param>
    /// <param name="currentUser">The acting user, defaulting to the system actor.</param>
    /// <returns>A new context.</returns>
    /// <remarks>
    /// Each call builds a fresh context with its own interceptors, which is how
    /// the isolation tests can hold two contexts for two different tenants at the
    /// same time and observe what each can actually see.
    /// </remarks>
    public ErpDbContext CreateContext(
        ITenantContext tenantContext,
        ICurrentUser? currentUser = null) =>
        CreateContextFor(ConnectionString, tenantContext, currentUser);

    private static ErpDbContext CreateContextFor(
        string connectionString,
        ITenantContext tenantContext,
        ICurrentUser? currentUser = null)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);

        IClock clock = new SystemClock();

        DbContextOptions<ErpDbContext> options =
            new DbContextOptionsBuilder<ErpDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new AuditingInterceptor(
                        tenantContext, currentUser ?? new AmbientCurrentUser(), clock),
                    new TenantConnectionInterceptor(tenantContext))
                .Options;

        return new ErpDbContext(options, tenantContext);
    }

    /// <summary>
    /// Creates the least-privileged role the application connects as.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// NOSUPERUSER and NOBYPASSRLS are the two attributes that matter. Either one
    /// missing silently disables every row-level-security policy in the database
    /// for this connection.
    /// </remarks>
    private async Task ProvisionApplicationRoleAsync()
    {
        await using NpgsqlConnection connection = new(OwnerConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(
            $"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}'
                NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;

            GRANT CONNECT ON DATABASE "{DatabaseName}" TO {AppRole};
            GRANT USAGE ON SCHEMA public TO {AppRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};

            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole};
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Shares one container across the whole integration-test suite.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "postgres";
}
