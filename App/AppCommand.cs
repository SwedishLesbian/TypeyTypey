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
    Elevate
}

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

    internal const string Usage =
        "Supported commands: --type, --history, --settings, --pause, --resume, --clear-history, " +
        "--admin, --admintask [system|off], --exit";

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
