namespace ERP.EasyRetailExporter;

/// <summary>What the command line gave; anything left out takes its default.</summary>
/// <param name="Server">The SQL Server, such as <c>.\SQLEXPRESS</c>.</param>
/// <param name="Database">The EasyRetail database.</param>
/// <param name="User">A SQL Server login; left out to sign in with Windows.</param>
/// <param name="Password">The login's password.</param>
/// <param name="AsAt">The date balances are read as at, <c>yyyy-MM-dd</c>.</param>
/// <param name="Out">Where the file is saved.</param>
internal sealed record ExportOptions(
    string? Server,
    string? Database,
    string? User,
    string? Password,
    string? AsAt,
    string? Out)
{
    /// <summary>How the command line is written, for <c>--help</c> and for a mistake.</summary>
    public const string Usage = """
        easyretail-export
            Opens the export window.

        easyretail-export [--server NAME] [--database NAME] [--as-at yyyy-mm-dd]
                          [--user LOGIN --password PASSWORD] [--out FILE]
            Exports without the window.

          --server     SQL Server, for example .\SQLEXPRESS (default: .\SQLEXPRESS)
          --database   EasyRetail database, for example EasyRetail2025
                       (default: the only EasyRetail database on the server)
          --as-at      Balances as at this date (default: today)
          --user       SQL Server login (default: sign in with Windows)
          --password   The login's password
          --out        Where to save (default: easyretail-export.json on the Desktop)

        From a Command Prompt, start it with "start /wait easyretail-export ..." to wait
        for it to finish and read its exit code: 0 saved, 1 not saved, 2 a mistake here.
        """;

    /// <summary>Reads the command line.</summary>
    /// <param name="args">Its arguments: <c>--name value</c> pairs, one dash or two.</param>
    /// <returns>The options, or null when the command line asks for help or cannot be read.</returns>
    public static ExportOptions? Parse(IReadOnlyList<string> args)
    {
        Dictionary<string, string> given = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < args.Count; index += 2)
        {
            string name = args[index].TrimStart('-').Replace("-", string.Empty, StringComparison.Ordinal);

            if (!args[index].StartsWith('-') || index + 1 >= args.Count || name is "help" or "h" or "?")
            {
                return null;
            }

            given[name] = args[index + 1];
        }

        string[] known = ["server", "database", "user", "password", "asat", "out"];

        if (given.Keys.Any(name => !known.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new ExportOptions(
            given.GetValueOrDefault("server"),
            given.GetValueOrDefault("database"),
            given.GetValueOrDefault("user"),
            given.GetValueOrDefault("password"),
            given.GetValueOrDefault("asat"),
            given.GetValueOrDefault("out"));
    }
}
