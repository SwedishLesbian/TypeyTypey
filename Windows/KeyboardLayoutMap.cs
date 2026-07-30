using System.Runtime.InteropServices;

namespace TypeyTypey;

/// <summary>
/// Maps characters to keystrokes through a Windows keyboard layout (an HKL).
///
/// Which layout matters, and the answer is not obvious. Windows tracks the active layout **per
/// thread**, so the layout that decides what a key produces belongs to the thread owning the
/// destination window, not to TypeyTypey. <see cref="ForForegroundWindow"/> reads that thread's
/// layout, and the orchestration captures it immediately before typing begins — after the initial
/// delay and after focus has been restored — because the whole point of the delay is that the user
/// changes windows during it. Capturing at hotkey time would map against whatever they were looking
/// at when they pressed it.
///
/// This maps against the **local** layout. A remote console configured for a different layout will
/// interpret the same scan codes differently, and nothing measurable on this machine can predict
/// that. Physical Keypresses therefore promises correct local key presses, not correct remote
/// characters. See README.
/// </summary>
internal sealed class KeyboardLayoutMap : IKeyboardLayoutMap
{
    private readonly IntPtr _layout;

    private KeyboardLayoutMap(IntPtr layout) => _layout = layout;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanExW(char ch, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

    private const uint MapvkVkToVsc = 0;

    /// <summary>
    /// The layout of the thread that owns the foreground window, falling back to this thread's
    /// layout when that window is gone. Call this as late as possible; the answer changes with focus.
    /// </summary>
    public static KeyboardLayoutMap ForForegroundWindow()
    {
        IntPtr window = GetForegroundWindow();
        uint thread = window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out _);
        return new KeyboardLayoutMap(GetKeyboardLayout(thread));
    }

    /// <summary>The shift states this can drive. Anything else is refused rather than approximated.</summary>
    private const int SupportedShiftStates = 0b0000_0111;

    public bool TryMap(char character, out ushort virtualKey, out StrokeModifiers modifiers)
    {
        virtualKey = 0;
        modifiers = StrokeModifiers.None;

        short result = VkKeyScanExW(character, _layout);
        if (result == -1)
            return false;

        int shiftState = (result >> 8) & 0xFF;

        // Bit 3 is Hankaku and bits 4-5 are reserved for OEM use. A character needing one of those
        // cannot be produced by pressing Shift, Ctrl and Alt, so it belongs to the Unicode path.
        if ((shiftState & ~SupportedShiftStates) != 0)
            return false;

        virtualKey = (ushort)(result & 0xFF);
        if (virtualKey == 0)
            return false;

        if ((shiftState & 1) != 0) modifiers |= StrokeModifiers.Shift;
        if ((shiftState & 2) != 0) modifiers |= StrokeModifiers.Ctrl;
        if ((shiftState & 4) != 0) modifiers |= StrokeModifiers.Alt;
        return true;
    }

    public ushort ScanCode(ushort virtualKey) => (ushort)MapVirtualKeyExW(virtualKey, MapvkVkToVsc, _layout);
}
