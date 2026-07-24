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
    public static bool TryParse(string[] arguments, out AppCommand command)
    {
        command = arguments.Length == 0 ? AppCommand.None : arguments[0].ToLowerInvariant() switch
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
        return arguments.Length <= 1 && (arguments.Length == 0 || command != AppCommand.None);
    }
}
