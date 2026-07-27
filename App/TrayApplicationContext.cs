using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeyTypey;

/// <summary>
/// Owns application lifetime and every background service: tray icon, global hotkeys, clipboard
/// monitoring, IPC commands, typing orchestration and picker lifetime.
///
/// Through v1.0.3 all of this lived on the Settings form, which made the application's core
/// behaviour depend on that window's visibility. Closing Settings recreated its handle and silently
/// destroyed the hotkey registrations. Settings is now purely a UI surface that can be opened,
/// closed and disposed without touching anything below.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ClipboardHistory _history;
    private readonly PendingSelection _pending = new();
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly HotkeyWindow _hotkeys;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripItem _pauseItem;
    private readonly Icon _icon;
    private readonly SynchronizationContext _ui;

    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private HistoryPicker? _picker;
    private CancellationTokenSource? _typingCts;
    private bool _monitorReadPending;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = AppSettings.Load();
        _history = new ClipboardHistory(_settings.MaximumHistoryEntries);
        _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _clipboardMonitor = new ClipboardMonitor();
        _clipboardMonitor.ClipboardChanged += (_, _) => CaptureClipboard();

        // An entry the user deleted, or that fell off the end of the history, must not stay armed.
        _history.Changed += (_, _) => _pending.ClearIfMissingFrom(_history.Snapshot());

        _hotkeys = new HotkeyWindow();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Type Current Clipboard", null, (_, _) => _ = TypeCurrentClipboardAsync());
        _menu.Items.Add("Clipboard History…", null, (_, _) => ShowHistoryPicker());
        _menu.Items.Add(new ToolStripSeparator());
        _pauseItem = _menu.Items.Add("Pause Clipboard Monitoring", null, (_, _) => SetMonitoring(!_settings.ClipboardMonitoringEnabled));
        _menu.Items.Add("Clear Clipboard History", null, (_, _) => _history.Clear());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        _menu.Items.Add("About", null, (_, _) => ShowAbout());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _menu.Opening += (_, _) =>
        {
            _pauseItem.Text = _settings.ClipboardMonitoringEnabled ? "Pause Clipboard Monitoring" : "Resume Clipboard Monitoring";
            ThemeManager.ApplyToMenu(_menu, _settings.Theme);
        };

        _trayIcon = new NotifyIcon { Icon = _icon, ContextMenuStrip = _menu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => ShowHistoryPicker();

        ApplyHotkeys(reportFailure: false);
        ApplyMonitoringState();
        UpdateTrayText();
    }

    /// <summary>Latest user-facing status. Never contains clipboard-derived text.</summary>
    public string Status { get; private set; } = string.Empty;

    public event EventHandler? StatusChanged;

    public AppSettings Settings => _settings;

    public ClipboardHistory History => _history;

    // ---------- commands ----------

    public void ExecuteCommand(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.Type: _ = TypeCurrentClipboardAsync(); break;
            case AppCommand.History: ShowHistoryPicker(); break;
            case AppCommand.Settings: ShowSettings(); break;
            case AppCommand.Pause: SetMonitoring(false); break;
            case AppCommand.Resume: SetMonitoring(true); break;
            case AppCommand.ClearHistory: _history.Clear(); break;
            case AppCommand.Elevate: RestartElevated(); break;
            case AppCommand.Exit: ExitApplication(); break;
        }
    }

    /// <summary>Marshals an IPC command onto the UI thread.</summary>
    public void PostCommand(AppCommand command) => _ui.Post(_ => ExecuteCommand(command), null);

    /// <summary>
    /// Elevates the running instance, as requested by <c>--admin</c>.
    ///
    /// The relay makes this work from a plain command line: whichever process owns the mutex
    /// performs the restart, so an already-running instance elevates itself rather than a second
    /// process racing it for single-instance ownership. This deliberately does not change the
    /// persisted Run as administrator setting — <c>--admin</c> elevates this run only.
    /// </summary>
    private void RestartElevated()
    {
        if (PrivilegeManager.IsElevated())
        {
            ShowBalloon("TypeyTypey is already running as administrator.");
            return;
        }

        // Drop --admin so the elevated instance does not re-enter this path and report back that it
        // is already elevated.
        string[] restartArguments = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(argument => !string.Equals(argument, CommandLine.AdminArgument, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (PrivilegeManager.TryRestartElevated(restartArguments))
        {
            ExitApplication();
            return;
        }

        SetStatus("Administrator restart was cancelled");
        ShowSafeError("Administrator restart was cancelled. TypeyTypey will continue without elevation.");
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == HotkeyManager.TypeClipboardHotkeyId)
            _ = TypeCurrentClipboardAsync();
        else if (id == HotkeyManager.HistoryHotkeyId)
            ShowHistoryPicker();
    }

    // ---------- settings ----------

    public void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(this, _icon);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    /// <summary>
    /// Applies edited settings. Returns false when the hotkeys were rejected, in which case the
    /// caller should keep the Settings window open.
    /// </summary>
    public bool ApplySettings(AppSettings updated, out string? error)
    {
        updated.Normalize();
        if (!updated.TypeClipboardHotkey.IsValid || !updated.HistoryHotkey.IsValid ||
            updated.TypeClipboardHotkey.IsSameAs(updated.HistoryHotkey))
        {
            error = "Choose two different hotkeys, each with at least one modifier key.";
            return false;
        }

        AppSettings previous = _settings;
        _settings = updated;
        if (!ApplyHotkeys(reportFailure: true))
        {
            _settings = previous;
            ApplyHotkeys(reportFailure: false);
            error = "Windows could not register one of those hotkeys. Another application may already be using it.";
            return false;
        }

        error = ApplyStartupRegistration();
        _history.SetMaximumEntries(_settings.MaximumHistoryEntries);
        ApplyMonitoringState();
        UpdateTrayText();
        _settings.Save();
        SetStatus($"Active: {_settings.TypeClipboardHotkey}; history: {_settings.HistoryHotkey}");
        return true;
    }

    /// <summary>
    /// Registers or removes the Windows startup entry. Returns a message when the operation failed,
    /// after reverting the in-memory setting so persisted state matches reality.
    /// </summary>
    private string? ApplyStartupRegistration()
    {
        try
        {
            StartupManager.SetEnabled(_settings.StartWithWindows);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException
                                      or System.Security.SecurityException or IOException)
        {
            bool wanted = _settings.StartWithWindows;
            _settings.StartWithWindows = false;
            return wanted
                ? $"TypeyTypey could not add itself to Windows startup: {ex.Message} The setting has been turned back off."
                : $"TypeyTypey could not remove its Windows startup entry: {ex.Message}";
        }
    }

    private bool ApplyHotkeys(bool reportFailure)
    {
        _hotkeys.UnregisterAll();
        bool type = _hotkeys.Register(HotkeyManager.TypeClipboardHotkeyId, _settings.TypeClipboardHotkey);
        bool history = _hotkeys.Register(HotkeyManager.HistoryHotkeyId, _settings.HistoryHotkey);
        if (type && history)
            return true;

        _hotkeys.UnregisterAll();
        if (reportFailure)
            SetStatus("One or more hotkeys are unavailable");
        return false;
    }

    private void ApplyMonitoringState()
    {
        if (_settings.ClipboardMonitoringEnabled)
            _clipboardMonitor.Start();
        else
            _clipboardMonitor.Stop();
    }

    public void SetMonitoring(bool enabled)
    {
        _settings.ClipboardMonitoringEnabled = enabled;
        ApplyMonitoringState();
        UpdateTrayText();
        _settings.Save();
        _settingsForm?.RefreshFromSettings();
        SetStatus(enabled ? "Clipboard monitoring resumed" : "Clipboard monitoring paused");
    }

    /// <summary>
    /// Tray tooltip doubles as the always-available elevation indicator, so the user can confirm
    /// administrator mode took effect without opening Settings. NotifyIcon.Text caps at 63
    /// characters; the longest form here is well inside that.
    /// </summary>
    private void UpdateTrayText()
    {
        var state = new List<string>(2);
        if (!_settings.ClipboardMonitoringEnabled) state.Add("Paused");
        if (PrivilegeManager.IsElevated()) state.Add("Administrator");
        _trayIcon.Text = state.Count == 0 ? "TypeyTypey" : $"TypeyTypey ({string.Join(", ", state)})";
    }

    public void ShowBalloon(string message) => _trayIcon.ShowBalloonTip(1500, "TypeyTypey", message, ToolTipIcon.Info);

    private void ShowAbout() => MessageBox.Show(
        $"TypeyTypey v{VersionInfo.Display}\nA quiet native Windows typing utility.\n\nMIT License",
        "About TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ---------- clipboard ----------

    private async void CaptureClipboard()
    {
        if (!_settings.ClipboardMonitoringEnabled || _monitorReadPending) return;
        _monitorReadPending = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        // Copying new text is a more recent instruction than the picker selection,
                        // so it takes the type hotkey back to the live clipboard.
                        _pending.Clear();
                        _history.Add(Clipboard.GetText(TextDataFormat.UnicodeText));
                    }
                    return;
                }
                catch (ExternalException) when (attempt < 2)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }
                catch (ExternalException) { return; }
            }
        }
        finally { _monitorReadPending = false; }
    }

    private string? ReadClipboardText()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetStatus("Clipboard has no text");
                return null;
            }
            return Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch (ExternalException)
        {
            SetStatus("Clipboard is temporarily busy");
            return null;
        }
    }

    // ---------- picker ----------

    public void ShowHistoryPicker()
    {
        if (_typingCts is not null)
            return;

        if (_picker is { IsDisposed: false })
        {
            _picker.Activate();
            return;
        }

        using var picker = new HistoryPicker(_history, _settings.Theme);
        _picker = picker;
        try
        {
            DialogResult result = picker.ShowDialog();
            if (result == DialogResult.OK)
                ArmSelection(picker.SelectedText);
        }
        finally { _picker = null; }
    }

    /// <summary>
    /// Makes a picked history entry the text the type hotkey will send, and says so on screen.
    /// Choosing an entry no longer types it: the user picks the text here and picks the destination
    /// afterwards, which is the order the two hotkeys imply.
    /// </summary>
    private void ArmSelection(string? text)
    {
        if (!_pending.Set(text))
            return;

        string message = PendingSelection.Describe(_pending.Length, _settings.TypeClipboardHotkey.ToString());
        SetStatus(message);
        ShowBalloon(message);
    }

    // ---------- typing ----------

    /// <summary>
    /// Types the picked history entry when one is armed, otherwise the current clipboard. The armed
    /// entry survives typing, so the same value can be sent to several fields.
    /// </summary>
    public async Task TypeCurrentClipboardAsync()
    {
        string? text = _pending.Resolve(ReadClipboardText);
        if (text is not null)
            await StartTypingAsync(text, WindowFocus.GetForegroundWindow());
    }

    private async Task StartTypingAsync(string text, IntPtr destination)
    {
        if (_typingCts is not null || string.IsNullOrEmpty(text))
            return;

        _typingCts = new CancellationTokenSource();
        try
        {
            SetStatus("Typing…");
            if (!await WindowFocus.RestoreAsync(destination, _typingCts.Token))
            {
                SetStatus("Destination window is unavailable");
                return;
            }
            await InputTyper.WaitForModifierReleaseAsync(_typingCts.Token);
            await Task.Delay(_settings.InitialDelayMs, _typingCts.Token);
            // Another application may have taken focus during the delay.
            if (!await WindowFocus.RestoreAsync(destination, _typingCts.Token))
            {
                SetStatus("Destination window is unavailable");
                return;
            }
            await InputTyper.TypeTextAsync(text, _settings.CharacterDelayMs, _typingCts.Token);
            if (_settings.ClearClipboardAfterTyping)
            {
                try { Clipboard.Clear(); } catch (ExternalException) { }
            }
            SetStatus("Typing complete");
        }
        catch (OperationCanceledException) { SetStatus("Typing cancelled"); }
        catch (InputInjectionException ex)
        {
            SetStatus(ex.WindowsErrorCode > 0 ? $"Typing failed (Windows error {ex.WindowsErrorCode})" : "Typing failed");
            ShowSafeError(InputTyper.DescribeFailure(ex));
        }
        catch (Exception)
        {
            SetStatus("Typing failed");
            ShowSafeError("TypeyTypey encountered an unexpected error while sending keyboard input. No clipboard text was shown or recorded.");
        }
        finally
        {
            _typingCts.Dispose();
            _typingCts = null;
        }
    }

    internal static void ShowSafeError(string message) =>
        MessageBox.Show(message, "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- lifetime ----------

    public void ExitApplication()
    {
        _typingCts?.Cancel();
        _history.Clear();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _typingCts?.Cancel();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _menu.Dispose();
            _hotkeys.Dispose();
            _clipboardMonitor.Dispose();
            _settingsForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}
