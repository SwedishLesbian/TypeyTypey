using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace TypeyTypey;

internal static class PrivilegeManager
{
    public static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (PlatformNotSupportedException) { return false; }
    }

    public static bool TryRestartElevated(IEnumerable<string> arguments)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);
            if (!arguments.Any(argument => string.Equals(argument, CommandLine.ElevatedRestartArgument, StringComparison.OrdinalIgnoreCase)))
                startInfo.ArgumentList.Add(CommandLine.ElevatedRestartArgument);
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
