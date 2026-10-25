using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Import;

/// <summary>Writes the export document from an open EasyRetail connection.</summary>
/// <remarks>
/// One writer, used twice: by the server when it can reach the database itself, and by
/// the exporter a shop runs on its own computer when it cannot - that program compiles
/// this file and <see cref="EasyRetailQueries"/> into itself. So the file it saves and the
/// document the server reads are the same thing, byte for byte in shape.
/// </remarks>
internal static class EasyRetailExport
{
    /// <summary>The format name written into every document.</summary>
    public const string Format = "easyretail";

    /// <summary>The version of the document's shape.</summary>
    public const int Version = 1;

    /// <summary>How to reach an EasyRetail database: read intent only, and Windows sign-in unless a login is given.</summary>
    /// <param name="server">The SQL Server, such as <c>.\SQLEXPRESS</c>.</param>
    /// <param name="database">The database, or <c>master</c> to list the others.</param>
    /// <param name="userName">A SQL Server login; empty to sign in with Windows.</param>
    /// <param name="password">The login's password.</param>
    /// <param name="trustServerCertificate">Whether to accept the server's own certificate.</param>
    /// <param name="applicationName">What the connection is called in SQL Server's activity list.</param>
    /// <returns>The connection string.</returns>
    public static string ConnectionString(
        string server,
        string database,
        string? userName,
        string? password,
        bool trustServerCertificate,
        string applicationName)
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = server.Trim(),
            InitialCatalog = database.Trim(),
            TrustServerCertificate = trustServerCertificate,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ConnectTimeout = 15,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ApplicationName = applicationName,
        };

        if (string.IsNullOrWhiteSpace(userName))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = userName.Trim();
            builder.Password = password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    /// <summary>Reads every section of the database into one JSON document.</summary>
    /// <param name="output">Where the document is written.</param>
    /// <param name="sql">An open connection to the EasyRetail database.</param>
    /// <param name="database">The database's name, recorded in the document.</param>
    /// <param name="asAt">The date balances are read as at.</param>
    /// <param name="exportedAt">When the export was made, recorded in the document.</param>
    /// <param name="sectionRead">Told each section's name and row count as it is written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the whole document is written.</returns>
    public static async Task WriteAsync(
        Stream output,
        SqlConnection sql,
        string database,
        DateOnly asAt,
        DateTime exportedAt,
        Action<string, int>? sectionRead,
        CancellationToken cancellationToken)
    {
        await using Utf8JsonWriter json = new(output);

        json.WriteStartObject();
        json.WriteString("format", Format);
        json.WriteNumber("version", Version);
        json.WriteString("database", database);
        json.WriteString("asAt", asAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        json.WriteString("exportedAt", exportedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        foreach ((string name, bool single, string query) in EasyRetailQueries.Sections)
        {
            int rows = await WriteSectionAsync(json, sql, name, single, query, asAt, cancellationToken);
            sectionRead?.Invoke(name, rows);

            // Written out as it goes, so a large database is never held whole in memory.
            await json.FlushAsync(cancellationToken);
        }

        json.WriteEndObject();
    }

    private static async Task<int> WriteSectionAsync(
        Utf8JsonWriter json,
        SqlConnection sql,
        string name,
        bool single,
        string query,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = sql.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 600;
        command.Parameters.Add("@AsAt", SqlDbType.Date).Value = asAt.ToDateTime(TimeOnly.MinValue);

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        json.WritePropertyName(name);

        if (!single)
        {
            json.WriteStartArray();
        }

        int rows = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (single && rows > 0)
            {
                break;
            }

            json.WriteStartObject();

            for (int column = 0; column < reader.FieldCount; column++)
            {
                json.WritePropertyName(reader.GetName(column));
                WriteValue(json, reader.GetValue(column));
            }

            json.WriteEndObject();
            rows++;
        }

        if (single && rows == 0)
        {
            json.WriteNullValue();
        }

        if (!single)
        {
            json.WriteEndArray();
        }

        return rows;
    }

    private static void WriteValue(Utf8JsonWriter json, object value)
    {
        switch (value)
        {
            case DBNull:
                json.WriteNullValue();
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case int number:
                json.WriteNumberValue(number);
                break;
            case long number:
                json.WriteNumberValue(number);
                break;
            case short number:
                json.WriteNumberValue(number);
                break;
            case byte number:
                json.WriteNumberValue(number);
                break;
            case decimal number:
                json.WriteNumberValue(number);
                break;
            case double number:
                json.WriteNumberValue(number);
                break;
            default:
                json.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
