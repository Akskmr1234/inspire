using ERP.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>Applies pending migrations and runs the seeder at startup.</summary>
/// <remarks>
/// Log messages are declared with <c>[LoggerMessage]</c> so the source generator
/// emits strongly-typed, allocation-free implementations. The alternative -
/// <c>logger.LogInformation("...", count, flag)</c> - boxes every value-type
/// argument into an <c>object[]</c> whether or not the message is ever emitted.
/// </remarks>
public static partial class DatabaseInitializer
{
    /// <summary>How long to wait for the database at startup, in seconds.</summary>
    /// <remarks>
    /// Chosen to sit inside the startup budget a deployment platform allows - commonly
    /// about a minute - with time to spare for the migrations that follow. Waiting
    /// longer than the platform is willing to wait achieves nothing: it rolls the
    /// deployment back regardless, and a container still waiting is a container that
    /// never reported why.
    /// </remarks>
    private const int DefaultDatabaseWaitSeconds = 30;

    /// <summary>How long to pause between connection attempts.</summary>
    private const int RetryDelayMilliseconds = 1_000;

    /// <summary>
    /// Brings the database up to date and seeds it, if seeding is enabled.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="applyMigrations">Whether to apply pending migrations.</param>
    /// <param name="databaseWaitTimeout">
    /// How long to keep waiting for the database to accept connections before giving
    /// up. Defaults to <see cref="DefaultDatabaseWaitSeconds"/> seconds.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when seeding is enabled but fails. Starting with a half-seeded
    /// database is worse than not starting: the failure would surface later as an
    /// inexplicable authorisation error rather than at the point of the problem.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Automatic migration on startup suits development and a single-instance
    /// deployment. It is <b>not</b> right for a multi-instance rollout, where
    /// several replicas would race to apply the same migration - there, run
    /// <c>dotnet ef database update</c> or a dedicated migration job as a
    /// deployment step and start the application with
    /// <paramref name="applyMigrations"/> false.
    /// </para>
    /// <para>
    /// Seeding itself is idempotent and safe to leave enabled.
    /// </para>
    /// </remarks>
    public static async Task InitializeAsync(
        IServiceProvider services,
        bool applyMigrations,
        TimeSpan? databaseWaitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ILoggerFactory loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        ILogger logger = loggerFactory.CreateLogger(typeof(DatabaseInitializer));

        await WaitForDatabaseAsync(
            scope.ServiceProvider,
            logger,
            databaseWaitTimeout ?? TimeSpan.FromSeconds(DefaultDatabaseWaitSeconds),
            cancellationToken);

        if (applyMigrations)
        {
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            IReadOnlyList<string> pending =
                [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];

            if (pending.Count > 0)
            {
                // The list is handed over as-is rather than joined into a string.
                // Nothing is computed at the call site, and Serilog renders the
                // collection only if the message is actually emitted - which also
                // keeps it queryable as a list rather than as prose.
                LogApplyingMigrations(logger, pending.Count, pending);

                await context.Database.MigrateAsync(cancellationToken);
            }
        }

        SeedOptions options = scope.ServiceProvider
            .GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!options.Enabled)
        {
            LogSeedingDisabled(logger, SeedOptions.SectionName);
            return;
        }

        DatabaseSeeder seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        Result<SeedSummary> result = await seeder.SeedAsync(cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Database seeding failed: {result.Error.Code} - {result.Error.Description}");
        }

        SeedSummary summary = result.Value;

        if (summary.AdministratorCreated)
        {
            // Logged once, at creation only, and without the password. Someone
            // bringing up a fresh installation needs to know what to type on the
            // sign-in screen, and the tenant code is not otherwise discoverable.
            LogAdministratorCreated(logger, summary.TenantCode);
        }
    }

    /// <summary>Waits for the database to start accepting connections.</summary>
    /// <param name="services">The scoped provider to resolve the context from.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the database is still unreachable when the timeout expires.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A container and the database it depends on are frequently started together, and
    /// PostgreSQL takes several seconds to initialise on its first boot. Connecting
    /// once and exiting on failure turns that ordinary race into a failed deployment,
    /// and the error it reports - "connection refused" - describes a database that is
    /// merely a few seconds late as though it were misconfigured.
    /// </para>
    /// <para>
    /// The wait is bounded, and expiry still throws. A database that never arrives is
    /// a genuine failure and must be loud: a process that hangs waiting for one is far
    /// harder to diagnose than a container that exits saying what it wanted.
    /// </para>
    /// </remarks>
    private static async Task WaitForDatabaseAsync(
        IServiceProvider services,
        ILogger logger,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ErpDbContext context = services.GetRequiredService<ErpDbContext>();

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        int attempt = 0;
        Exception? lastFailure = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            attempt++;

            try
            {
                if (await context.Database.CanConnectAsync(cancellationToken))
                {
                    if (attempt > 1)
                    {
                        LogDatabaseReady(logger, attempt);
                    }

                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Every provider reports "not listening yet" as its own exception
                // type, and none of them is worth enumerating: while the deadline
                // holds, any failure to connect is treated as the database still
                // coming up. The last one is kept so the give-up message can say what
                // was actually wrong.
                lastFailure = ex;
            }

            LogWaitingForDatabase(logger, attempt);

            await Task.Delay(RetryDelayMilliseconds, cancellationToken);
        }

        throw new InvalidOperationException(
            $"The database did not accept connections within {timeout.TotalSeconds:N0} " +
            $"seconds. Check that the server is running and that the connection string " +
            $"names the right host - on a managed platform it is built from PGHOST, " +
            $"PGPORT, PGDATABASE, PGUSER and PGPASSWORD unless ConnectionStrings__Postgres " +
            $"is set explicitly.",
            lastFailure);
    }

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Waiting for the database to accept connections (attempt {Attempt})")]
    private static partial void LogWaitingForDatabase(ILogger logger, int attempt);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Database accepted connections after {Attempts} attempts")]
    private static partial void LogDatabaseReady(ILogger logger, int attempts);

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Applying {Count} pending migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(
        ILogger logger,
        int count,
        IReadOnlyList<string> migrations);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Seeding is disabled. Enable it with {Section}:Enabled to create the " +
                  "permission catalogue, roles, and an administrator.")]
    private static partial void LogSeedingDisabled(ILogger logger, string section);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "An administrator account was created. Sign in with company code " +
                  "'{TenantCode}' and the configured administrator user name. The " +
                  "password must be changed at first sign-in.")]
    private static partial void LogAdministratorCreated(ILogger logger, string tenantCode);
}
