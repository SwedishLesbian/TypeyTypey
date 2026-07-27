using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace TypeyTypey;

/// <summary>
/// Installs and removes the optional Windows scheduled task behind <c>--admintask</c>.
///
/// The task is registered from XML through <c>schtasks.exe</c> rather than with
/// <c>/SC ONLOGON /RU …</c>, because only the XML form can ask for an interactive-token logon —
/// the command line form wants a stored password for an ONLOGON task, and a stored password is a
/// credential this application has no business handling.
/// </summary>
internal static class ScheduledTaskManager
{
    internal const string TaskName = "TypeyTypey";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Applies <paramref name="mode"/>, elevating first if needed. An empty message means the work
    /// was handed to an elevated relaunch, which reports its own outcome.
    /// </summary>
    public static (bool Succeeded, string Message) Execute(AdminTaskMode mode)
    {
        if (mode == AdminTaskMode.None)
            return (false, CommandLine.Usage);

        // Registering a task in the root folder is an administrative operation. Relaunching is the
        // only way to raise a UAC prompt from an already-running process.
        if (!PrivilegeManager.IsElevated())
        {
            if (PrivilegeManager.TryRestartElevated(CommandLine.Arguments(mode)))
                return (true, string.Empty);

            return (false, "Changing the TypeyTypey scheduled task needs administrator rights, and the elevation request was cancelled or refused. Nothing was changed.");
        }

        try
        {
            return mode == AdminTaskMode.Remove ? Remove() : Install(mode);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException
                                      or UnauthorizedAccessException or SecurityException)
        {
            return (false, $"TypeyTypey could not update its scheduled task: {ex.Message}");
        }
    }

    private static (bool, string) Install(AdminTaskMode mode)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application path is unavailable.");

        string xmlPath = Path.Combine(Path.GetTempPath(), $"TypeyTypey-task-{Environment.ProcessId}.xml");
        try
        {
            // schtasks only accepts a Unicode task definition; a UTF-8 file is rejected outright.
            File.WriteAllText(xmlPath, BuildTaskXml(mode, executable, CurrentUserSid()), Encoding.Unicode);
            (int exitCode, string output) = RunSchtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            if (exitCode != 0)
                return (false, $"Windows refused to create the TypeyTypey scheduled task.\n\n{output}");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        string startupNote = DisableStartupEntry();
        return mode == AdminTaskMode.System
            ? (true, $"Created the scheduled task \"{TaskName}\". TypeyTypey will start at boot as the SYSTEM account.\n\n" +
                     "SYSTEM starts in session 0, which has no desktop: the tray icon will not appear and the hotkeys will not reach your session. " +
                     $"Run \"TypeyTypey.exe {CommandLine.AdminTaskArgument}\" instead for an elevated tray icon at sign-in.{startupNote}")
            : (true, $"Created the scheduled task \"{TaskName}\". TypeyTypey will start with administrator rights when you sign in, with no UAC prompt.{startupNote}");
    }

    private static (bool, string) Remove()
    {
        (int exitCode, string output) = RunSchtasks("/Delete", "/TN", TaskName, "/F");
        return exitCode == 0
            ? (true, $"Removed the scheduled task \"{TaskName}\". TypeyTypey no longer starts automatically through Task Scheduler.")
            : (false, $"Windows refused to remove the TypeyTypey scheduled task.\n\n{output}");
    }

    /// <summary>
    /// Turns off the <c>Run</c> registry entry. Left in place it would start a second, unelevated
    /// TypeyTypey at sign-in, which the single-instance relay answers by opening Settings.
    /// </summary>
    private static string DisableStartupEntry()
    {
        try
        {
            StartupManager.SetEnabled(false);
            AppSettings settings = AppSettings.Load();
            if (!settings.StartWithWindows)
                return string.Empty;

            settings.StartWithWindows = false;
            settings.Save();
            return "\n\nStart with Windows has been turned off so the two do not both start a copy.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException
                                      or SecurityException or IOException)
        {
            return $"\n\nTurn off Start with Windows in Settings as well, or two copies will start at sign-in. ({ex.Message})";
        }
    }

    /// <summary>
    /// The task definition. Split out from registration so the trigger, principal and run level of
    /// each mode can be pinned by tests without touching Task Scheduler.
    /// </summary>
    internal static string BuildTaskXml(AdminTaskMode mode, string executablePath, string userSid)
    {
        bool system = mode == AdminTaskMode.System;

        // Both triggers wait before starting. At sign-in the notification area is not ready
        // immediately, and a tray icon added too early is silently dropped; at boot, SYSTEM starts
        // long before the services TypeyTypey reads the clipboard through.
        string trigger = system
            ? "<BootTrigger><Enabled>true</Enabled><Delay>PT30S</Delay></BootTrigger>"
            : $"<LogonTrigger><Enabled>true</Enabled><Delay>PT10S</Delay><UserId>{Escape(userSid)}</UserId></LogonTrigger>";

        string principal = system
            ? "<UserId>S-1-5-18</UserId><LogonType>ServiceAccount</LogonType>"
            : $"<UserId>{Escape(userSid)}</UserId><LogonType>InteractiveToken</LogonType>";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts TypeyTypey, which types clipboard text as simulated keyboard input.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals>
                <Principal id="Author">{principal}<RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
                <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(executablePath)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? identity.Name;
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("schtasks.exe could not be started.");

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            throw new InvalidOperationException("schtasks.exe did not finish.");
        }

        return (process.ExitCode, output.Trim());
    }
}
