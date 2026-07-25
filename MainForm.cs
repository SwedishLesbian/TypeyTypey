using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeyTypey;

internal sealed class MainForm : Form
{
    private readonly HotkeyControls _typeHotkey = new("Type Clipboard");
    private readonly HotkeyControls _historyHotkey = new("Clipboard History");
    private readonly NumericUpDown _characterDelay = new() { Minimum = 0, Maximum = 1_000, Increment = 5, Width = 90 };
    private readonly NumericUpDown _initialDelay = new() { Minimum = 0, Maximum = 10_000, Increment = 50, Width = 90 };
    private readonly CheckBox _clearClipboard = new() { Text = "Clear clipboard after typing", AutoSize = true };
    private readonly CheckBox _monitoringEnabled = new() { Text = "Enable clipboard monitoring", AutoSize = true };
    private readonly NumericUpDown _maximumHistory = new() { Minimum = 1, Maximum = 500, Width = 90 };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _runAsAdministrator = new() { Text = "Run as administrator (requests UAC approval)", AutoSize = true };
    private readonly Button _save = new() { Text = "Save settings", AutoSize = true };
    private readonly Button _typeNow = new() { Text = "Type current clipboard", AutoSize = true };
    private readonly Button _clearHistory = new() { Text = "Clear history", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _applicationIcon;
    private readonly ClipboardHistory _history;
    private readonly ClipboardMonitor _clipboardMonitor;
    private AppSettings _settings;
    private CancellationTokenSource? _typingCts;
    private bool _allowClose;
    private bool _typeHotkeyRegistered;
    private bool _historyHotkeyRegistered;
    private bool _monitorReadPending;

    public MainForm()
    {
        _settings = AppSettings.Load();
        _history = new ClipboardHistory(_settings.MaximumHistoryEntries);
        _clipboardMonitor = new ClipboardMonitor();
        _clipboardMonitor.ClipboardChanged += (_, _) => CaptureClipboardAsync();
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        Text = "TypeyTypey Settings";
        // Size is the outer window size, matching the intended compact-but-tall settings layout.
        Size = new Size(510, 610);
        MinimumSize = new Size(510, 610);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = _applicationIcon;
        if (_settings.WindowLeft is not null && _settings.WindowTop is not null)
            Location = new Point(_settings.WindowLeft.Value, _settings.WindowTop.Value);

        BuildLayout();
        LoadSettingsIntoControls();
        _save.Click += (_, _) => SaveAndActivate();
        _typeNow.Click += async (_, _) => await TriggerCurrentClipboardAsync();
        _clearHistory.Click += (_, _) => _history.Clear();
        FormClosing += OnFormClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Type Current Clipboard", null, async (_, _) => await TriggerCurrentClipboardAsync());
        menu.Items.Add("Clipboard History…", null, async (_, _) => await ShowHistoryPickerAsync());
        menu.Items.Add(new ToolStripSeparator());
        var pause = menu.Items.Add("Pause Clipboard Monitoring", null, (_, _) => ToggleMonitoring());
        pause.Name = "pauseMonitoring";
        menu.Items.Add("Clear Clipboard History", null, (_, _) => _history.Clear());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        menu.Opening += (_, _) => pause.Text = _settings.ClipboardMonitoringEnabled ? "Pause Clipboard Monitoring" : "Resume Clipboard Monitoring";

        _trayIcon = new NotifyIcon { Text = "TypeyTypey", Icon = _applicationIcon, ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += async (_, _) => await ShowHistoryPickerAsync();
        Shown += (_, _) =>
        {
            SaveAndActivate(showBalloon: false);
            if (_settings.ClipboardMonitoringEnabled) _clipboardMonitor.Start();
        };
    }

    public void ShowSettings()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public void ExecuteCommand(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.Type: _ = TriggerCurrentClipboardAsync(); break;
            case AppCommand.History: _ = ShowHistoryPickerAsync(); break;
            case AppCommand.Settings: ShowSettings(); break;
            case AppCommand.Pause: SetMonitoring(false); break;
            case AppCommand.Resume: SetMonitoring(true); break;
            case AppCommand.ClearHistory: _history.Clear(); break;
            case AppCommand.Exit: ExitApplication(); break;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyManager.WmHotkey)
        {
            if (m.WParam.ToInt32() == HotkeyManager.TypeClipboardHotkeyId)
                _ = TriggerCurrentClipboardAsync();
            else if (m.WParam.ToInt32() == HotkeyManager.HistoryHotkeyId)
                _ = ShowHistoryPickerAsync();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _typingCts?.Cancel();
            UnregisterHotkeys();
            _clipboardMonitor.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, AutoScroll = true };
        root.Controls.Add(Section("Hotkeys", _typeHotkey, _historyHotkey));
        root.Controls.Add(Section("Typing", NumberRow("Character delay", _characterDelay, "ms"), NumberRow("Initial delay", _initialDelay, "ms"), _clearClipboard));
        root.Controls.Add(Section("Clipboard", _monitoringEnabled, NumberRow("Maximum history entries", _maximumHistory), _clearHistory));
        root.Controls.Add(Section("Startup", _startWithWindows, _runAsAdministrator));
        root.Controls.Add(Section("About", new Label { Text = "TypeyTypey v1.0.2\nTypes clipboard-derived text with native Unicode input. Clipboard history is memory-only.", AutoSize = true }));
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.AddRange([_save, _typeNow]);
        root.Controls.Add(actions);
        root.Controls.Add(_status);
        Controls.Add(root);
    }

    private static GroupBox Section(string title, params Control[] controls)
    {
        var box = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
        var content = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        content.Controls.AddRange(controls);
        box.Controls.Add(content);
        return box;
    }

    private static FlowLayoutPanel NumberRow(string label, Control numeric, string suffix = "")
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        row.Controls.Add(numeric);
        if (!string.IsNullOrEmpty(suffix)) row.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
        return row;
    }

    private void LoadSettingsIntoControls()
    {
        _typeHotkey.SetBinding(_settings.TypeClipboardHotkey);
        _historyHotkey.SetBinding(_settings.HistoryHotkey);
        _characterDelay.Value = _settings.CharacterDelayMs;
        _initialDelay.Value = _settings.InitialDelayMs;
        _clearClipboard.Checked = _settings.ClearClipboardAfterTyping;
        _monitoringEnabled.Checked = _settings.ClipboardMonitoringEnabled;
        _maximumHistory.Value = _settings.MaximumHistoryEntries;
        _startWithWindows.Checked = _settings.StartWithWindows;
        _runAsAdministrator.Checked = _settings.RunAsAdministrator;
    }

    private void SaveAndActivate(bool showBalloon = true)
    {
        HotkeyBinding type = _typeHotkey.GetBinding();
        HotkeyBinding history = _historyHotkey.GetBinding();
        if (!type.IsValid || !history.IsValid || type.IsSameAs(history))
        {
            ShowSafeError("Choose two different hotkeys, each with at least one modifier key.");
            return;
        }

        UnregisterHotkeys();
        if (!RegisterHotkey(HotkeyManager.TypeClipboardHotkeyId, type) || !RegisterHotkey(HotkeyManager.HistoryHotkeyId, history))
        {
            UnregisterHotkeys();
            _status.Text = "One or more hotkeys are unavailable";
            ShowSafeError("Windows could not register one of those hotkeys. Another application may already be using it.");
            return;
        }
        _typeHotkeyRegistered = _historyHotkeyRegistered = true;
        _settings = new AppSettings
        {
            TypeClipboardHotkey = type,
            HistoryHotkey = history,
            CharacterDelayMs = (int)_characterDelay.Value,
            InitialDelayMs = (int)_initialDelay.Value,
            ClearClipboardAfterTyping = _clearClipboard.Checked,
            ClipboardMonitoringEnabled = _monitoringEnabled.Checked,
            MaximumHistoryEntries = (int)_maximumHistory.Value,
            StartWithWindows = _startWithWindows.Checked,
            RunAsAdministrator = _runAsAdministrator.Checked,
            WindowLeft = Location.X,
            WindowTop = Location.Y
        };
        try { StartupManager.SetEnabled(_settings.StartWithWindows); } catch { _settings.StartWithWindows = false; _startWithWindows.Checked = false; }
        _settings.Save();
        _history.SetMaximumEntries(_settings.MaximumHistoryEntries);
        if (_settings.ClipboardMonitoringEnabled) _clipboardMonitor.Start(); else _clipboardMonitor.Stop();
        _trayIcon.Text = _settings.ClipboardMonitoringEnabled ? "TypeyTypey" : "TypeyTypey (Paused)";
        if (_settings.RunAsAdministrator && !PrivilegeManager.IsElevated())
        {
            if (PrivilegeManager.TryRestartElevated(Environment.GetCommandLineArgs().Skip(1)))
            {
                _allowClose = true;
                Close();
                return;
            }

            _settings.RunAsAdministrator = false;
            _runAsAdministrator.Checked = false;
            _settings.Save();
            _status.Text = "Administrator restart was cancelled";
            ShowSafeError("Administrator restart was cancelled. TypeyTypey will continue without elevation.");
            return;
        }
        _status.Text = $"Active: {type}; history: {history}";
        if (showBalloon) _trayIcon.ShowBalloonTip(1500, "TypeyTypey", "Settings saved.", ToolTipIcon.Info);
    }

    private bool RegisterHotkey(int id, HotkeyBinding binding) => HotkeyManager.RegisterHotKey(Handle, id, binding.ToModifiers(), (uint)binding.Key);

    private void UnregisterHotkeys()
    {
        if (_typeHotkeyRegistered) HotkeyManager.UnregisterHotKey(Handle, HotkeyManager.TypeClipboardHotkeyId);
        if (_historyHotkeyRegistered) HotkeyManager.UnregisterHotKey(Handle, HotkeyManager.HistoryHotkeyId);
        _typeHotkeyRegistered = _historyHotkeyRegistered = false;
    }

    private async void CaptureClipboardAsync()
    {
        if (!_settings.ClipboardMonitoringEnabled || _monitorReadPending) return;
        _monitorReadPending = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText()) _history.Add(Clipboard.GetText(TextDataFormat.UnicodeText));
                    return;
                }
                catch (ExternalException) when (attempt < 2)
                {
                    await Task.Delay(50);
                }
                catch (ExternalException) { return; }
            }
        }
        finally { _monitorReadPending = false; }
    }

    private async Task TriggerCurrentClipboardAsync()
    {
        string? text = ReadClipboardText();
        if (text is not null) await StartTypingAsync(text, WindowFocus.GetForegroundWindow());
    }

    private async Task ShowHistoryPickerAsync()
    {
        if (_typingCts is not null) return;
        IntPtr destination = WindowFocus.GetForegroundWindow();
        using var picker = new HistoryPicker(_history);
        if (picker.ShowDialog() == DialogResult.OK && picker.SelectedText is not null)
            await StartTypingAsync(picker.SelectedText, destination);
    }

    private string? ReadClipboardText()
    {
        try
        {
            if (!Clipboard.ContainsText()) { _status.Text = "Clipboard has no text"; return null; }
            return Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch (ExternalException) { _status.Text = "Clipboard is temporarily busy"; return null; }
    }

    private async Task StartTypingAsync(string text, IntPtr destination)
    {
        if (_typingCts is not null || string.IsNullOrEmpty(text)) return;
        _typingCts = new CancellationTokenSource();
        try
        {
            _status.Text = "Typing…";
            // The picker has just owned focus; restore the target before waiting so it is never visible while input begins.
            if (!await WindowFocus.RestoreAsync(destination, _typingCts.Token)) { _status.Text = "Destination window is unavailable"; return; }
            await InputTyper.WaitForModifierReleaseAsync(_typingCts.Token);
            await Task.Delay(_settings.InitialDelayMs, _typingCts.Token);
            // Another application may have taken focus during the delay.
            if (!await WindowFocus.RestoreAsync(destination, _typingCts.Token)) { _status.Text = "Destination window is unavailable"; return; }
            await InputTyper.TypeTextAsync(text, _settings.CharacterDelayMs, _typingCts.Token);
            if (_settings.ClearClipboardAfterTyping) try { Clipboard.Clear(); } catch (ExternalException) { }
            _status.Text = "Typing complete";
        }
        catch (OperationCanceledException) { _status.Text = "Typing cancelled"; }
        catch (InputInjectionException ex)
        {
            _status.Text = ex.WindowsErrorCode > 0 ? $"Typing failed (Windows error {ex.WindowsErrorCode})" : "Typing failed";
            ShowSafeError(InputTyper.DescribeFailure(ex));
        }
        catch (Exception) { _status.Text = "Typing failed"; ShowSafeError("TypeyTypey encountered an unexpected error while sending keyboard input. No clipboard text was shown or recorded."); }
        finally { _typingCts.Dispose(); _typingCts = null; }
    }

    private void ToggleMonitoring() => SetMonitoring(!_settings.ClipboardMonitoringEnabled);
    private void SetMonitoring(bool enabled) { _monitoringEnabled.Checked = enabled; SaveAndActivate(showBalloon: false); }
    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void ExitApplication() { _allowClose = true; Close(); }
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown) return;
        e.Cancel = true;
        HideToTray();
    }
    private void ShowAbout() => MessageBox.Show("TypeyTypey v1.0.2\nA quiet native Windows typing utility.\n\nMIT License", "About TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private static void ShowSafeError(string message) => MessageBox.Show(message, "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed class HotkeyControls : FlowLayoutPanel
    {
        private readonly CheckBox _ctrl = new() { Text = "Ctrl", AutoSize = true };
        private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
        private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
        private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
        private readonly ComboBox _key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        public HotkeyControls(string label) { AutoSize = true; WrapContents = false; Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }); Controls.AddRange([_ctrl, _alt, _shift, _win, _key]); foreach (Keys key in KeysList()) _key.Items.Add(key); }
        public void SetBinding(HotkeyBinding binding) { _ctrl.Checked = binding.Ctrl; _alt.Checked = binding.Alt; _shift.Checked = binding.Shift; _win.Checked = binding.Win; _key.SelectedItem = binding.Key; if (_key.SelectedIndex < 0) _key.SelectedIndex = 0; }
        public HotkeyBinding GetBinding() => new() { Ctrl = _ctrl.Checked, Alt = _alt.Checked, Shift = _shift.Checked, Win = _win.Checked, Key = _key.SelectedItem is Keys key ? key : Keys.V };
        private static IEnumerable<Keys> KeysList() { for (Keys key = Keys.A; key <= Keys.Z; key++) yield return key; for (Keys key = Keys.D0; key <= Keys.D9; key++) yield return key; for (Keys key = Keys.F1; key <= Keys.F12; key++) yield return key; yield return Keys.Insert; yield return Keys.Home; yield return Keys.End; yield return Keys.PageUp; yield return Keys.PageDown; yield return Keys.Space; }
    }
}
