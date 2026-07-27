namespace TypeyTypey;

/// <summary>
/// The clipboard-history entry the user chose in the picker, held until something supersedes it.
///
/// Through v1.0.4 choosing an entry started the typing timer immediately, which forced the user to
/// pick the destination window before opening the picker. Choosing now only *arms* the text: the
/// type hotkey uses it in place of the live clipboard, so the picker becomes "decide what to type"
/// and the hotkey stays "decide where to type it". Copying new text is the more recent instruction
/// and clears the armed entry.
///
/// This type deliberately knows nothing about WinForms so the arming rules can be tested; the
/// context owns the tray notification and the clipboard.
/// </summary>
internal sealed class PendingSelection
{
    private string? _text;

    public bool IsArmed => _text is not null;

    /// <summary>Length of the armed text, or zero. Never the text itself — see <see cref="Describe"/>.</summary>
    public int Length => _text?.Length ?? 0;

    /// <summary>
    /// Arms <paramref name="text"/>. Empty or whitespace-only text arms nothing and clears any
    /// previous entry, matching <see cref="ClipboardHistory.Add"/>, which never stores it either.
    /// </summary>
    public bool Set(string? text)
    {
        _text = string.IsNullOrWhiteSpace(text) ? null : text;
        return _text is not null;
    }

    public void Clear() => _text = null;

    /// <summary>
    /// The text the type hotkey should send: the armed entry when there is one, otherwise whatever
    /// <paramref name="readClipboard"/> returns. The clipboard is read lazily so an armed entry
    /// types even while another application is holding the clipboard open.
    /// </summary>
    public string? Resolve(Func<string?> readClipboard) => _text ?? readClipboard();

    /// <summary>Clears the armed entry when it is no longer among <paramref name="history"/>.</summary>
    public bool ClearIfMissingFrom(IReadOnlyList<string> history)
    {
        if (_text is null || history.Contains(_text, StringComparer.Ordinal))
            return false;

        _text = null;
        return true;
    }

    /// <summary>
    /// Confirmation shown when an entry is armed. It reports the length and the hotkey only:
    /// clipboard-derived text never appears in a notification, status line or error message.
    /// </summary>
    public static string Describe(int length, string hotkey) =>
        $"Selected {length} character{(length == 1 ? string.Empty : "s")}. Press {hotkey} where you want it typed.";
}
