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
    /// <summary>
    /// Brings the database up to date and seeds it, if seeding is enabled.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="applyMigrations">Whether to apply pending migrations.</param>
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ILoggerFactory loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        ILogger logger = loggerFactory.CreateLogger(typeof(DatabaseInitializer));

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
