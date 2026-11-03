using System.Text.Json;

namespace ERP.EasyRetailExporter;

/// <summary>
/// What the window was filled in with the last time it saved a file, so the next export
/// starts there: the server, the login, the database and the folder. Never the password.
/// </summary>
/// <param name="Server">The SQL Server.</param>
/// <param name="User">The SQL Server login; null for Windows sign-in.</param>
/// <param name="Database">The database exported.</param>
/// <param name="Folder">The folder the file was saved in.</param>
internal sealed record Remembered(string? Server, string? User, string? Database, string? Folder)
{
    /// <summary>Per Windows user, beside the user's other application data.</summary>
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyRetail export",
        "settings.json");

    /// <summary>A first run's: nothing remembered.</summary>
    public static Remembered Nothing { get; } = new(null, null, null, null);

    /// <summary>Reads what was remembered.</summary>
    /// <returns>It, or <see cref="Nothing"/> where nothing could be read.</returns>
    public static Remembered Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Remembered>(File.ReadAllText(FilePath)) ?? Nothing
                : Nothing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable is as good as never saved: the window starts from its defaults.
            return Nothing;
        }
    }

    /// <summary>Remembers this for the next run.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered is no failure: the export itself is saved.
        }
    }
}
