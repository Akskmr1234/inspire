using ERP.Application.Abstractions.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Interceptors;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Time;
using ERP.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
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
/// Requires a running Docker daemon.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("inspire_erp_tests")
        .WithUsername("erp_test")
        .WithPassword("erp_test")
        .Build();

    /// <summary>Gets the connection string for the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Starts the container and applies every migration.</summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Applying the real migrations, rather than EnsureCreated, is the point:
        // it proves the migrations themselves work against PostgreSQL. A schema
        // conjured from the model would hide any defect in them.
        await using ErpDbContext context = CreateContext(new AmbientTenantContext());
        await context.Database.MigrateAsync();
    }

    /// <summary>Stops and removes the container.</summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a context bound to the container, using the supplied tenant scope.
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
        ICurrentUser? currentUser = null)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);

        IClock clock = new SystemClock();

        DbContextOptions<ErpDbContext> options =
            new DbContextOptionsBuilder<ErpDbContext>()
                .UseNpgsql(ConnectionString)
                .AddInterceptors(
                    new AuditingInterceptor(
                        tenantContext, currentUser ?? new AmbientCurrentUser(), clock),
                    new TenantConnectionInterceptor(tenantContext))
                .Options;

        return new ErpDbContext(options, tenantContext);
    }

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
}

/// <summary>Shares one container across the whole integration-test suite.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "postgres";
}
