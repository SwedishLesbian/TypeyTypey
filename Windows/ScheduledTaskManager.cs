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

        RegistrationOutcome outcome = Register(BuildTaskXml(mode, executable, CurrentUserSid()), SubmitTaskXml);
        if (!outcome.Succeeded)
            return (false, $"Windows refused to create the TypeyTypey scheduled task.\n\n{outcome.Output}");

        string startupNote = DisableStartupEntry();
        if (outcome.RemovedNodes.Count > 0)
            startupNote = $"\n\nThis version of Windows rejected {string.Join(" and ", outcome.RemovedNodes)}; " +
                          $"the task was created without {(outcome.RemovedNodes.Count == 1 ? "it" : "them")}, " +
                          "which does not change how it runs." + startupNote;
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

    /// <summary>What a registration attempt ended up doing.</summary>
    internal readonly record struct RegistrationOutcome(bool Succeeded, string Output, IReadOnlyList<string> RemovedNodes);

    /// <summary>
    /// Hands one task definition to the scheduler and reports what it said. The seam that lets the
    /// retry logic below be tested without a scheduler, an administrator token, or a particular
    /// Windows version.
    /// </summary>
    internal delegate (int ExitCode, string Output) SubmitTaskDefinition(string xml);

    /// <summary>
    /// Registers <paramref name="xml"/>, retrying without an optional element when the scheduler
    /// rejects the document for containing one it does not know.
    ///
    /// Bounded by <see cref="TaskXmlCompatibility.MaximumAttempts"/> and by the allowlist: an
    /// element that is not removable ends the loop with the scheduler's own error preserved, rather
    /// than being stripped to make the command succeed. The same node is never removed twice —
    /// removal either changes the document or the loop stops — so it cannot spin.
    /// </summary>
    internal static RegistrationOutcome Register(string xml, SubmitTaskDefinition submit)
    {
        var removed = new List<string>();
        string current = xml;

        for (int attempt = 1; ; attempt++)
        {
            (int exitCode, string output) = submit(current);
            if (exitCode == 0)
                return new RegistrationOutcome(true, output, removed);

            if (attempt >= TaskXmlCompatibility.MaximumAttempts)
                return new RegistrationOutcome(false, output, removed);

            string? node = TaskXmlCompatibility.UnexpectedNodeName(output, current);
            if (!TaskXmlCompatibility.TryRemoveNode(current, node, out string reduced))
                return new RegistrationOutcome(false, output, removed);

            removed.Add(node!);
            current = reduced;
        }
    }

    /// <summary>
    /// Writes the definition to a temporary file and runs <c>schtasks</c> against it. The file is
    /// deleted on every path, including the failure that triggers a retry, so a rejected attempt
    /// leaves nothing behind.
    /// </summary>
    private static (int ExitCode, string Output) SubmitTaskXml(string xml)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"TypeyTypey-task-{Environment.ProcessId}-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks only accepts a Unicode task definition; a UTF-8 file is rejected outright.
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            return RunSchtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
        }
        finally
        {
            try { File.Delete(xmlPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
        //
        // Element order is not cosmetic. Task Scheduler validates against its XSD and rejects the
        // whole definition when a sequence is out of order: logonTriggerType extends the base
        // trigger with UserId then Delay, so Enabled, UserId, Delay — not Delay, UserId.
        string trigger = system
            ? "<BootTrigger><Enabled>true</Enabled><Delay>PT30S</Delay></BootTrigger>"
            : $"<LogonTrigger><Enabled>true</Enabled><UserId>{Escape(userSid)}</UserId><Delay>PT10S</Delay></LogonTrigger>";

        string principal = system
            ? "<UserId>S-1-5-18</UserId><LogonType>ServiceAccount</LogonType>"
            : $"<UserId>{Escape(userSid)}</UserId><LogonType>InteractiveToken</LogonType>";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <URI>\{TaskName}</URI>
                <Description>Starts TypeyTypey, which types clipboard text as simulated keyboard input.</Description>
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
