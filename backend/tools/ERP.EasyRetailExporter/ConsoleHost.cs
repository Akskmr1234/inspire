using System.Runtime.InteropServices;

namespace ERP.EasyRetailExporter;

/// <summary>Where a command-line run's output goes, and so whether anyone can answer it.</summary>
internal enum ConsoleKind
{
    /// <summary>Into a file or a pipe, for a script: nothing is asked, and nothing waits at the end.</summary>
    Redirected,

    /// <summary>Into the console of the Command Prompt that started it, which reads what is typed there too.</summary>
    Borrowed,

    /// <summary>Into a console opened for this run: questions can be asked, and it waits before closing.</summary>
    Own,

    /// <summary>Nowhere a console could be had.</summary>
    None,
}

/// <summary>
/// The console a command-line run writes to.
/// </summary>
/// <remarks>
/// The program is a window, so Windows starts it with no console at all - which is what
/// keeps a black box from opening behind the window when it is double-clicked. Given options
/// it needs somewhere to write: the file or pipe its output was sent to, the Command Prompt
/// it was started from, or a console of its own.
/// </remarks>
internal static partial class ConsoleHost
{
    /// <summary>The console of the process that started this one.</summary>
    private const int ParentProcess = -1;

    private const int StandardOutput = -11;
    private const uint DiskFile = 1;
    private const uint Pipe = 3;

    /// <summary>Finds somewhere for a command-line run to write.</summary>
    /// <returns>Where its output goes.</returns>
    public static ConsoleKind Open()
    {
        // Sent to a file or a pipe - a script, a scheduled task - it writes there, and must
        // never stop to wait for an answer nobody is there to give.
        nint output = GetStdHandle(StandardOutput);

        if (output != 0 && output != -1 && GetFileType(output) is DiskFile or Pipe)
        {
            return ConsoleKind.Redirected;
        }

        if (AttachConsole(ParentProcess))
        {
            return ConsoleKind.Borrowed;
        }

        return AllocConsole() ? ConsoleKind.Own : ConsoleKind.None;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(nint file);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
