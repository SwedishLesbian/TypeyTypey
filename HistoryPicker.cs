namespace TypeyTypey;

internal sealed class HistoryPicker : Form
{
    private readonly ClipboardHistory _history;
    private readonly AppTheme _theme;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search clipboard history…" };
    private readonly ListBox _entries = new() { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = nameof(HistoryEntry.Display) };
    private List<HistoryEntry> _filtered = [];

    public HistoryPicker(ClipboardHistory history, AppTheme theme)
    {
        _history = history;
        _theme = theme;

        // The scale pass must be deferred to ResumeLayout; see the comment in SettingsForm. Without
        // it the picker renders at 1/scale on a high-DPI display.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Clipboard History";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = WindowPlacement.DefaultPickerClientSize;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Controls.Add(_entries);
        Controls.Add(_search);
        ResumeLayout(performLayout: true);

        _search.TextChanged += (_, _) => RefreshEntries();
        _entries.DoubleClick += (_, _) => ChooseSelected();
        _entries.KeyDown += OnEntryKeyDown;
        _search.KeyDown += OnSearchKeyDown;
        _history.Changed += OnHistoryChanged;
        Shown += (_, _) =>
        {
            RefreshEntries();
            // The picker opens from a global hotkey while another application owns the foreground,
            // so an explicit foreground request is needed in addition to TopMost.
            WindowFocus.ForceForeground(Handle);
            Activate();
            _search.Focus();
        };

        ThemeManager.Apply(this, _theme);
    }

    public string? SelectedText { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _history.Changed -= OnHistoryChanged;
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == ThemeManager.WmSettingChange && _theme == AppTheme.System)
            ThemeManager.Apply(this, _theme);
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
            BeginInvoke(RefreshEntries);
    }

    private void RefreshEntries()
    {
        string query = _search.Text.Trim();
        _filtered = _history.Snapshot()
            .Where(text => string.IsNullOrEmpty(query) || text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(text => new HistoryEntry(text))
            .ToList();
        _entries.DataSource = _filtered;
        if (_filtered.Count > 0)
            _entries.SelectedIndex = 0;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _entries.Items.Count > 0)
        {
            _entries.Focus();
            _entries.SelectedIndex = Math.Min(_entries.SelectedIndex + 1, _entries.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            ChooseSelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ChooseSelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete && _entries.SelectedItem is HistoryEntry entry)
        {
            _history.Remove(entry.Text);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void ChooseSelected()
    {
        if (_entries.SelectedItem is not HistoryEntry entry)
            return;
        SelectedText = entry.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class HistoryEntry
    {
        public HistoryEntry(string text) => Text = text;
        public string Text { get; }
        public string Display => Text.Replace("\r", " ").Replace("\n", " ⏎ ");
    }
}
