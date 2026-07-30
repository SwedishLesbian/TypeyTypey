using System.Reflection;

namespace TypeyTypey;

/// <summary>
/// Single source of truth for the displayed version. The project file supplies the value; no UI
/// string repeats it. Formatting is separated from reflection so it can be tested deterministically.
/// </summary>
internal static class VersionInfo
{
    private static readonly Lazy<string> Cached = new(() => Format(ReadRawVersion()));

    /// <summary>Three-part display version, e.g. "1.0.4".</summary>
    public static string Display => Cached.Value;

    /// <summary>
    /// The Win32 version resource fields, read back from the assembly attributes the project file
    /// sets. About shows these rather than its own copies, so the executable's properties page and
    /// the dialog can never disagree.
    /// </summary>
    public static string Product => Attribute<AssemblyProductAttribute>(a => a.Product, "TypeyTypey");

    /// <summary>AssemblyTitle is what the project file maps to the Win32 FileDescription field.</summary>
    public static string Description => Attribute<AssemblyTitleAttribute>(a => a.Title, string.Empty);

    public static string Company => Attribute<AssemblyCompanyAttribute>(a => a.Company, string.Empty);

    public static string Copyright => Attribute<AssemblyCopyrightAttribute>(a => a.Copyright, string.Empty);

    private static string Attribute<T>(Func<T, string> select, string fallback) where T : Attribute
    {
        T? attribute = typeof(VersionInfo).Assembly.GetCustomAttribute<T>();
        string? value = attribute is null ? null : select(attribute);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? ReadRawVersion()
    {
        Assembly assembly = typeof(VersionInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }

    /// <summary>
    /// Reduces assembly metadata to a user-facing version. Drops SourceLink build metadata and the
    /// always-zero fourth component, while preserving a prerelease suffix if one is ever used.
    /// </summary>
    internal static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "0.0.0";

        string value = raw.Trim();

        // "1.0.4+9a1b2c3" -> "1.0.4"
        int metadata = value.IndexOf('+');
        if (metadata >= 0)
            value = value[..metadata];

        // Preserve "1.0.4-beta.1" untouched apart from metadata removal.
        int prerelease = value.IndexOf('-');
        string core = prerelease >= 0 ? value[..prerelease] : value;
        string suffix = prerelease >= 0 ? value[prerelease..] : string.Empty;

        string[] parts = core.Split('.');
        if (parts.Length == 4 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
            core = string.Join('.', parts.Take(3));

        return core + suffix;
    }
}
