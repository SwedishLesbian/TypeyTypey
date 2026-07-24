namespace TypeyTypey;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!CommandLine.TryParse(Environment.GetCommandLineArgs().Skip(1).ToArray(), out AppCommand command))
        {
            MessageBox.Show("Supported commands: --type, --history, --settings, --pause, --resume, --clear-history, --exit", "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var singleInstance = new SingleInstanceManager();
        if (!singleInstance.IsPrimaryInstance)
        {
            SingleInstanceManager.SendCommand(command == AppCommand.None ? AppCommand.Settings : command);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        singleInstance.CommandReceived += requestedCommand =>
        {
            if (!form.IsDisposed)
                form.BeginInvoke(() => form.ExecuteCommand(requestedCommand));
        };
        singleInstance.Listen();
        if (command != AppCommand.None)
            form.Shown += (_, _) => form.BeginInvoke(() => form.ExecuteCommand(command));
        Application.Run(form);
    }
}
