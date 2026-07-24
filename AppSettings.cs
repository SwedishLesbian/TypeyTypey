using System.Text.Json;

namespace TypeyTypey;

internal sealed class AppSettings
{
    public HotkeyBinding TypeClipboardHotkey { get; set; } = new();
    public HotkeyBinding HistoryHotkey { get; set; } = new() { Shift = true };
    public int CharacterDelayMs { get; set; } = 15;
    public int InitialDelayMs { get; set; } = 500;
    public bool ClearClipboardAfterTyping { get; set; }
    public bool ClipboardMonitoringEnabled { get; set; } = true;
    public int MaximumHistoryEntries { get; set; } = 50;
    public bool StartWithWindows { get; set; }
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
        CharacterDelayMs = Math.Clamp(CharacterDelayMs, 0, 1_000);
        InitialDelayMs = Math.Clamp(InitialDelayMs, 0, 10_000);
        MaximumHistoryEntries = Math.Clamp(MaximumHistoryEntries, 1, 500);
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
