using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace ERP.EasyRetailExporter;

/// <summary>
/// The export at the command line, for a script or a support call: the options given, and
/// the rest taken from their defaults - or asked for, in a console the program opened for
/// itself, where nobody else is reading what is typed.
/// </summary>
internal static class CommandLine
{
    private const string DefaultServer = @".\SQLEXPRESS";

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy"];

    /// <summary>Runs the export the command line describes.</summary>
    /// <param name="args">The command line's arguments.</param>
    /// <param name="console">
    /// Where the output goes. Only in a console of the program's own are questions asked, and
    /// is it kept open at the end so what it says can be read: a borrowed one is shared with
    /// the Command Prompt reading it, and redirected output has nobody to answer.
    /// </param>
    /// <returns>0 once the file is saved, 1 when it is not, 2 for a command line that cannot be read.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, ConsoleKind console)
    {
        bool ownConsole = console == ConsoleKind.Own;

        if (console is ConsoleKind.Borrowed or ConsoleKind.Own)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        // A Command Prompt does not wait for a windowed program, so its prompt is already
        // back on the line this one would start on.
        if (console == ConsoleKind.Borrowed)
        {
            Console.WriteLine();
        }

        ExportOptions? options = ExportOptions.Parse(args);
        int exitCode;

        if (options is null)
        {
            Console.WriteLine(ExportOptions.Usage);
            exitCode = 2;
        }
        else
        {
            using CancellationTokenSource stop = new();
            Console.CancelKeyPress += (_, press) =>
            {
                press.Cancel = true;
                stop.Cancel();
            };

            try
            {
                exitCode = await RunAsync(options, ownConsole && !Console.IsInputRedirected, stop.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("Stopped. Nothing was saved.");
                exitCode = 1;
            }
        }

        if (ownConsole && !Console.IsInputRedirected)
        {
            Prompt.Pause();
        }

        return exitCode;
    }

    private static async Task<int> RunAsync(ExportOptions options, bool ask, CancellationToken cancellationToken)
    {
        Console.WriteLine("EasyRetail export");
        Console.WriteLine("Saves accounts, customers, suppliers, products, balances and stock to one file.");
        Console.WriteLine("It only reads. Nothing in EasyRetail is changed.");

        if (ask)
        {
            Console.WriteLine("Press Enter to accept the suggestion in [brackets].");
        }

        Console.WriteLine();

        SqlExportBackend backend = new();
        (ServerSignIn SignIn, IReadOnlyList<DatabaseChoice> Databases)? connected =
            await SignInAsync(backend, options, ask, cancellationToken);

        if (connected is null)
        {
            return 1;
        }

        (ServerSignIn signIn, IReadOnlyList<DatabaseChoice> databases) = connected.Value;
        string? database = options.Database ?? ChooseDatabase(signIn, databases, ask);

        if (string.IsNullOrWhiteSpace(database))
        {
            Console.WriteLine(ask ? "No database was chosen." : "No database was chosen. Name one with --database.");
            return 1;
        }

        DateOnly? asAt = AskDate(options.AsAt, ask);

        if (asAt is null)
        {
            return 1;
        }

        string output = Path.GetFullPath(options.Out ?? ExportFile.DefaultPath());

        return await ExportAsync(backend, signIn, database.Trim(), asAt.Value, output, ask, cancellationToken) ? 0 : 1;
    }

    /// <summary>Connects to the server, asking again until it answers or nothing more can be asked.</summary>
    private static async Task<(ServerSignIn SignIn, IReadOnlyList<DatabaseChoice> Databases)?> SignInAsync(
        SqlExportBackend backend,
        ExportOptions options,
        bool ask,
        CancellationToken cancellationToken)
    {
        string server = options.Server ?? DefaultServer;
        string user = options.User ?? string.Empty;
        string password = options.Password ?? string.Empty;

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

            ServerSignIn signIn = new(server, user.Length > 0 ? user : null, password);

            try
            {
                IReadOnlyList<DatabaseChoice> databases = await backend.ListDatabasesAsync(signIn, cancellationToken);
                Console.WriteLine();
                return (signIn, databases);
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

    /// <summary>Offers the EasyRetail databases by number - or every database, when none is recognised.</summary>
    private static string? ChooseDatabase(ServerSignIn signIn, IReadOnlyList<DatabaseChoice> databases, bool ask)
    {
        List<string> offered = [.. databases.Where(database => database.EasyRetail).Select(database => database.Name)];

        if (offered.Count == 0)
        {
            offered = [.. databases.Select(database => database.Name)];

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

        if (!ask)
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
    private static DateOnly? AskDate(string? given, bool ask)
    {
        string today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool asking = given is null && ask;
        string answer = given ?? today;

        while (true)
        {
            if (asking)
            {
                answer = Prompt.Ask("Balances as at, yyyy-mm-dd", today);
            }

            if (DateOnly.TryParseExact(answer, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                return date;
            }

            Console.WriteLine($"'{answer}' is not a date. Enter it as yyyy-mm-dd, for example {today}.");

            if (!asking)
            {
                return null;
            }
        }
    }

    private static async Task<bool> ExportAsync(
        SqlExportBackend backend,
        ServerSignIn signIn,
        string database,
        DateOnly asAt,
        string output,
        bool ask,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Reading {database}, balances as at {asAt:yyyy-MM-dd}...");

        try
        {
            await backend.ExportAsync(
                signIn,
                database,
                asAt,
                output,
                (section, rows) => Console.WriteLine($"  {section,-12} {rows,9:N0}"),
                cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine();
            Console.WriteLine(exception is SqlException
                ? $"The database could not be read: {exception.Message}"
                : $"The file could not be saved: {exception.Message}");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Saved to {output}");
        Console.WriteLine("In the ERP open Settings > Import data > Export file, and choose this file.");

        if (ask)
        {
            Explorer.Select(output);
        }

        return true;
    }
}
