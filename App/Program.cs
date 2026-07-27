namespace TypeyTypey;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!CommandLine.TryParse(arguments, out AppCommand command, out bool elevatedRestart, out AdminTaskMode adminTask))
        {
            MessageBox.Show(CommandLine.Usage, "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // --admintask administers Task Scheduler and exits. It deliberately runs before the
        // single-instance handshake so it neither disturbs a running instance nor becomes one.
        if (adminTask != AdminTaskMode.None)
        {
            (bool succeeded, string message) = ScheduledTaskManager.Execute(adminTask);
            if (message.Length > 0)
                MessageBox.Show(message, "TypeyTypey", MessageBoxButtons.OK, succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return;
        }

        AppSettings settings = AppSettings.Load();
        if (settings.RunAsAdministrator && !PrivilegeManager.IsElevated())
        {
            if (PrivilegeManager.TryRestartElevated(arguments))
                return;

            // A declined UAC request must not cause an elevation prompt on every future launch.
            settings.RunAsAdministrator = false;
            settings.Save();
        }

        using var singleInstance = new SingleInstanceManager();
        if (!singleInstance.IsPrimaryInstance)
        {
            if (elevatedRestart && singleInstance.WaitForPrimaryInstance(TimeSpan.FromSeconds(5)))
            {
                // The non-elevated parent has exited and released the mutex; continue as the sole elevated instance.
            }
            else
            {
                SingleInstanceManager.SendCommand(command == AppCommand.None ? AppCommand.Settings : command);
                return;
            }
        }

        ApplicationConfiguration.Initialize();
        // A WindowsFormsSynchronizationContext must exist before the context marshals IPC commands.
        WindowsFormsSynchronizationContext.AutoInstall = true;
        using var context = new TrayApplicationContext();
        singleInstance.CommandReceived += context.PostCommand;
        singleInstance.Listen();
        if (command != AppCommand.None)
            context.PostCommand(command);
        Application.Run(context);
    }
}
