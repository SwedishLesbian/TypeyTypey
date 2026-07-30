namespace TypeyTypey;

/// <summary>One help section: a heading and the paragraphs beneath it.</summary>
internal readonly record struct HelpTopic(string Title, IReadOnlyList<string> Paragraphs);

/// <summary>
/// The operational guidance the Help window shows, held as data rather than built into the window.
///
/// Separated for two reasons. It carries what manual testing actually found — which mode failed
/// where, and which application interfered — and that is the kind of content that quietly rots
/// unless something checks it is still there. And it is free of WinForms, so the check runs on any
/// machine rather than only where the application can be built.
/// </summary>
internal static class HelpTopics
{
    /// <summary>
    /// Written from observed behaviour, not from theory. Every claim here about iDRAC, Chrome and
    /// Windows Terminal was seen during manual testing of v1.0.5; nothing is predicted.
    /// </summary>
    public static IReadOnlyList<HelpTopic> Operational { get; } =
    [
        new("Choosing a typing mode",
        [
            "Automatic is the recommended mode and the one to try first. It sends physical key " +
            "presses where the keyboard layout allows and falls back to Unicode for anything else.",

            "Physical sends synthetic physical key presses. Use it for remote consoles and for any " +
            "application that does not accept Unicode injection.",

            "Unicode works well in ordinary Windows applications and supports any character, but a " +
            "remote console may not receive modifier state from it. In testing against an iDRAC " +
            "console, Unicode produced lowercase text because the console never saw Shift, while " +
            "Physical and Automatic both typed correctly.",

            "If typing produces unexpected characters, wrong case, or nothing at all, switch typing " +
            "modes before changing anything else. The Typing Mode submenu on the tray icon changes " +
            "it immediately."
        ]),

        new("When physical keys act as shortcuts",
        [
            "Physical mode sends real key combinations, so a character that needs a modifier is " +
            "indistinguishable from a shortcut using the same keys. The target application decides " +
            "which it is, and it may choose the shortcut.",

            "In testing, Physical mode typing into Chrome triggered tab-group behaviour rather than " +
            "entering the character.",

            "Switch to Automatic or Unicode when the target treats the generated keys as shortcuts."
        ]),

        new("Clipboard already copied before TypeyTypey started",
        [
            "Text copied before TypeyTypey started can still be typed with the type hotkey — the " +
            "clipboard is read when you press it.",

            "That text does not appear in the clipboard history picker. History is built by watching " +
            "for clipboard changes, so an entry only appears once something is copied while " +
            "TypeyTypey is running.",

            "History is held in memory only. It is empty again after TypeyTypey restarts, including " +
            "the restart that happens when it elevates itself."
        ]),

        new("Typing into elevated applications",
        [
            "A TypeyTypey running without administrator rights cannot type into an elevated " +
            "application. Windows blocks input from a lower integrity level to a higher one.",

            "An elevated TypeyTypey can type into both elevated and ordinary applications.",

            "This is Windows integrity-level behaviour and applies to every application that " +
            "simulates input, not just this one. Use Run as administrator in Settings, the --admin " +
            "option, or the scheduled task created by --admintask."
        ]),

        new("Windows Terminal and mixed elevation",
        [
            "If an elevated Windows Terminal is running, an unelevated TypeyTypey may be unable to " +
            "type into any Windows Terminal window — including tabs that look unelevated. Windows " +
            "Terminal shares one process across its windows, so the elevated instance can own the " +
            "window receiving the keystrokes.",

            "Closing every elevated Windows Terminal restores normal typing for an unelevated " +
            "TypeyTypey. Running TypeyTypey elevated also works.",

            "The classic console host, conhost.exe, behaved normally in the same test. This is an " +
            "interaction with how Windows Terminal handles elevation, not a fault found in TypeyTypey."
        ])
    ];
}
