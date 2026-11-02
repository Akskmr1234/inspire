namespace ERP.EasyRetailExporter;

/// <summary>
/// Saves an EasyRetail database to <c>easyretail-export.json</c>, for Settings › Import data.
/// </summary>
/// <remarks>
/// Run on the computer that has EasyRetail - usually by double-clicking it, which opens a
/// window that finds the SQL Server, offers the EasyRetail databases on it and saves the
/// file. Given options it does the same at the command line, for a script (see
/// <see cref="ExportOptions"/>). Either way it only reads: the connection asks for read
/// intent and every query is a <c>SELECT</c>.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunWindow();
        }

        return await CommandLine.RunAsync(args, ConsoleHost.Open());
    }

    /// <summary>Shows the window until it is closed.</summary>
    /// <returns>0: whatever happened is said in the window.</returns>
    private static int RunWindow()
    {
        // Windows Forms needs a single-threaded apartment - the save dialog and the
        // clipboard do - and an async Main has none, so the window gets a thread of its own.
        Thread window = new(ShowWindow) { Name = "Export window" };
        window.SetApartmentState(ApartmentState.STA);
        window.Start();
        window.Join();

        return 0;
    }

    private static void ShowWindow()
    {
        ApplicationConfiguration.Initialize();

        // Windows' own interface font, set here rather than in the project: the generated
        // setting throws where the font is missing, and this falls back to the default one.
        Application.SetDefaultFont(new Font("Segoe UI", 9.75F));

        // Anything the window did not see coming is said in a message rather than closing it.
        Application.ThreadException += (_, failure) => MessageBox.Show(
            failure.Exception.Message,
            "EasyRetail export",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        using ExportForm form = new(new SqlExportBackend(), Remembered.Load(), LocalServers.Find());
        Application.Run(form);
    }
}
