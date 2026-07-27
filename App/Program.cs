namespace TypeyTypey;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!CommandLine.TryParse(arguments, out AppCommand command, out bool elevatedRestart))
        {
            MessageBox.Show("Supported commands: --type, --history, --settings, --pause, --resume, --clear-history, --admin, --exit", "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
