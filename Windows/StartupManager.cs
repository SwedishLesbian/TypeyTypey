using Microsoft.Win32;

namespace TypeyTypey;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TypeyTypey";

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
            throw new InvalidOperationException("Windows startup settings are unavailable.");

        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
