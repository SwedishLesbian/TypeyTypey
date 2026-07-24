namespace TypeyTypey;

internal sealed class HistoryPicker : Form
{
    private readonly ClipboardHistory _history;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search clipboard history…" };
    private readonly ListBox _entries = new() { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = nameof(HistoryEntry.Display) };
    private List<HistoryEntry> _filtered = [];

    public HistoryPicker(ClipboardHistory history)
    {
        _history = history;
        Text = "Clipboard History";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 420);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Controls.Add(_entries);
        Controls.Add(_search);

        _search.TextChanged += (_, _) => RefreshEntries();
        _entries.DoubleClick += (_, _) => ChooseSelected();
        _entries.KeyDown += OnEntryKeyDown;
        _search.KeyDown += OnSearchKeyDown;
        _history.Changed += OnHistoryChanged;
        Shown += (_, _) => { RefreshEntries(); _search.Focus(); };
    }

    public string? SelectedText { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _history.Changed -= OnHistoryChanged;
        base.Dispose(disposing);
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

    private sealed class HistoryEntry(string text)
    {
        public string Text { get; } = text;
        public string Display => text.Replace("\r", " ").Replace("\n", " ⏎ ");
    }
}
