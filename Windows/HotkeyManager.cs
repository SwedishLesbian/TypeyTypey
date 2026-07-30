using System.Runtime.InteropServices;

namespace TypeyTypey;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Ctrl = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

internal static class HotkeyManager
{
    public const int TypeClipboardHotkeyId = 0x5459;
    public const int HistoryHotkeyId = 0x545A;
    public const int StopTypingHotkeyId = 0x545B;

    /// <summary>
    /// One-shot typing-mode overrides. Contiguous and derived from the mode so the id round-trips:
    /// <see cref="ModeOverrideHotkeyId"/> and <see cref="ModeForOverrideId"/> are inverses, which is
    /// what lets WM_HOTKEY name a mode rather than the window that happened to have focus.
    /// </summary>
    public const int ModeOverrideHotkeyIdBase = 0x5460;

    public static int ModeOverrideHotkeyId(TypingMode mode) => ModeOverrideHotkeyIdBase + (int)mode;

    public static TypingMode? ModeForOverrideId(int id)
    {
        int offset = id - ModeOverrideHotkeyIdBase;
        return Enum.IsDefined(typeof(TypingMode), offset) ? (TypingMode)offset : null;
    }

    public const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, HotkeyModifiers fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
