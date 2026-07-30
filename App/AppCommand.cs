namespace TypeyTypey;

internal enum AppCommand
{
    None,
    Type,
    History,
    Settings,
    Pause,
    Resume,
    ClearHistory,
    Exit,
    Elevate,

    /// <summary>
    /// Shows the help window and exits. Unlike the others this is never relayed to a running
    /// instance: help must work whether or not TypeyTypey is already running.
    /// </summary>
    Help
}

/// <summary>One command line option and what it does, as shown in the help window.</summary>
internal readonly record struct CommandLineOption(string Flag, string Summary);

/// <summary>
/// What <c>--admintask</c> should do to the Windows scheduled task. Unlike every other command line
/// option this is not relayed to the running instance: it is a one-shot administrative operation
/// performed by the process that was launched, which then exits.
/// </summary>
internal enum AdminTaskMode
{
    None,

    /// <summary>Start at sign-in, as the current user, with the highest privileges available.</summary>
    Logon,

    /// <summary>Start at system boot as LocalSystem. See the warning in <c>ScheduledTaskManager</c>.</summary>
    System,

    /// <summary>Remove whichever task was installed.</summary>
    Remove
}

internal static class CommandLine
{
    internal const string ElevatedRestartArgument = "--elevated-restart";
    internal const string AdminArgument = "--admin";
    internal const string AdminTaskArgument = "--admintask";

    /// <summary>
    /// Every supported option, in the order the help window lists them. This is the single source
    /// of truth: <see cref="Usage"/> is derived from it, so a new option cannot be documented in one
    /// place and missing from the other.
    /// </summary>
    internal static IReadOnlyList<CommandLineOption> Options { get; } =
    [
        new("--type", "Type the clipboard, or the armed history entry, into the active window."),
        new("--history", "Open the clipboard history picker."),
        new("--settings", "Open the settings window."),
        new("--pause", "Stop recording copied text in the history."),
        new("--resume", "Start recording copied text again."),
        new("--clear-history", "Discard every history entry held in memory."),
        new("--admin", "Restart the running instance with administrator rights. Raises a UAC prompt."),
        new("--admintask", "Create a scheduled task that starts TypeyTypey elevated at sign-in, with no UAC prompt."),
        new("--admintask system", "Create the boot-time task that runs as LocalSystem. See the warning it prints."),
        new("--admintask off", "Remove whichever scheduled task was created. 'remove' also works."),
        new("--help", "Show this window."),
        new("--exit", "Close the running instance.")
    ];

    internal static string Usage =>
        "Supported commands: " + string.Join(", ", Options.Select(option => option.Flag));

    public static bool TryParse(string[] arguments, out AppCommand command)
    {
        return TryParse(arguments, out command, out _, out _);
    }

    public static bool TryParse(string[] arguments, out AppCommand command, out bool elevatedRestart)
    {
        return TryParse(arguments, out command, out elevatedRestart, out _);
    }

    public static bool TryParse(string[] arguments, out AppCommand command, out bool elevatedRestart, out AdminTaskMode adminTask)
    {
        command = AppCommand.None;
        adminTask = AdminTaskMode.None;

        elevatedRestart = arguments.Count(argument => string.Equals(argument, ElevatedRestartArgument, StringComparison.OrdinalIgnoreCase)) == 1;
        string[] visibleArguments = arguments.Where(argument => !string.Equals(argument, ElevatedRestartArgument, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visibleArguments.Length + (elevatedRestart ? 1 : 0) != arguments.Length)
            return false;

        // --admintask is the one option that takes a value, so it is parsed before the single-token
        // commands rather than widening their rule that a command line carries exactly one token.
        if (visibleArguments.Length > 0 && string.Equals(visibleArguments[0], AdminTaskArgument, StringComparison.OrdinalIgnoreCase))
        {
            adminTask = visibleArguments.Length switch
            {
                1 => AdminTaskMode.Logon,
                2 => visibleArguments[1].ToLowerInvariant() switch
                {
                    "system" => AdminTaskMode.System,
                    "off" or "remove" => AdminTaskMode.Remove,
                    _ => AdminTaskMode.None
                },
                _ => AdminTaskMode.None
            };
            return adminTask != AdminTaskMode.None;
        }

        command = visibleArguments.Length == 0 ? AppCommand.None : visibleArguments[0].ToLowerInvariant() switch
        {
            "--type" => AppCommand.Type,
            "--history" => AppCommand.History,
            "--settings" => AppCommand.Settings,
            "--pause" => AppCommand.Pause,
            "--resume" => AppCommand.Resume,
            "--clear-history" => AppCommand.ClearHistory,
            "--exit" => AppCommand.Exit,
            "--help" or "-h" or "/?" => AppCommand.Help,
            AdminArgument => AppCommand.Elevate,
            _ => AppCommand.None
        };
        return visibleArguments.Length <= 1 && (visibleArguments.Length == 0 || command != AppCommand.None);
    }

    /// <summary>The arguments that reproduce <paramref name="mode"/>, for the elevated relaunch.</summary>
    public static string[] Arguments(AdminTaskMode mode) => mode switch
    {
        AdminTaskMode.Logon => [AdminTaskArgument],
        AdminTaskMode.System => [AdminTaskArgument, "system"],
        AdminTaskMode.Remove => [AdminTaskArgument, "off"],
        _ => []
    };
}
