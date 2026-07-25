namespace TypeyTypey;

/// <summary>
/// The Settings window. Purely a UI surface: it edits a copy of the settings and hands it to
/// <see cref="TrayApplicationContext"/> to apply. Closing or disposing it affects nothing else.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayApplicationContext _context;
    private readonly HotkeyControls _typeHotkey = new("Type Clipboard");
    private readonly HotkeyControls _historyHotkey = new("Clipboard History");
    private readonly NumericUpDown _characterDelay = new() { Minimum = 0, Maximum = 1_000, Increment = 5, Width = 90 };
    private readonly NumericUpDown _initialDelay = new() { Minimum = 0, Maximum = 10_000, Increment = 50, Width = 90 };
    private readonly CheckBox _clearClipboard = new() { Text = "Clear clipboard after typing", AutoSize = true };
    private readonly CheckBox _monitoringEnabled = new() { Text = "Enable clipboard monitoring", AutoSize = true };
    private readonly NumericUpDown _maximumHistory = new() { Minimum = 1, Maximum = 500, Width = 90 };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _runAsAdministrator = new() { Text = "Run as administrator (requests UAC approval)", AutoSize = true };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _save = new() { Text = "Save settings", AutoSize = true };
    private readonly Button _typeNow = new() { Text = "Type current clipboard", AutoSize = true };
    private readonly Button _clearHistory = new() { Text = "Clear history", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _elevationNotice = new() { AutoSize = true, Visible = false, Margin = new Padding(0, 6, 0, 0) };

    public SettingsForm(TrayApplicationContext context, Icon icon)
    {
        _context = context;

        // Autoscaling is what was missing in v1.0.3, where the process is DPI aware but the form
        // applied no scale factor, so 510x610 was consumed as raw device pixels.
        //
        // The suspend/resume bracket is load-bearing, not cosmetic. Assigning AutoScaleMode resets
        // AutoScaleDimensions to the current device value, so a scale pass that runs immediately
        // computes a factor of 1.0 and silently does nothing. Deferring the pass to ResumeLayout is
        // what the WinForms designer emits, and it is the only arrangement measured to work here.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "TypeyTypey Settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        LoadFromSettings(_context.Settings);

        // Logical units; the deferred scale pass converts these to device pixels.
        ClientSize = WindowPlacement.DefaultSettingsClientSize;
        MinimumSize = WindowPlacement.MinimumSettingsWindowSize;
        ResumeLayout(performLayout: true);

        // Placement depends on the final scaled Size, so it must follow the scale pass.
        RestoreSavedPosition();

        _save.Click += (_, _) => SaveSettings();
        _typeNow.Click += async (_, _) => await _context.TypeCurrentClipboardAsync();
        _clearHistory.Click += (_, _) => _context.History.Clear();
        _theme.SelectedIndexChanged += (_, _) => ApplyTheme(SelectedTheme());
        _context.StatusChanged += OnContextStatusChanged;

        _status.Text = _context.Status;
        ApplyTheme(_context.Settings.Theme);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _context.StatusChanged -= OnContextStatusChanged;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Repaint when Windows switches its app theme while the window is open.
        if (m.Msg == ThemeManager.WmSettingChange && SelectedTheme() == AppTheme.System)
            ApplyTheme(AppTheme.System);
    }

    /// <summary>Re-reads settings changed elsewhere (for example pause/resume from the tray menu).</summary>
    public void RefreshFromSettings()
    {
        if (!IsDisposed)
            _monitoringEnabled.Checked = _context.Settings.ClipboardMonitoringEnabled;
    }

    private void OnContextStatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(() => _status.Text = _context.Status);
        else _status.Text = _context.Status;
    }

    private AppTheme SelectedTheme() => _theme.SelectedItem is ThemeChoice choice ? choice.Value : AppTheme.System;

    private void ApplyTheme(AppTheme theme)
    {
        ThemeManager.Apply(this, theme);
        // Apply paints every label with the standard text colour, so the accent is restored after.
        // High contrast keeps the user's own palette untouched.
        if (_elevationNotice.Visible && !ThemeManager.ShouldUseSystemPalette)
            _elevationNotice.ForeColor = ThemeManager.PaletteFor(ThemeManager.Resolve(theme)).Accent;
        Refresh();
    }

    private void RestoreSavedPosition()
    {
        Rectangle[] areas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        Rectangle active = Screen.FromPoint(Cursor.Position).WorkingArea;
        Point? location = WindowPlacement.ResolveStartLocation(
            _context.Settings.WindowLeft, _context.Settings.WindowTop, Size, areas, active);

        if (location is null)
            return;

        StartPosition = FormStartPosition.Manual;
        Location = location.Value;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, AutoScroll = true };
        root.Controls.Add(Section("Hotkeys", _typeHotkey, _historyHotkey));
        root.Controls.Add(Section("Typing", NumberRow("Character delay", _characterDelay, "ms"), NumberRow("Initial delay", _initialDelay, "ms"), _clearClipboard));
        root.Controls.Add(Section("Clipboard", _monitoringEnabled, NumberRow("Maximum history entries", _maximumHistory), _clearHistory));
        root.Controls.Add(Section("Appearance", LabelledRow("Theme", _theme)));
        root.Controls.Add(Section("Startup", _startWithWindows, _runAsAdministrator));
        root.Controls.Add(Section("About", new Label
        {
            Text = $"TypeyTypey v{VersionInfo.Display}\nTypes clipboard-derived text with native Unicode input. Clipboard history is memory-only.",
            AutoSize = true
        }));

        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.AddRange([_save, _typeNow]);
        root.Controls.Add(actions);
        root.Controls.Add(_status);

        // Elevation is otherwise invisible, and it is the whole point of the administrator setting.
        // Shown only when elevated, so the normal case stays uncluttered.
        if (PrivilegeManager.IsElevated())
        {
            _elevationNotice.Text = "Running as administrator — can type into elevated applications.";
            _elevationNotice.Visible = true;
        }
        root.Controls.Add(_elevationNotice);
        Controls.Add(root);

        foreach (ThemeChoice choice in ThemeChoice.All)
            _theme.Items.Add(choice);
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
        if (!string.IsNullOrEmpty(suffix))
            row.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
        return row;
    }

    private static FlowLayoutPanel LabelledRow(string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        row.Controls.Add(control);
        return row;
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _typeHotkey.SetBinding(settings.TypeClipboardHotkey);
        _historyHotkey.SetBinding(settings.HistoryHotkey);
        _characterDelay.Value = settings.CharacterDelayMs;
        _initialDelay.Value = settings.InitialDelayMs;
        _clearClipboard.Checked = settings.ClearClipboardAfterTyping;
        _monitoringEnabled.Checked = settings.ClipboardMonitoringEnabled;
        _maximumHistory.Value = settings.MaximumHistoryEntries;
        _startWithWindows.Checked = settings.StartWithWindows;
        _runAsAdministrator.Checked = settings.RunAsAdministrator;
        _theme.SelectedItem = ThemeChoice.All.FirstOrDefault(choice => choice.Value == settings.Theme) ?? ThemeChoice.All[0];
    }

    private void SaveSettings()
    {
        var updated = new AppSettings
        {
            TypeClipboardHotkey = _typeHotkey.GetBinding(),
            HistoryHotkey = _historyHotkey.GetBinding(),
            CharacterDelayMs = (int)_characterDelay.Value,
            InitialDelayMs = (int)_initialDelay.Value,
            ClearClipboardAfterTyping = _clearClipboard.Checked,
            ClipboardMonitoringEnabled = _monitoringEnabled.Checked,
            MaximumHistoryEntries = (int)_maximumHistory.Value,
            StartWithWindows = _startWithWindows.Checked,
            RunAsAdministrator = _runAsAdministrator.Checked,
            Theme = SelectedTheme(),
            WindowLeft = Location.X,
            WindowTop = Location.Y
        };

        if (!_context.ApplySettings(updated, out string? error))
        {
            TrayApplicationContext.ShowSafeError(error ?? "Those settings could not be applied.");
            return;
        }

        // Startup registration can fail independently of the settings being valid.
        if (error is not null)
        {
            _startWithWindows.Checked = _context.Settings.StartWithWindows;
            TrayApplicationContext.ShowSafeError(error);
        }

        ApplyTheme(_context.Settings.Theme);

        if (_context.Settings.RunAsAdministrator && !PrivilegeManager.IsElevated())
        {
            if (PrivilegeManager.TryRestartElevated(Environment.GetCommandLineArgs().Skip(1)))
            {
                _context.ExitApplication();
                return;
            }

            _context.Settings.RunAsAdministrator = false;
            _runAsAdministrator.Checked = false;
            _context.Settings.Save();
            TrayApplicationContext.ShowSafeError("Administrator restart was cancelled. TypeyTypey will continue without elevation.");
            return;
        }

        _context.ShowBalloon("Settings saved.");
    }

    /// <summary>Pairs the persisted enum with its display string so the string is never stored.</summary>
    private sealed record ThemeChoice(AppTheme Value, string Label)
    {
        public static ThemeChoice[] All { get; } =
        [
            new(AppTheme.System, "System default"),
            new(AppTheme.Light, "Light"),
            new(AppTheme.Dark, "Dark")
        ];

        public override string ToString() => Label;
    }

    private sealed class HotkeyControls : FlowLayoutPanel
    {
        private readonly CheckBox _ctrl = new() { Text = "Ctrl", AutoSize = true };
        private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
        private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
        private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
        private readonly ComboBox _key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };

        public HotkeyControls(string label)
        {
            AutoSize = true;
            WrapContents = false;
            Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            Controls.AddRange([_ctrl, _alt, _shift, _win, _key]);
            foreach (Keys key in KeysList())
                _key.Items.Add(key);
        }

        public void SetBinding(HotkeyBinding binding)
        {
            _ctrl.Checked = binding.Ctrl;
            _alt.Checked = binding.Alt;
            _shift.Checked = binding.Shift;
            _win.Checked = binding.Win;
            _key.SelectedItem = binding.Key;
            if (_key.SelectedIndex < 0) _key.SelectedIndex = 0;
        }

        public HotkeyBinding GetBinding() => new()
        {
            Ctrl = _ctrl.Checked,
            Alt = _alt.Checked,
            Shift = _shift.Checked,
            Win = _win.Checked,
            Key = _key.SelectedItem is Keys key ? key : Keys.V
        };

        private static IEnumerable<Keys> KeysList()
        {
            for (Keys key = Keys.A; key <= Keys.Z; key++) yield return key;
            for (Keys key = Keys.D0; key <= Keys.D9; key++) yield return key;
            for (Keys key = Keys.F1; key <= Keys.F12; key++) yield return key;
            yield return Keys.Insert;
            yield return Keys.Home;
            yield return Keys.End;
            yield return Keys.PageUp;
            yield return Keys.PageDown;
            yield return Keys.Space;
        }
    }
}
