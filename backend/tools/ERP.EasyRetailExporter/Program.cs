using System.Diagnostics;
using System.Globalization;
using System.Text;
using ERP.Infrastructure.Import;
using Microsoft.Data.SqlClient;

namespace ERP.EasyRetailExporter;

/// <summary>
/// Saves an EasyRetail database to <c>easyretail-export.json</c>, for Settings › Import data
/// in Aider ERP.
/// </summary>
/// <remarks>
/// Run on the computer that has EasyRetail - usually by double-clicking it. It asks for the
/// SQL Server, offers the EasyRetail databases it finds there, and saves the file on the
/// Desktop. It only reads: the connection asks for read intent and every query is a
/// <c>SELECT</c>.
/// </remarks>
internal static class Program
{
    private const string ApplicationName = "Aider ERP export";
    private const string DefaultServer = @".\SQLEXPRESS";
    private const string FileName = "easyretail-export.json";

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy"];

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        ExportOptions? options = ExportOptions.Parse(args);

        if (options is null)
        {
            Console.WriteLine(ExportOptions.Usage);
            return 2;
        }

        // Double-clicked, the window closes the moment the program ends; so it waits.
        bool pause = args.Length == 0 && !Console.IsInputRedirected;

        using CancellationTokenSource stop = new();
        Console.CancelKeyPress += (_, press) =>
        {
            press.Cancel = true;
            stop.Cancel();
        };

        int exitCode;

        try
        {
            exitCode = await RunAsync(options, stop.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Stopped. Nothing was saved.");
            exitCode = 1;
        }

        if (pause)
        {
            Prompt.Pause();
        }

        return exitCode;
    }

    private static async Task<int> RunAsync(ExportOptions options, CancellationToken cancellationToken)
    {
        Console.WriteLine("EasyRetail export for Aider ERP");
        Console.WriteLine("Saves accounts, customers, suppliers, products, balances and stock to one file.");
        Console.WriteLine("It only reads. Nothing in EasyRetail is changed.");
        Console.WriteLine("Press Enter to accept the suggestion in [brackets].");
        Console.WriteLine();

        SignIn? signIn = await SignInAsync(options, cancellationToken);

        if (signIn is null)
        {
            return 1;
        }

        string? database = options.Database ?? ChooseDatabase(signIn);

        if (string.IsNullOrWhiteSpace(database))
        {
            Console.WriteLine("No database was chosen.");
            return 1;
        }

        DateOnly? asAt = AskDate(options.AsAt);

        if (asAt is null)
        {
            return 1;
        }

        string output = Path.GetFullPath(options.Out ?? DefaultOutput());

        return await ExportAsync(signIn, database.Trim(), asAt.Value, output, cancellationToken) ? 0 : 1;
    }

    /// <summary>Connects to the server, asking again until it answers or nothing more can be asked.</summary>
    private static async Task<SignIn?> SignInAsync(ExportOptions options, CancellationToken cancellationToken)
    {
        string server = options.Server ?? DefaultServer;
        string user = options.User ?? string.Empty;
        string password = options.Password ?? string.Empty;
        bool ask = !Console.IsInputRedirected;

        while (true)
        {
            if (ask && options.Server is null)
            {
                server = Prompt.Ask("SQL Server", server);
            }

            if (ask && options.User is null)
            {
                user = Prompt.Ask("SQL login (Enter to sign in with Windows)");
            }

            if (ask && user.Length > 0 && options.Password is null)
            {
                password = Prompt.AskSecret("Password");
            }

            SignIn signIn = new(server, user.Length > 0 ? user : null, password);

            try
            {
                await using SqlConnection master = new(signIn.ConnectionString("master"));
                await master.OpenAsync(cancellationToken);

                signIn.Databases = await ListDatabasesAsync(master, cancellationToken);
                Console.WriteLine();
                return signIn;
            }
            catch (Exception exception) when (exception is SqlException or PlatformNotSupportedException)
            {
                Console.WriteLine();
                Console.WriteLine($"Could not connect to {server}: {exception.Message}");

                // Nothing can be asked again when every answer came from the command line.
                bool answered = options.Server is not null && options.User is not null;

                if (!ask || answered)
                {
                    return null;
                }

                Console.WriteLine("Check the server name and the sign-in, and try again.");
                Console.WriteLine();
            }
        }
    }

    /// <summary>The databases the sign-in can open, newest first, with those that look like EasyRetail marked.</summary>
    private static async Task<IReadOnlyList<(string Name, bool EasyRetail)>> ListDatabasesAsync(
        SqlConnection master,
        CancellationToken cancellationToken)
    {
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

        List<(string Name, bool EasyRetail)> databases = [];

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add((reader.GetString(0), reader.GetInt32(1) == 1));
        }

        return databases;
    }

    /// <summary>Offers the EasyRetail databases by number - or every database, when none is recognised.</summary>
    private static string? ChooseDatabase(SignIn signIn)
    {
        List<string> offered = [.. signIn.Databases.Where(database => database.EasyRetail).Select(database => database.Name)];

        if (offered.Count == 0)
        {
            offered = [.. signIn.Databases.Select(database => database.Name)];

            if (offered.Count > 0)
            {
                Console.WriteLine($"No EasyRetail database was recognised on {signIn.Server}. Its databases are:");
            }
        }
        else
        {
            Console.WriteLine($"EasyRetail databases on {signIn.Server}:");
        }

        for (int index = 0; index < offered.Count; index++)
        {
            Console.WriteLine($"  {index + 1,3}  {offered[index]}");
        }

        if (Console.IsInputRedirected)
        {
            return offered.Count == 1 ? offered[0] : null;
        }

        string answer = offered.Count > 0
            ? Prompt.Ask("Database (number or name)", "1")
            : Prompt.Ask("EasyRetail database name, for example EasyRetail2025");

        return int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number >= 1
            && number <= offered.Count
                ? offered[number - 1]
                : answer;
    }

    /// <summary>The date balances are read as at: the command line's, or asked for, with today suggested.</summary>
    private static DateOnly? AskDate(string? given)
    {
        string today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool ask = given is null && !Console.IsInputRedirected;
        string answer = given ?? today;

        while (true)
        {
            if (ask)
            {
                answer = Prompt.Ask("Balances as at, yyyy-mm-dd", today);
            }

            if (DateOnly.TryParseExact(answer, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                return date;
            }

            Console.WriteLine($"'{answer}' is not a date. Enter it as yyyy-mm-dd, for example {today}.");

            if (!ask)
            {
                return null;
            }
        }
    }

    /// <summary>The Desktop, where the file is easy to find; the current folder where there is none.</summary>
    private static string DefaultOutput()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        return Path.Combine(
            string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop) ? Environment.CurrentDirectory : desktop,
            FileName);
    }

    /// <summary>Writes the document beside the target and moves it into place, so a failed run leaves no half file.</summary>
    private static async Task<bool> ExportAsync(
        SignIn signIn,
        string database,
        DateOnly asAt,
        string output,
        CancellationToken cancellationToken)
    {
        string partial = output + ".part";

        Console.WriteLine();
        Console.WriteLine($"Reading {database}, balances as at {asAt:yyyy-MM-dd}...");

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
                    (section, rows) => Console.WriteLine($"  {section,-12} {rows,9:N0}"),
                    cancellationToken);
            }

            File.Move(partial, output, overwrite: true);
        }
        catch (Exception exception) when (exception is SqlException or IOException or UnauthorizedAccessException)
        {
            Discard(partial);
            Console.WriteLine();
            Console.WriteLine(exception is SqlException
                ? $"The database could not be read: {exception.Message}"
                : $"The file could not be saved: {exception.Message}");
            return false;
        }
        catch (OperationCanceledException)
        {
            Discard(partial);
            throw;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Saved to {output}");
        Console.WriteLine("In Aider ERP open Settings > Import data > Export file, and choose this file.");

        ShowInExplorer(output);
        return true;
    }

    private static void Discard(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (IOException)
        {
            // Left behind; the next run overwrites it.
        }
    }

    /// <summary>Opens the folder with the file selected, on Windows, so it is found at once.</summary>
    private static void ShowInExplorer(string output)
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return;
        }

        try
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

            using Process? shown = Process.Start(new ProcessStartInfo(explorer, $"/select,\"{output}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The file is saved either way, and its path is printed above.
        }
    }

    /// <summary>The server and the sign-in that reached it, and the databases it offered.</summary>
    private sealed class SignIn(string server, string? user, string password)
    {
        public string Server { get; } = server;

        public IReadOnlyList<(string Name, bool EasyRetail)> Databases { get; set; } = [];

        public string ConnectionString(string database) => EasyRetailExport.ConnectionString(
            Server,
            database,
            user,
            password,
            trustServerCertificate: true,
            ApplicationName);
    }
}
