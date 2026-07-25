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
    Exit
}

internal static class CommandLine
{
    internal const string ElevatedRestartArgument = "--elevated-restart";

    public static bool TryParse(string[] arguments, out AppCommand command)
    {
        return TryParse(arguments, out command, out _);
    }

    public static bool TryParse(string[] arguments, out AppCommand command, out bool elevatedRestart)
    {
        elevatedRestart = arguments.Count(argument => string.Equals(argument, ElevatedRestartArgument, StringComparison.OrdinalIgnoreCase)) == 1;
        string[] visibleArguments = arguments.Where(argument => !string.Equals(argument, ElevatedRestartArgument, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visibleArguments.Length + (elevatedRestart ? 1 : 0) != arguments.Length)
        {
            command = AppCommand.None;
            return false;
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
            _ => AppCommand.None
        };
        return visibleArguments.Length <= 1 && (visibleArguments.Length == 0 || command != AppCommand.None);
    }
}
