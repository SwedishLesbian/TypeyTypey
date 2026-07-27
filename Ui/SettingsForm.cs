namespace TypeyTypey;

/// <summary>
/// The Settings window. Purely a UI surface: it edits a copy of the settings and hands it to
/// <see cref="TrayApplicationContext"/> to apply. Closing or disposing it affects nothing else.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayApplicationContext _context;
    private readonly HotkeyControls _typeHotkey = new("Type clipboard");
    private readonly HotkeyControls _historyHotkey = new("Clipboard history");
    private readonly HotkeyControls _stopHotkey = new("Stop typing");
    private readonly NumericUpDown _characterDelay = new() { Minimum = 0, Maximum = 1_000, Increment = 5, Width = 84 };
    private readonly NumericUpDown _initialDelay = new() { Minimum = 0, Maximum = 10_000, Increment = 50, Width = 84 };
    private readonly CheckBox _clearClipboard = new() { Text = "Clear the clipboard after typing", AutoSize = true };
    private readonly CheckBox _monitoringEnabled = new() { Text = "Record copied text in the history", AutoSize = true };
    private readonly NumericUpDown _maximumHistory = new() { Minimum = 1, Maximum = 500, Width = 84 };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _runAsAdministrator = new() { Text = "Run as administrator", AutoSize = true };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _save = new() { Text = "Save settings", AutoSize = true };
    private readonly Button _typeNow = new() { Text = "Type current clipboard", AutoSize = true };
    private readonly Button _clearHistory = new() { Text = "Clear history now", AutoSize = true };
    private readonly MutedLabel _status = new() { AutoSize = true };
    private readonly Label _elevationNotice = new() { AutoSize = true };

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
        if (_elevationNotice.Parent is not null && !ThemeManager.ShouldUseSystemPalette)
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

    /// <summary>
    /// A page of cards under a docked action bar, rather than a stack of group boxes. The bar is
    /// outside the scrolling region so Save stays reachable at any window size.
    /// </summary>
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(UiKit.PagePadding, UiKit.SpaceWide, UiKit.PagePadding, UiKit.PagePadding)
        };

        page.Controls.Add(Header());
        page.Controls.Add(UiKit.Eyebrow("Hotkeys"));
        page.Controls.Add(Card(
            _typeHotkey,
            _historyHotkey,
            _stopHotkey,
            UiKit.Caption("Each hotkey works anywhere in Windows. Stop typing cancels a run in progress.")));

        page.Controls.Add(UiKit.Eyebrow("Typing"));
        page.Controls.Add(Card(
            NumberRow("Character delay", _characterDelay, "ms"),
            UiKit.Caption("Pause between keystrokes. Raise it if the target drops characters."),
            NumberRow("Wait before typing", _initialDelay, "ms"),
            UiKit.Caption("Time to click into the window you want the text to land in."),
            _clearClipboard));

        page.Controls.Add(UiKit.Eyebrow("Clipboard history"));
        page.Controls.Add(Card(
            _monitoringEnabled,
            NumberRow("Keep at most", _maximumHistory, "entries"),
            UiKit.Caption("History is held in memory only and is discarded when TypeyTypey exits."),
            _clearHistory));

        page.Controls.Add(UiKit.Eyebrow("Appearance"));
        page.Controls.Add(Card(LabelledRow("Theme", _theme)));

        page.Controls.Add(UiKit.Eyebrow("Startup"));
        page.Controls.Add(Card(
            _startWithWindows,
            _runAsAdministrator,
            UiKit.Caption("Administrator mode asks for UAC approval and is needed to type into elevated windows.")));

        // Elevation is otherwise invisible, and it is the whole point of the administrator setting.
        // Shown only when elevated, so the normal case stays uncluttered.
        if (PrivilegeManager.IsElevated())
        {
            _elevationNotice.Text = "Running as administrator. TypeyTypey can type into elevated windows.";
            _elevationNotice.Margin = new Padding(0, UiKit.SpaceSection, 0, 0);
            CardPanel notice = Card(_elevationNotice);
            notice.AccentEdge = SystemColors.Highlight;
            notice.Margin = new Padding(0, UiKit.SpaceSection, 0, 0);
            page.Controls.Add(notice);
        }

        root.Controls.Add(page, 0, 0);
        root.Controls.Add(ActionBar(), 0, 1);
        Controls.Add(root);

        foreach (ThemeChoice choice in ThemeChoice.All)
            _theme.Items.Add(choice);
    }

    private static Control Header()
    {
        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        stack.Controls.Add(new Label { Text = "TypeyTypey", Font = UiKit.Title, AutoSize = true, Margin = new Padding(0) });
        stack.Controls.Add(new MutedLabel
        {
            Text = $"Version {VersionInfo.Display}",
            Font = UiKit.Helper,
            AutoSize = true,
            Margin = new Padding(0, UiKit.SpaceTight, 0, 0)
        });
        return stack;
    }

    private Control ActionBar()
    {
        var host = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(UiKit.PagePadding, UiKit.SpaceWide, UiKit.PagePadding, UiKit.SpaceWide)
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0) };
        _save.Margin = new Padding(0, 0, UiKit.Space, 0);
        buttons.Controls.AddRange([_save, _typeNow]);

        _status.Margin = new Padding(0, UiKit.Space, 0, 0);
        bar.Controls.Add(buttons);
        bar.Controls.Add(_status);

        // Added after the bar so the rule docks above it, anchoring the bar to the scrolling page.
        host.Controls.Add(bar);
        host.Controls.Add(new SeparatorPanel());
        return host;
    }

    private static CardPanel Card(params Control[] controls)
    {
        var card = new CardPanel { Dock = DockStyle.Top };
        var content = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        content.Controls.AddRange(controls);
        card.Controls.Add(content);
        return card;
    }

    private static FlowLayoutPanel NumberRow(string label, Control numeric, string suffix = "")
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, UiKit.SpaceTight) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 5, UiKit.Space, 0) });
        row.Controls.Add(numeric);
        if (!string.IsNullOrEmpty(suffix))
            row.Controls.Add(new MutedLabel { Text = suffix, AutoSize = true, Margin = new Padding(UiKit.SpaceTight, 5, 0, 0) });
        return row;
    }

    private static FlowLayoutPanel LabelledRow(string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 5, UiKit.Space, 0) });
        row.Controls.Add(control);
        return row;
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _typeHotkey.SetBinding(settings.TypeClipboardHotkey);
        _historyHotkey.SetBinding(settings.HistoryHotkey);
        _stopHotkey.SetBinding(settings.StopTypingHotkey);
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
            StopTypingHotkey = _stopHotkey.GetBinding(),
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
            Margin = new Padding(0, 0, 0, UiKit.Space);
            // A fixed label width lines the three hotkey rows up into a column without a table.
            Controls.Add(new Label { Text = label, AutoSize = false, Width = 108, Margin = new Padding(0, 5, UiKit.Space, 0) });
            foreach (CheckBox modifier in new[] { _ctrl, _alt, _shift, _win })
                modifier.Margin = new Padding(0, 4, UiKit.Space, 0);
            _key.Margin = new Padding(UiKit.SpaceTight, 1, 0, 0);
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
