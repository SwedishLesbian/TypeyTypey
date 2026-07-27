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
            uint sent = SendInput((uint)inputs.Length, inputs, NativeInputSize);
            if (sent != (uint)inputs.Length)
                throw new InputInjectionException(sent, (uint)inputs.Length, sent == 0 ? Marshal.GetLastWin32Error() : 0);
            if (characterDelayMs > 0)
                await Task.Delay(characterDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0) } }
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
