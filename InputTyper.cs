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
        [FieldOffset(0)] public KEYBDINPUT ki;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static async Task WaitForModifierReleaseAsync(CancellationToken cancellationToken)
    {
        Keys[] modifiers = [Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin];
        while (modifiers.Any(key => (GetAsyncKeyState((int)key) & 0x8000) != 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
    }

    public static async Task TypeTextAsync(string text, int characterDelayMs, CancellationToken cancellationToken)
    {
        foreach (char character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            INPUT[] inputs = [CreateUnicodeInput(character, keyUp: false), CreateUnicodeInput(character, keyUp: true)];
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != (uint)inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected simulated keyboard input.");
            if (characterDelayMs > 0)
                await Task.Delay(characterDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0) } }
    };
}
