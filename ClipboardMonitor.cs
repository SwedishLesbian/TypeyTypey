using System.Runtime.InteropServices;

namespace TypeyTypey;

internal sealed class ClipboardMonitor : NativeWindow, IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private bool _disposed;

    public ClipboardMonitor() => CreateHandle(new CreateParams());

    public event EventHandler? ClipboardChanged;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate)
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Start() => AddClipboardFormatListener(Handle);

    public void Stop() => RemoveClipboardFormatListener(Handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
