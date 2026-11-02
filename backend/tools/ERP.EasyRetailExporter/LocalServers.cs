using System.Security;
using Microsoft.Win32;

namespace ERP.EasyRetailExporter;

/// <summary>The SQL Server instances installed on this computer, as the names to connect to them by.</summary>
/// <remarks>
/// Read from the registry, where SQL Server lists its instances: at once, and without the
/// network broadcast a SQL Browser search makes, which a firewall answers with a prompt.
/// EasyRetail runs on an instance installed on the shop's own computer, so the one it uses
/// is almost always among these.
/// </remarks>
internal static class LocalServers
{
    private const string InstancesKey = @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL";

    /// <summary>The instance a server installs unnamed, reached by the computer's name alone.</summary>
    private const string DefaultInstance = "MSSQLSERVER";

    /// <summary>Lists the instances, as <c>.</c> for the default one and <c>.\NAME</c> for the rest.</summary>
    /// <returns>The names, in order; none where SQL Server is not installed here.</returns>
    public static IReadOnlyList<string> Find()
    {
        SortedSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        // A 32-bit SQL Server lists itself in the 32-bit view of the registry.
        foreach (RegistryView view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            try
            {
                using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? instances = machine.OpenSubKey(InstancesKey);

                foreach (string instance in instances?.GetValueNames() ?? [])
                {
                    found.Add(instance.Equals(DefaultInstance, StringComparison.OrdinalIgnoreCase) ? "." : $@".\{instance}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A view that cannot be read lists nothing; the server can still be typed in.
            }
        }

        return [.. found];
    }
}
