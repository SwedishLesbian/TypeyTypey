using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeyTypey;

internal static class InputTyper
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    // INPUT contains a union whose largest member is MOUSEINPUT. Omitting it makes
    // Marshal.SizeOf<INPUT>() 32 bytes on x64 instead of Win32's required 40 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    internal static int NativeInputSize => Marshal.SizeOf<INPUT>();

    /// <summary>How long the settle after a modifier release takes. Also used after an explicit release.</summary>
    private const int ModifierSettleMs = 30;

    private static readonly Keys[] AllModifiers =
    [
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.LWin, Keys.RWin
    ];

    /// <summary>
    /// Waits until Windows reports every modifier key released, then settles briefly. Returns false
    /// if some modifier was still reported down when <paramref name="timeout"/> elapsed.
    ///
    /// A false result is the only trustworthy answer this can give. A true result is not proof the
    /// keyboard is clear: GetAsyncKeyState returns 0 both for "the key is up" and for "UIPI will not
    /// let you read the foreground thread's input", and those are indistinguishable in the return
    /// value. See <see cref="ReleaseModifiers"/> for the guarantee that does not depend on reading.
    /// </summary>
    public static async Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (AllModifiers.Any(key => (GetAsyncKeyState((int)key) & 0x8000) != 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 >= deadline)
                return false;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
        await Task.Delay(ModifierSettleMs, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Synthesises a key-up for each modifier <paramref name="modifiers"/> names, so the target sees
    /// a clean keyboard regardless of what this process is allowed to read.
    ///
    /// This exists because <see cref="WaitForModifierReleaseAsync"/> cannot be trusted when
    /// TypeyTypey is not elevated. UIPI blocks a lower-integrity process from reading the foreground
    /// thread's input state, and the block is reported as 0 — the same value as "not pressed" — so
    /// the wait returns immediately while Ctrl and Alt are still physically held. The characters
    /// that follow then reach the target as control codes rather than text, which is why the paste
    /// arrived truncated. Only the caller's own hotkey modifiers are released: when WM_HOTKEY
    /// arrived, Windows had already confirmed those keys were down, so this is targeted rather than
    /// a blind clear of every modifier.
    ///
    /// Alt is released before Ctrl deliberately. Releasing Alt while Ctrl is still down cannot be
    /// read as the lone Alt tap that opens a window's menu bar. A hotkey whose only modifier is Alt
    /// has no such cover, but there the user is holding Alt anyway and will release it themselves.
    ///
    /// The physical keys may still be down afterwards. USB keyboards report modifiers as a state
    /// bitmap rather than repeating them, so the release holds until the user actually lets go.
    /// </summary>
    public static void ReleaseModifiers(HotkeyModifiers modifiers)
    {
        Keys[] keys = ModifierKeysToRelease(modifiers);
        if (keys.Length == 0)
            return;

        INPUT[] inputs = Array.ConvertAll(keys, key => CreateKeyUpInput(key));
        SendInput((uint)inputs.Length, inputs, NativeInputSize);
    }

    /// <summary>
    /// The keys <see cref="ReleaseModifiers"/> sends a key-up for, in order. Both sides of each
    /// modifier are released because the hotkey tells us Alt was down, not which Alt.
    /// </summary>
    internal static Keys[] ModifierKeysToRelease(HotkeyModifiers modifiers)
    {
        var keys = new List<Keys>(8);
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) keys.AddRange([Keys.LMenu, Keys.RMenu]);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) keys.AddRange([Keys.LControlKey, Keys.RControlKey]);
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) keys.AddRange([Keys.LShiftKey, Keys.RShiftKey]);
        if (modifiers.HasFlag(HotkeyModifiers.Win)) keys.AddRange([Keys.LWin, Keys.RWin]);
        return [.. keys];
    }

    /// <summary>Settles after <see cref="ReleaseModifiers"/> so the target processes the key-ups first.</summary>
    public static Task SettleAsync(CancellationToken cancellationToken) =>
        Task.Delay(ModifierSettleMs, cancellationToken);

    /// <summary>Characters to send between yields when the character delay is zero.</summary>
    private const int CharactersPerYield = 32;

    public static async Task TypeTextAsync(string text, int characterDelayMs, CancellationToken cancellationToken)
    {
        int sinceYield = 0;
        foreach (char character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            INPUT[] inputs = [CreateUnicodeInput(character, keyUp: false), CreateUnicodeInput(character, keyUp: true)];
            uint sent = SendInput((uint)inputs.Length, inputs, NativeInputSize);
            if (sent != (uint)inputs.Length)
                throw new InputInjectionException(sent, (uint)inputs.Length, sent == 0 ? Marshal.GetLastWin32Error() : 0);

            if (characterDelayMs > 0)
            {
                await Task.Delay(characterDelayMs, cancellationToken).ConfigureAwait(false);
            }
            else if (++sinceYield >= CharactersPerYield)
            {
                // A zero delay must still yield periodically. With no await at all this loop runs to
                // completion on the UI thread, the message loop stops pumping, and the stop-typing
                // hotkey cannot be delivered. Task.Delay(1) is not a substitute: the system timer
                // rounds it up to roughly 15 ms, which would make "no delay" the slowest setting.
                sinceYield = 0;
                await Task.Yield();
            }
        }
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0) } }
    };

    /// <summary>A key-up for a virtual key. Unlike the typed characters this carries no Unicode flag.</summary>
    private static INPUT CreateKeyUpInput(Keys key) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = KeyeventfKeyup } }
    };

    internal static string DescribeFailure(InputInjectionException failure) => failure.WindowsErrorCode switch
    {
        5 => "Windows denied simulated keyboard input (error 5: Access denied). Run TypeyTypey at the same elevation as the target application, or turn on Run as administrator in Settings.",
        0 when failure.SentInputs > 0 => $"Windows accepted only {failure.SentInputs} of {failure.RequestedInputs} keyboard events. Typing stopped to avoid entering an incomplete value.",
        0 => "Windows rejected simulated keyboard input without returning an error code. No clipboard text was shown or recorded.",
        _ => $"Windows rejected simulated keyboard input (error {failure.WindowsErrorCode}: {new Win32Exception(failure.WindowsErrorCode).Message}). No clipboard text was shown or recorded."
    };
}

internal sealed class InputInjectionException(uint sentInputs, uint requestedInputs, int windowsErrorCode) : Exception
{
    public uint SentInputs { get; } = sentInputs;
    public uint RequestedInputs { get; } = requestedInputs;
    public int WindowsErrorCode { get; } = windowsErrorCode;
}
