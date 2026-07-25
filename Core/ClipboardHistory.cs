namespace TypeyTypey;

internal sealed class ClipboardHistory
{
    private readonly object _gate = new();
    private readonly List<string> _entries = [];
    private int _maximumEntries;

    public ClipboardHistory(int maximumEntries) => _maximumEntries = Math.Clamp(maximumEntries, 1, 500);

    public event EventHandler? Changed;

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    public void SetMaximumEntries(int maximumEntries)
    {
        bool changed;
        lock (_gate)
        {
            _maximumEntries = Math.Clamp(maximumEntries, 1, 500);
            changed = TrimUnsafe();
        }
        if (changed) OnChanged();
    }

    public void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_gate)
        {
            _entries.RemoveAll(entry => string.Equals(entry, text, StringComparison.Ordinal));
            _entries.Insert(0, text);
            TrimUnsafe();
        }
        OnChanged();
    }

    public bool Remove(string text)
    {
        bool removed;
        lock (_gate)
            removed = _entries.Remove(text);
        if (removed) OnChanged();
        return removed;
    }

    public void Clear()
    {
        bool changed;
        lock (_gate)
        {
            changed = _entries.Count > 0;
            _entries.Clear();
        }
        if (changed) OnChanged();
    }

    private bool TrimUnsafe()
    {
        if (_entries.Count <= _maximumEntries)
            return false;
        _entries.RemoveRange(_maximumEntries, _entries.Count - _maximumEntries);
        return true;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
