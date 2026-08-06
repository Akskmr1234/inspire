using System.Data;
using System.Globalization;
using ERP.Application.Platform.Dashboards;
using ERP.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Runs the statement behind a custom dashboard widget.</summary>
/// <remarks>
/// <para>
/// This is where the guarantees about custom SQL actually live. The validation in the
/// application layer produces a clear error for an obvious mistake; what makes running
/// somebody else's SQL defensible is here, and it is three things.
/// </para>
/// <para>
/// The statement runs in a <c>READ ONLY</c> transaction, so PostgreSQL itself refuses
/// any write that got past validation - the guarantee does not depend on having parsed
/// the text correctly. It runs under a statement timeout and wrapped in a row cap, so
/// no widget can hold a connection open or pull an unbounded result into a dashboard
/// panel. And it runs on the ordinary application connection as the ordinary
/// application role, which is the important one: row-level security applies to it
/// exactly as to every other query, so a statement reaching for another tenant's rows
/// returns nothing. That check is in the database, and no amount of ingenuity in the
/// SQL moves it.
/// </para>
/// </remarks>
public sealed partial class CustomWidgetExecutor : ICustomWidgetExecutor
{
    /// <summary>How long a widget query may run before the database stops it.</summary>
    private const int StatementTimeoutMilliseconds = 5_000;

    /// <summary>The most rows a widget may return.</summary>
    /// <remarks>
    /// A dashboard panel draws a handful of bars or a short ranked list. Anything
    /// beyond this is a query written against the wrong grain, and truncating it is
    /// kinder than serialising it.
    /// </remarks>
    private const int MaximumRows = 500;

    private readonly ErpDbContext _context;
    private readonly ILogger<CustomWidgetExecutor> _logger;

    /// <summary>Initialises a new instance of the <see cref="CustomWidgetExecutor"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">The logger.</param>
    public CustomWidgetExecutor(ErpDbContext context, ILogger<CustomWidgetExecutor> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MetricPoint>>> ExecuteAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        // The context's own connection, so the tenant the connection interceptor has
        // already established still applies. Opening a fresh connection would leave
        // app.current_tenant unset, and every row-level-security policy would then
        // match nothing - which fails safe, but silently and confusingly.
        NpgsqlConnection connection = (NpgsqlConnection)_context.Database.GetDbConnection();

        bool openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        NpgsqlTransaction? transaction = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            // Made read-only at the database rather than trusted to be so. Everything
            // after this point is incapable of writing, whatever the statement says.
            await using (NpgsqlCommand guard = connection.CreateCommand())
            {
                guard.Transaction = transaction;

                // The analyser is right that this is composed rather than
                // parameterised, and it cannot be otherwise: SET TRANSACTION and
                // statement_timeout take no parameters. The interpolated value is an
                // int constant declared above, never anything a caller supplies.
#pragma warning disable S2077 // Use a parameterized query instead of string formatting
                guard.CommandText =
                    "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = "
                    + StatementTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + ";";

#pragma warning restore S2077

                await guard.ExecuteNonQueryAsync(cancellationToken);
            }

            List<MetricPoint> points = [];

            await using (NpgsqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                // Composed on purpose, and the one place in the system where that is
                // true. The statement IS the user input - there is no parameter form of
                // "run this query" - which is exactly why it is wrapped in a read-only
                // transaction, capped, timed out, and left subject to row-level
                // security. The LIMIT is an int constant, not caller input.
                //
                // Wrapped rather than trusted to bound itself: a caller's own LIMIT is
                // respected, being inside the subquery, and this only caps one with none.
#pragma warning disable S2077 // Use a parameterized query instead of string formatting
                command.CommandText =
                    "SELECT * FROM (" + query + ") AS widget LIMIT "
                    + MaximumRows.ToString(CultureInfo.InvariantCulture);

#pragma warning restore S2077

                await using NpgsqlDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                int labelOrdinal = reader.GetOrdinal(CustomWidgetQuery.LabelColumn);
                int valueOrdinal = reader.GetOrdinal(CustomWidgetQuery.ValueColumn);

                while (await reader.ReadAsync(cancellationToken))
                {
                    string label = reader.IsDBNull(labelOrdinal)
                        ? string.Empty
                        : reader.GetValue(labelOrdinal).ToString() ?? string.Empty;

                    decimal value = reader.IsDBNull(valueOrdinal)
                        ? 0m
                        : Convert.ToDecimal(
                            reader.GetValue(valueOrdinal), CultureInfo.InvariantCulture);

                    points.Add(new MetricPoint(label, value));
                }
            }

            // Rolled back rather than committed. Nothing was written - nothing could
            // have been - so there is nothing to keep, and rolling back says so.
            await transaction.RollbackAsync(cancellationToken);

            return Result.Success<IReadOnlyList<MetricPoint>>(points);
        }
        catch (Exception ex)
            when (ex is PostgresException or NpgsqlException or InvalidOperationException
                or FormatException or OverflowException or IndexOutOfRangeException)
        {
            // A widget whose query no longer runs must not take the dashboard with it.
            // Logged in full, reported in summary: a database error message can name
            // columns and tables the reader has no business learning about.
            LogWidgetQueryFailed(_logger, ex);

            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return Result.Failure<IReadOnlyList<MetricPoint>>(Error.Validation(
                "CustomWidget.QueryFailed",
                "This widget's query could not be run. Check it against the data it reads."));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "A custom dashboard widget query failed")]
    private static partial void LogWidgetQueryFailed(ILogger logger, Exception exception);
}
