using System.Text;
using ERP.Application.Platform.Import;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Import;

/// <summary>Reads an EasyRetail database the server can reach.</summary>
/// <remarks>
/// <para>
/// EasyRetail keeps its books in SQL Server on the shop's own computer, which a server in
/// the cloud usually cannot reach. So there are two ways in: the server reads the database
/// itself when it can, and otherwise the shop runs the exporter - a Windows program built
/// from tools/ERP.EasyRetailExporter - which reads it and saves the same document to a
/// file, which is uploaded. Both write it with <see cref="EasyRetailExport"/>.
/// </para>
/// <para>
/// Read only, and nothing else: the connection asks for read intent and the queries are
/// all <c>SELECT</c>s.
/// </para>
/// </remarks>
public sealed class EasyRetailSource : IEasyRetailSource
{
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="EasyRetailSource"/> class.</summary>
    /// <param name="clock">The clock, for today's date.</param>
    public EasyRetailSource(IClock clock) => _clock = clock;

    /// <inheritdoc />
    public async Task<Result<string>> ReadAsync(
        EasyRetailConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Server) || string.IsNullOrWhiteSpace(connection.Database))
        {
            return Result.Failure<string>(Error.Validation(
                "Import.ConnectionIncomplete", "Enter the SQL Server and the database name."));
        }

        string connectionString = EasyRetailExport.ConnectionString(
            connection.Server,
            connection.Database,
            connection.UserName,
            connection.Password,
            connection.TrustServerCertificate,
            "Aider ERP import");

        DateOnly asAt = connection.AsAt ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        try
        {
            await using SqlConnection sql = new(connectionString);
            await sql.OpenAsync(cancellationToken);

            using MemoryStream buffer = new();

            await EasyRetailExport.WriteAsync(
                buffer,
                sql,
                connection.Database.Trim(),
                asAt,
                _clock.UtcNow.UtcDateTime,
                sectionRead: null,
                cancellationToken);

            return Result.Success(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (SqlException exception)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "Import.SourceUnreadable",
                $"The EasyRetail database could not be read: {exception.Message}"));
        }
        catch (PlatformNotSupportedException)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "Import.WindowsSignInUnavailable",
                "This server cannot sign in to SQL Server with Windows authentication. "
                + "Enter a SQL Server login and password, or export with the exporter instead."));
        }
    }
}
