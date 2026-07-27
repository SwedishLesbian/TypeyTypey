namespace TypeyTypey;

// Stand-ins for the three collaborators ScheduledTaskManager touches that would drag WinForms or the
// registry into a Linux build. The signatures mirror the real ones, so the linked source still has
// to type-check against them; a signature change on the real type breaks this build, which is the
// intended failure. Nothing here is ever exercised by a test.

internal sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public static AppSettings Load() => new();

    public void Save() { }
}

internal static class StartupManager
{
    public static void SetEnabled(bool enabled) => _ = enabled;
}

internal static class PrivilegeManager
{
    public static bool IsElevated() => false;

    public static bool TryRestartElevated(IEnumerable<string> arguments) => arguments.Any();
}
