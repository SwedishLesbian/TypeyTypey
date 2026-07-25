namespace TypeyTypey;

/// <summary>
/// Owns the global hotkey registrations on a dedicated, permanently-lived window.
///
/// A hotkey registration belongs to an HWND, not to a process, and dies silently when that window
/// is destroyed. Through v1.0.3 the registrations lived on the Settings form, whose handle WinForms
/// recreates whenever <c>ShowInTaskbar</c> changes — which <c>HideToTray</c> did on every close.
/// Closing Settings therefore killed both hotkeys with no error. This window is created once and is
/// never shown, resized, or reparented, so its handle is stable for the life of the process.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly HashSet<int> _registered = [];
    private bool _disposed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public event Action<int>? HotkeyPressed;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyManager.WmHotkey)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Registers one hotkey, replacing any previous registration for the same id.</summary>
    public bool Register(int id, HotkeyBinding binding)
    {
        Unregister(id);
        if (!HotkeyManager.RegisterHotKey(Handle, id, binding.ToModifiers(), (uint)binding.Key))
            return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            HotkeyManager.UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered.ToArray())
            HotkeyManager.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        DestroyHandle();
    }
}
