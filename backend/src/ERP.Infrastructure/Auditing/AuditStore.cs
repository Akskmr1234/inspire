using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ERP.Infrastructure.Auditing;

/// <summary>Where the audit trail is kept.</summary>
/// <remarks>
/// <para>
/// In a database of its own: <c>ConnectionStrings:Audit</c> where one is configured,
/// otherwise the application's database name with <c>_audit</c> appended, on the same
/// server and with the same credentials.
/// </para>
/// <para>
/// Where that database does not exist and the application's role may not create it, the
/// trail is kept in the <c>audit</c> schema of the application's own database instead, and
/// the startup log says what an administrator runs to give it a database of its own. A
/// trail kept beside the books is still a trail; a trail not kept at all is not.
/// </para>
/// </remarks>
public sealed partial class AuditStore
{
    private readonly string _main;

    /// <summary>Initialises a new instance of the <see cref="AuditStore"/> class.</summary>
    /// <param name="main">The application's own connection string.</param>
    /// <param name="configured">The audit database's, where one is configured.</param>
    public AuditStore(string main, string? configured)
    {
        _main = main;
        ConnectionString = string.IsNullOrWhiteSpace(configured) ? Derive(main) : configured;
    }

    /// <summary>Gets the connection string the trail is written through.</summary>
    public string ConnectionString { get; private set; }

    /// <summary>Gets whether the trail has a database of its own.</summary>
    public bool IsSeparate { get; private set; } = true;

    /// <summary>Gets the name of the database the trail is kept in.</summary>
    public string DatabaseName => new NpgsqlConnectionStringBuilder(ConnectionString).Database ?? string.Empty;

    /// <summary>
    /// Makes sure the trail's database exists and is up to date, falling back to the
    /// application's own database where it cannot be had.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="migrate">Whether to bring its schema up to date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the trail can be written.</returns>
    public async Task InitialiseAsync(
        IServiceProvider services,
        ILogger logger,
        bool migrate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!await ExistsAsync(cancellationToken) && !await CreateAsync(logger, cancellationToken))
        {
            ConnectionString = _main;
            IsSeparate = false;
        }

        if (migrate)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IDbContextFactory<AuditDbContext> factory =
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuditDbContext>>();

            await using AuditDbContext context = await factory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
        }

        LogAuditStore(logger, DatabaseName, IsSeparate);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex SafeName();

    private static string Derive(string main)
    {
        NpgsqlConnectionStringBuilder builder = new(main);
        builder.Database = $"{builder.Database}_audit";

        return builder.ConnectionString;
    }

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Information,
        Message = "Audit trail kept in database '{Database}' (separate: {Separate}).")]
    private static partial void LogAuditStore(ILogger logger, string database, bool separate);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Warning,
        Message = "The audit database '{Database}' does not exist and could not be created ({Reason}). "
            + "The audit trail is kept in the 'audit' schema of the application's database until "
            + "an administrator runs: CREATE DATABASE \"{Database}\" OWNER {Owner};")]
    private static partial void LogCannotCreate(ILogger logger, string database, string reason, string owner);

    private async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using NpgsqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S2077:Formatting SQL queries is security-sensitive",
        Justification =
            "CREATE DATABASE takes no parameters. The name comes from configuration, never from a "
            + "request, and is refused unless it is letters, digits and underscores.")]
    private async Task<bool> CreateAsync(ILogger logger, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder target = new(ConnectionString);
        string database = target.Database ?? string.Empty;
        string owner = target.Username ?? new NpgsqlConnectionStringBuilder(_main).Username ?? "erp_app";

        if (!SafeName().IsMatch(database))
        {
            LogCannotCreate(logger, database, "the name is not letters, digits and underscores", owner);

            return false;
        }

        try
        {
            await using NpgsqlConnection connection = new(_main);
            await connection.OpenAsync(cancellationToken);

            // A database name cannot be a bound parameter; it is checked above instead.
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{database}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);

            return true;
        }
        catch (PostgresException exception)
        {
            LogCannotCreate(logger, database, exception.MessageText, owner);

            return false;
        }
    }
}
