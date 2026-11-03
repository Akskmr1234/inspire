using System.ComponentModel;
using System.Diagnostics;
using ERP.Infrastructure.Import;
using Microsoft.Data.SqlClient;

namespace ERP.EasyRetailExporter;

/// <summary>A SQL Server, and the sign-in that reaches it: Windows when there is no login.</summary>
/// <param name="Server">The server, such as <c>.\SQLEXPRESS</c>.</param>
/// <param name="User">A SQL Server login, or null to sign in with Windows.</param>
/// <param name="Password">The login's password.</param>
internal sealed record ServerSignIn(string Server, string? User, string Password)
{
    /// <summary>What the connection is called in SQL Server's activity list.</summary>
    private const string ApplicationName = "Aider ERP export";

    /// <summary>The connection string for one of the server's databases.</summary>
    /// <param name="database">The database, or <c>master</c> to list the others.</param>
    /// <returns>The connection string: read intent, and the sign-in above.</returns>
    public string ConnectionString(string database) => EasyRetailExport.ConnectionString(
        Server,
        database,
        User,
        Password,
        trustServerCertificate: true,
        ApplicationName);

    /// <summary>The server and who signs in to it, for a message - never the password.</summary>
    /// <returns>Such as <c>.\SQLEXPRESS (Windows sign-in)</c>.</returns>
    public override string ToString() => User is null ? $"{Server} (Windows sign-in)" : $"{Server} ({User})";
}

/// <summary>A database on the server, and whether it looks like EasyRetail's.</summary>
/// <param name="Name">The database's name.</param>
/// <param name="EasyRetail">Whether it has EasyRetail's vouchers, products and ledgers.</param>
internal sealed record DatabaseChoice(string Name, bool EasyRetail);

/// <summary>What the window and the command line ask of a SQL Server.</summary>
internal interface IExportBackend
{
    /// <summary>Signs in and lists the databases the sign-in can open, newest first.</summary>
    /// <param name="signIn">The server and the sign-in.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The databases, each marked with whether it looks like EasyRetail's.</returns>
    Task<IReadOnlyList<DatabaseChoice>> ListDatabasesAsync(ServerSignIn signIn, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a database into the export file. Written beside it and moved into place at the
    /// end, so a run that fails or is stopped leaves no half file behind.
    /// </summary>
    /// <param name="signIn">The server and the sign-in.</param>
    /// <param name="database">The EasyRetail database.</param>
    /// <param name="asAt">The date balances are read as at.</param>
    /// <param name="output">Where the file is saved.</param>
    /// <param name="sectionRead">Told each section's name and row count as it is written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the file is in place.</returns>
    Task ExportAsync(
        ServerSignIn signIn,
        string database,
        DateOnly asAt,
        string output,
        Action<string, int> sectionRead,
        CancellationToken cancellationToken);
}

/// <summary>The SQL Server itself, read with the server's own queries and writer.</summary>
internal sealed class SqlExportBackend : IExportBackend
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseChoice>> ListDatabasesAsync(
        ServerSignIn signIn,
        CancellationToken cancellationToken)
    {
        await using SqlConnection master = new(signIn.ConnectionString("master"));
        await master.OpenAsync(cancellationToken);

        await using SqlCommand command = master.CreateCommand();
        command.CommandText = """
            SELECT name,
                CASE WHEN OBJECT_ID(QUOTENAME(name) + N'.dbo.Voucher', N'U') IS NOT NULL
                      AND OBJECT_ID(QUOTENAME(name) + N'.dbo.Product', N'U') IS NOT NULL
                      AND OBJECT_ID(QUOTENAME(name) + N'.dbo.Ledger', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END AS easyRetail
            FROM sys.databases
            WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1
            ORDER BY create_date DESC
            """;

        List<DatabaseChoice> databases = [];

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new DatabaseChoice(reader.GetString(0), reader.GetInt32(1) == 1));
        }

        return databases;
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        ServerSignIn signIn,
        string database,
        DateOnly asAt,
        string output,
        Action<string, int> sectionRead,
        CancellationToken cancellationToken)
    {
        string partial = output + ".part";

        try
        {
            await using (SqlConnection sql = new(signIn.ConnectionString(database)))
            {
                await sql.OpenAsync(cancellationToken);

                await using FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None);

                await EasyRetailExport.WriteAsync(
                    file,
                    sql,
                    database,
                    asAt,
                    DateTime.Now,
                    sectionRead,
                    cancellationToken);
            }

            File.Move(partial, output, overwrite: true);
        }
        catch
        {
            Discard(partial);
            throw;
        }
    }

    private static void Discard(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind; the next run overwrites it.
        }
    }
}

/// <summary>The export file's name, and where it goes unless somewhere else is chosen.</summary>
internal static class ExportFile
{
    /// <summary>The file's name: the one Settings › Import data asks for.</summary>
    public const string Name = "easyretail-export.json";

    /// <summary>The Desktop, where the file is easy to find; the current folder where there is none.</summary>
    /// <returns>The folder.</returns>
    public static string DefaultFolder()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        return string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop) ? Environment.CurrentDirectory : desktop;
    }

    /// <summary>The file on the Desktop.</summary>
    /// <returns>Its full path.</returns>
    public static string DefaultPath() => Path.Combine(DefaultFolder(), Name);
}

/// <summary>Windows Explorer, to show a saved file in its folder.</summary>
internal static class Explorer
{
    /// <summary>Opens the file's folder with the file selected, so it is found at once.</summary>
    /// <param name="file">The file; nothing happens for none.</param>
    public static void Select(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

            using Process? shown = Process.Start(new ProcessStartInfo(explorer, $"/select,\"{file}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Win32Exception)
        {
            // The file is saved either way, and its path is shown beside the button.
        }
    }
}
