using System.Runtime.InteropServices;

namespace TypeyTypey;

internal static class WindowFocus
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static async Task<bool> RestoreAsync(IntPtr window, CancellationToken cancellationToken)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return false;
        if (GetForegroundWindow() == window)
            return true;
        SetForegroundWindow(window);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetForegroundWindow() == window)
                return true;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }
}
