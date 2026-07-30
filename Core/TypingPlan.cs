namespace TypeyTypey;

/// <summary>
/// How TypeyTypey turns clipboard text into keyboard input.
///
/// The numeric values are persisted. <see cref="Unicode"/> is deliberately zero so that a settings
/// file written before v1.0.6 — which has no TypingMode property at all — deserializes to the
/// behaviour that build had. See AGENTS.md §9b for why the default is not Automatic.
/// </summary>
internal enum TypingMode
{
    /// <summary>KEYEVENTF_UNICODE for every character. The behaviour of every build through v1.0.5.</summary>
    Unicode = 0,

    /// <summary>Virtual-key presses with real modifiers, as a physical keyboard would send them.</summary>
    Physical = 1,

    /// <summary>Physical where the layout allows it, Unicode for the rest, decided before typing starts.</summary>
    Automatic = 2
}

/// <summary>
/// The one place a typing mode's name and explanation are written. The tray submenu, the Settings
/// dropdown, Help and the hotkey validation messages all read from here, so the three surfaces
/// cannot drift into calling the same mode different things.
/// </summary>
internal static class TypingModeText
{
    /// <summary>Display order, which is not the persisted order. Automatic reads first because it is the recommendation.</summary>
    public static TypingMode[] InDisplayOrder => [TypingMode.Automatic, TypingMode.Unicode, TypingMode.Physical];

    public static string Label(TypingMode mode) => mode switch
    {
        TypingMode.Automatic => "Automatic",
        TypingMode.Physical => "Physical Keypresses",
        _ => "Unicode Input"
    };

    public static string Description(TypingMode mode) => mode switch
    {
        TypingMode.Automatic => "Uses physical keypresses when possible and Unicode input when necessary.",
        TypingMode.Physical => "Sends normal keyboard presses. Best for iDRAC, VNC, KVM, and browser-based remote consoles.",
        _ => "Works with most Windows applications and supports arbitrary Unicode text."
    };
}

/// <summary>
/// The modifiers a physical keystroke needs. These are the shift states <c>VkKeyScanEx</c> reports
/// in the high byte of its result, not the hotkey modifiers in <see cref="HotkeyModifiers"/>: the
/// two describe different things and must not be conflated.
/// </summary>
[Flags]
internal enum StrokeModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4
}

/// <summary>
/// One character's worth of input. Either a Unicode event carrying the character itself, or a
/// virtual-key press with the modifiers the active layout requires to produce that character.
/// </summary>
internal readonly record struct TypingStep(bool Physical, char Character, ushort VirtualKey, ushort ScanCode, StrokeModifiers Modifiers)
{
    public static TypingStep Unicode(char character) => new(false, character, 0, 0, StrokeModifiers.None);

    public static TypingStep Key(char character, ushort virtualKey, ushort scanCode, StrokeModifiers modifiers) =>
        new(true, character, virtualKey, scanCode, modifiers);
}

/// <summary>
/// Why a plan could not be built. Only <see cref="TypingMode.Physical"/> produces one: it is the
/// single mode that refuses to fall back, because silently substituting a different character into
/// a password or a command is worse than typing nothing.
/// </summary>
internal sealed record TypingPlanFailure(int Index, int CodePoint, string Description)
{
    /// <summary>
    /// Names the character by position and code point rather than by value. Clipboard-derived text
    /// never appears in a message; a code point is metadata about one character, and without it the
    /// user has no way to tell which character to remove.
    /// </summary>
    public string Message =>
        $"The current keyboard layout cannot type {Description} at position {Index + 1} (U+{CodePoint:X4}). " +
        "Nothing was typed. Switch Typing Mode to Automatic or Unicode Input for this text.";
}

/// <summary>
/// A validated sequence of keystrokes. Built in full before any input is injected, so a string that
/// cannot be typed fails with an empty destination rather than a half-entered value.
/// </summary>
internal sealed class TypingPlan
{
    public TypingPlan(IReadOnlyList<TypingStep> steps) => Steps = steps;

    public IReadOnlyList<TypingStep> Steps { get; }

    public bool UsesPhysical => Steps.Any(step => step.Physical);

    public bool UsesUnicode => Steps.Any(step => !step.Physical);
}

/// <summary>One key-down or key-up, before it becomes a Win32 <c>INPUT</c>.</summary>
internal readonly record struct KeyEvent(ushort VirtualKey, ushort ScanCode, bool KeyUp);

/// <summary>
/// Expands a physical step into the key events that produce it. Separated from the injection code
/// so the ordering — which is the part that goes wrong, and which no test could observe once it has
/// become a <c>SendInput</c> call — can be asserted directly.
/// </summary>
internal static class KeyEventSequence
{
    internal const ushort VkLShift = 0xA0;
    internal const ushort VkLControl = 0xA2;
    internal const ushort VkLMenu = 0xA4;
    internal const ushort VkRMenu = 0xA5;

    /// <summary>
    /// Modifiers down, key down, key up, modifiers up — nested, so the modifiers released are the
    /// ones pressed and nothing crosses over. <c>A</c> is Shift down, A down, A up, Shift up.
    /// </summary>
    public static IReadOnlyList<KeyEvent> ForStep(TypingStep step)
    {
        if (!step.Physical)
            return [];

        var events = new List<KeyEvent>(8);
        events.AddRange(ModifierEvents(step.Modifiers, keyUp: false));
        events.Add(new KeyEvent(step.VirtualKey, step.ScanCode, KeyUp: false));
        events.Add(new KeyEvent(step.VirtualKey, step.ScanCode, KeyUp: true));
        events.AddRange(ModifierEvents(step.Modifiers, keyUp: true));
        return events;
    }

    /// <summary>Key-ups for modifiers believed to be held, used to guarantee release on any exit path.</summary>
    public static IReadOnlyList<KeyEvent> ForRelease(StrokeModifiers held) => ModifierEvents(held, keyUp: true);

    /// <summary>
    /// Ctrl, then Alt, then Shift going down; the exact reverse coming up.
    ///
    /// Alt becomes the *right* Alt when Ctrl is also required. Ctrl+Alt is how <c>VkKeyScanEx</c>
    /// reports AltGr, and on a layout that has AltGr it is the right Alt that produces those
    /// characters — sending the left one would press a Ctrl+Alt shortcut instead.
    /// </summary>
    private static List<KeyEvent> ModifierEvents(StrokeModifiers modifiers, bool keyUp)
    {
        var keys = new List<ushort>(3);
        bool altGr = modifiers.HasFlag(StrokeModifiers.Ctrl) && modifiers.HasFlag(StrokeModifiers.Alt);
        if (modifiers.HasFlag(StrokeModifiers.Ctrl)) keys.Add(VkLControl);
        if (modifiers.HasFlag(StrokeModifiers.Alt)) keys.Add(altGr ? VkRMenu : VkLMenu);
        if (modifiers.HasFlag(StrokeModifiers.Shift)) keys.Add(VkLShift);
        if (keyUp)
            keys.Reverse();

        return keys.ConvertAll(key => new KeyEvent(key, 0, keyUp));
    }
}

/// <summary>Maps characters to keystrokes through a keyboard layout.</summary>
internal interface IKeyboardLayoutMap
{
    /// <summary>
    /// True when <paramref name="character"/> can be produced by one key press with the returned
    /// modifiers. False means the layout has no such key, or needs a shift state this cannot drive.
    /// </summary>
    bool TryMap(char character, out ushort virtualKey, out StrokeModifiers modifiers);

    /// <summary>
    /// The layout's scan code for a virtual key, or zero when it has none.
    ///
    /// Carried on every physical step because a browser-hosted console reads it. Chromium builds
    /// <c>KeyboardEvent.code</c> from the scan code in the message, not from the virtual key, and a
    /// zero there surfaces as <c>Unidentified</c> — which is one way a remote console can drop or
    /// mishandle an otherwise valid keystroke.
    /// </summary>
    ushort ScanCode(ushort virtualKey);
}

/// <summary>
/// Turns text into a plan. Deliberately free of Win32 and WinForms so every rule below is testable
/// against a fake layout, on any machine — see tools/linux-check.
/// </summary>
internal static class TypingPlanner
{
    internal const ushort VkReturn = 0x0D;
    internal const ushort VkTab = 0x09;

    /// <summary>
    /// Builds the plan, or returns the first character that defeats it. Never partially succeeds:
    /// the caller gets a complete plan or nothing.
    /// </summary>
    public static (TypingPlan? Plan, TypingPlanFailure? Failure) Plan(string text, TypingMode mode, IKeyboardLayoutMap layout)
    {
        var steps = new List<TypingStep>(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            // CR in a CRLF pair is dropped rather than typed. Sending both would enter two line
            // breaks physically, where the Unicode path happens to be forgiving.
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                continue;

            if (mode == TypingMode.Unicode)
            {
                steps.Add(TypingStep.Unicode(character));
                continue;
            }

            if (TryMapPhysical(character, layout, out TypingStep step))
            {
                steps.Add(step);
                continue;
            }

            if (mode == TypingMode.Automatic)
            {
                steps.Add(TypingStep.Unicode(character));
                continue;
            }

            return (null, Failure(text, index));
        }

        return (new TypingPlan(steps), null);
    }

    /// <summary>
    /// The control characters that are keys rather than glyphs, then whatever the layout offers.
    /// Enter and Tab are handled here because a layout map has no key for them.
    /// </summary>
    private static bool TryMapPhysical(char character, IKeyboardLayoutMap layout, out TypingStep step)
    {
        switch (character)
        {
            case '\n':
            case '\r':
                step = TypingStep.Key(character, VkReturn, layout.ScanCode(VkReturn), StrokeModifiers.None);
                return true;
            case '\t':
                step = TypingStep.Key(character, VkTab, layout.ScanCode(VkTab), StrokeModifiers.None);
                return true;
        }

        // A surrogate is half a code point. No key produces one, and pressing something for it would
        // corrupt the pair, so this is left to the Unicode path in every mode that has one.
        if (char.IsSurrogate(character) || char.IsControl(character))
        {
            step = default;
            return false;
        }

        if (layout.TryMap(character, out ushort virtualKey, out StrokeModifiers modifiers))
        {
            step = TypingStep.Key(character, virtualKey, layout.ScanCode(virtualKey), modifiers);
            return true;
        }

        step = default;
        return false;
    }

    /// <summary>
    /// Describes the offending character by category and code point. Surrogate pairs report the
    /// combined code point, so an emoji is named once rather than as two unusable halves.
    /// </summary>
    private static TypingPlanFailure Failure(string text, int index)
    {
        char character = text[index];
        int codePoint = character;
        string description = "that character";

        if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            codePoint = char.ConvertToUtf32(character, text[index + 1]);
            description = "that character";
        }
        else if (char.IsControl(character))
        {
            description = "that control character";
        }

        return new TypingPlanFailure(index, codePoint, description);
    }
}
