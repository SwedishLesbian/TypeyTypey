namespace TypeyTypey;

/// <summary>
/// The clipboard-history entry the user chose in the picker, held until something supersedes it.
///
/// Through v1.0.4 choosing an entry started the typing timer immediately, which forced the user to
/// pick the destination window before opening the picker. Choosing now only *arms* the text: the
/// type hotkey uses it in place of the live clipboard, so the picker becomes "decide what to type"
/// and the hotkey stays "decide where to type it". Copying new text is the more recent instruction
/// and supersedes the armed entry.
///
/// Superseding is decided by comparing the clipboard against what it held when the entry was armed,
/// not by listening for clipboard changes. Monitoring can be paused, and a paused monitor used to
/// leave an armed entry winning over a copy the user had plainly just made.
///
/// This type deliberately knows nothing about WinForms so the arming rules can be tested; the
/// context owns the tray notification and the clipboard itself.
/// </summary>
internal sealed class PendingSelection
{
    private string? _text;
    private string? _clipboardWhenArmed;
    private bool _clipboardWhenArmedIsKnown;

    public bool IsArmed => _text is not null;

    /// <summary>Length of the armed text, or zero. Never the text itself — see <see cref="Describe"/>.</summary>
    public int Length => _text?.Length ?? 0;

    /// <summary>
    /// Arms <paramref name="text"/>, remembering what the clipboard held at that moment.
    /// Empty or whitespace-only text arms nothing and clears any previous entry, matching
    /// <see cref="ClipboardHistory.Add"/>, which never stores it either.
    ///
    /// <paramref name="clipboardRead"/> is false when the clipboard could not be read. The entry is
    /// still armed; only the comparison in <see cref="Resolve"/> is given up, because a snapshot
    /// that was never taken cannot show a later copy.
    /// </summary>
    public bool Set(string? text, bool clipboardRead, string? clipboardText)
    {
        _text = string.IsNullOrWhiteSpace(text) ? null : text;
        _clipboardWhenArmed = clipboardText;
        _clipboardWhenArmedIsKnown = clipboardRead;
        return _text is not null;
    }

    public void Clear()
    {
        _text = null;
        _clipboardWhenArmed = null;
        _clipboardWhenArmedIsKnown = false;
    }

    /// <summary>
    /// The text the type hotkey should send, and the only place the armed entry is consumed.
    ///
    /// The armed entry wins until the clipboard is seen to have changed since it was armed, at which
    /// point the copy is the newer instruction and the entry is dropped. The entry survives typing,
    /// so the same value can be sent to several fields. A clipboard that could not be read proves
    /// nothing, so the armed entry is kept rather than guessed away.
    /// </summary>
    public string? Resolve(bool clipboardRead, string? clipboardText)
    {
        if (_text is null)
            return clipboardText;

        if (!clipboardRead || !_clipboardWhenArmedIsKnown)
            return _text;

        if (string.Equals(clipboardText, _clipboardWhenArmed, StringComparison.Ordinal))
            return _text;

        Clear();
        return clipboardText;
    }

    /// <summary>Clears the armed entry when it is no longer among <paramref name="history"/>.</summary>
    public bool ClearIfMissingFrom(IReadOnlyList<string> history)
    {
        if (_text is null || history.Contains(_text, StringComparer.Ordinal))
            return false;

        Clear();
        return true;
    }

    /// <summary>
    /// Confirmation shown when an entry is armed. It reports the length and the hotkey only:
    /// clipboard-derived text never appears in a notification, status line or error message.
    /// </summary>
    public static string Describe(int length, string hotkey) =>
        $"Selected {length} character{(length == 1 ? string.Empty : "s")}. Press {hotkey} where you want it typed.";
}
