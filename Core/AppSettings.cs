using System.Text.Json;

namespace TypeyTypey;

internal sealed class AppSettings
{
    public HotkeyBinding TypeClipboardHotkey { get; set; } = new();
    public HotkeyBinding HistoryHotkey { get; set; } = new() { Shift = true };

    /// <summary>
    /// Cancels a typing run in progress. Absent from settings files written before v1.0.5, where the
    /// property initialiser supplies the default rather than leaving a null binding behind.
    /// </summary>
    public HotkeyBinding StopTypingHotkey { get; set; } = new() { Key = Keys.X };
    /// <summary>
    /// How text is turned into keystrokes. Absent from settings files written before v1.0.6, which
    /// deserialize to <see cref="TypingMode.Unicode"/> — the behaviour those builds had — so an
    /// upgrade never silently changes how an existing user's typing reaches its target.
    /// </summary>
    public TypingMode TypingMode { get; set; } = TypingMode.Unicode;

    /// <summary>Opt-in. While false the three bindings below are neither validated nor registered.</summary>
    public bool TypingModeOverridesEnabled { get; set; }

    /// <summary>
    /// One-shot hotkeys that type in a named mode without changing <see cref="TypingMode"/>. Null
    /// means unassigned, which is the default for all three and is always allowed.
    /// </summary>
    public HotkeyBinding? AutomaticModeHotkey { get; set; }

    public HotkeyBinding? UnicodeModeHotkey { get; set; }

    public HotkeyBinding? PhysicalModeHotkey { get; set; }

    public int CharacterDelayMs { get; set; } = 15;
    public int InitialDelayMs { get; set; } = 500;
    public bool ClearClipboardAfterTyping { get; set; }
    public bool ClipboardMonitoringEnabled { get; set; } = true;
    public int MaximumHistoryEntries { get; set; } = 50;
    public bool StartWithWindows { get; set; }
    public bool RunAsAdministrator { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public int? WindowLeft { get; set; }
    public int? WindowTop { get; set; }

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeyTypey");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Normalize()
    {
        TypeClipboardHotkey ??= new HotkeyBinding();
        HistoryHotkey ??= new HotkeyBinding { Shift = true };
        StopTypingHotkey ??= new HotkeyBinding { Key = Keys.X };
        CharacterDelayMs = Math.Clamp(CharacterDelayMs, 0, 1_000);
        InitialDelayMs = Math.Clamp(InitialDelayMs, 0, 10_000);
        MaximumHistoryEntries = Math.Clamp(MaximumHistoryEntries, 1, 500);
        // A settings file written by a newer build, or hand-edited, must not leave an undefined
        // theme in play. System.Text.Json will happily deserialize any integer into the enum.
        if (!Enum.IsDefined(Theme))
            Theme = AppTheme.System;
        if (!Enum.IsDefined(TypingMode))
            TypingMode = TypingMode.Unicode;
    }

    /// <summary>
    /// The override hotkeys that are actually in play, paired with the mode each one types in.
    /// Empty when the feature is off or nothing is assigned, which is the default.
    /// </summary>
    public IEnumerable<(TypingMode Mode, HotkeyBinding Binding)> AssignedModeOverrides()
    {
        if (!TypingModeOverridesEnabled)
            yield break;

        if (AutomaticModeHotkey is not null) yield return (TypingMode.Automatic, AutomaticModeHotkey);
        if (UnicodeModeHotkey is not null) yield return (TypingMode.Unicode, UnicodeModeHotkey);
        if (PhysicalModeHotkey is not null) yield return (TypingMode.Physical, PhysicalModeHotkey);
    }

    /// <summary>
    /// Checks the three global hotkeys as a set and returns why they cannot be used, or null when
    /// they are fine. Lives here rather than on the window that edits them so the rule is one thing
    /// in one place, and so it can be tested without constructing a form.
    /// </summary>
    public string? ValidateHotkeys()
    {
        var all = new List<(string Name, HotkeyBinding Binding)>
        {
            ("Type clipboard", TypeClipboardHotkey),
            ("Clipboard history", HistoryHotkey),
            ("Stop typing", StopTypingHotkey)
        };

        // Unassigned overrides are not an error — they are the default. Only what the user actually
        // bound takes part in the uniqueness check, and only while the feature is switched on.
        foreach ((TypingMode mode, HotkeyBinding binding) in AssignedModeOverrides())
            all.Add(($"{TypingModeText.Label(mode)} override", binding));

        foreach ((string name, HotkeyBinding binding) in all)
        {
            if (!binding.IsValid)
                return $"The {name.ToLowerInvariant()} hotkey needs at least one modifier key.";
        }

        for (int first = 0; first < all.Count; first++)
        {
            for (int second = first + 1; second < all.Count; second++)
            {
                if (all[first].Binding.IsSameAs(all[second].Binding))
                    return $"{all[first].Name} and {all[second].Name.ToLowerInvariant()} are set to the same combination. Give each one a different key.";
            }
        }

        return null;
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(SettingsDirectory);
        string temporaryPath = Path.Combine(SettingsDirectory, "settings.json.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}

internal sealed class HotkeyBinding
{
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public Keys Key { get; set; } = Keys.V;

    public HotkeyModifiers ToModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.NoRepeat;
        if (Ctrl) modifiers |= HotkeyModifiers.Ctrl;
        if (Alt) modifiers |= HotkeyModifiers.Alt;
        if (Shift) modifiers |= HotkeyModifiers.Shift;
        if (Win) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    public bool IsValid => (ToModifiers() & ~HotkeyModifiers.NoRepeat) != HotkeyModifiers.None;

    public bool IsSameAs(HotkeyBinding? other) =>
        other is not null && ToModifiers() == other.ToModifiers() && Key == other.Key;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}
