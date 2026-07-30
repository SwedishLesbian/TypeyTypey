using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TypeyTypey;

/// <summary>Colours for one effective theme. Deliberately small and native-looking.</summary>
internal sealed record ThemePalette(
    Color Window,
    Color Surface,
    Color Text,
    Color DisabledText,
    Color Border,
    Color ButtonFace,
    Color Accent)
{
    public static ThemePalette Light { get; } = new(
        Window: SystemColors.Control,
        Surface: SystemColors.Window,
        Text: SystemColors.ControlText,
        DisabledText: SystemColors.GrayText,
        Border: SystemColors.ControlDark,
        ButtonFace: SystemColors.Control,
        // Amber reads as "elevated privilege" without the alarm of red.
        Accent: Color.FromArgb(138, 88, 0));

    public static ThemePalette Dark { get; } = new(
        Window: Color.FromArgb(32, 32, 32),
        Surface: Color.FromArgb(43, 43, 43),
        Text: Color.FromArgb(240, 240, 240),
        DisabledText: Color.FromArgb(154, 154, 154),
        Border: Color.FromArgb(82, 82, 82),
        ButtonFace: Color.FromArgb(56, 56, 56),
        Accent: Color.FromArgb(232, 184, 96));
}

/// <summary>
/// Resolves and applies the effective theme. Intentionally a plain static helper rather than a
/// theming framework: the application has two windows and a fixed control set.
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>WM_SETTINGCHANGE; broadcast when the user switches the Windows app theme.</summary>
    internal const int WmSettingChange = 0x001A;

    /// <summary>
    /// Resolves a stored preference to a concrete appearance. <see cref="AppTheme.System"/> is never
    /// rewritten to Light or Dark in settings; it is resolved fresh on each call.
    /// </summary>
    public static EffectiveTheme Resolve(AppTheme preference) => preference switch
    {
        AppTheme.Light => EffectiveTheme.Light,
        AppTheme.Dark => EffectiveTheme.Dark,
        _ => ReadWindowsAppsTheme()
    };

    /// <summary>
    /// High contrast is a Windows accessibility mode with a user-chosen palette. Overriding it with
    /// custom colours would defeat the accommodation, so themed painting is suppressed entirely.
    /// </summary>
    public static bool ShouldUseSystemPalette => SystemInformation.HighContrast;

    private static EffectiveTheme ReadWindowsAppsTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // The value is absent on older builds and on systems that never left the default.
            return key?.GetValue(AppsUseLightThemeValue) is int light && light == 0
                ? EffectiveTheme.Dark
                : EffectiveTheme.Light;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return EffectiveTheme.Light;
        }
    }

    public static ThemePalette PaletteFor(EffectiveTheme theme) =>
        theme == EffectiveTheme.Dark ? ThemePalette.Dark : ThemePalette.Light;

    /// <summary>Applies <paramref name="preference"/> to a window and everything inside it.</summary>
    public static void Apply(Form form, AppTheme preference)
    {
        if (ShouldUseSystemPalette)
            return;

        EffectiveTheme effective = Resolve(preference);
        ThemePalette palette = PaletteFor(effective);

        form.BackColor = palette.Window;
        form.ForeColor = palette.Text;
        ApplyToChildren(form.Controls, palette, effective, palette.Window);
        TrySetDarkTitleBar(form, effective == EffectiveTheme.Dark);
    }

    /// <summary>
    /// Paints a control tree. <paramref name="background"/> is the colour behind the controls at this
    /// level, which changes when the walk descends into a <see cref="CardPanel"/>: a card fills
    /// itself with the surface colour, so anything inside it must match the surface rather than the
    /// page, or it paints a visible rectangle over the card.
    /// </summary>
    private static void ApplyToChildren(Control.ControlCollection controls, ThemePalette palette, EffectiveTheme theme, Color background)
    {
        bool dark = theme == EffectiveTheme.Dark;
        foreach (Control control in controls)
        {
            Color childBackground = background;
            switch (control)
            {
                case CardPanel card:
                    // The card paints its own fill and border, so BackColor stays the page colour
                    // and the rounded corners blend into it.
                    card.BackColor = background;
                    card.ForeColor = palette.Text;
                    card.CardColor = palette.Surface;
                    card.BorderColor = palette.Border;
                    if (card.AccentEdge != Color.Empty)
                        card.AccentEdge = palette.Accent;
                    childBackground = palette.Surface;
                    break;

                case EyebrowLabel or MutedLabel:
                    control.BackColor = background;
                    control.ForeColor = palette.DisabledText;
                    break;

                case SeparatorPanel:
                    control.BackColor = palette.Border;
                    break;

                case TextBox textBox:
                    textBox.BackColor = palette.Surface;
                    textBox.ForeColor = palette.Text;
                    textBox.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    break;

                case ListBox listBox:
                    listBox.BackColor = palette.Surface;
                    listBox.ForeColor = palette.Text;
                    listBox.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    break;

                case ComboBox comboBox:
                    comboBox.BackColor = palette.Surface;
                    comboBox.ForeColor = palette.Text;
                    comboBox.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                    break;

                case NumericUpDown numeric:
                    numeric.BackColor = palette.Surface;
                    numeric.ForeColor = palette.Text;
                    numeric.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    break;

                case Button button:
                    button.BackColor = palette.ButtonFace;
                    button.ForeColor = palette.Text;
                    button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                    button.FlatAppearance.BorderColor = palette.Border;
                    break;

                default:
                    control.BackColor = background;
                    control.ForeColor = palette.Text;
                    break;
            }

            if (control.HasChildren)
                ApplyToChildren(control.Controls, palette, theme, childBackground);
        }
    }

    /// <summary>Applies dark colours to a context menu, which is painted outside the form's control tree.</summary>
    public static void ApplyToMenu(ToolStrip menu, AppTheme preference)
    {
        if (ShouldUseSystemPalette)
            return;

        ApplyPaletteToMenu(menu, PaletteFor(Resolve(preference)));
    }

    /// <summary>
    /// Paints a menu and every submenu beneath it. Split from <see cref="ApplyToMenu"/> so it can be
    /// tested against a chosen palette without depending on the machine's theme or high-contrast
    /// setting.
    ///
    /// The recursion is the point. A submenu is not a child control of its parent menu: it is a
    /// separate <see cref="ToolStripDropDown"/> with its own back colour, fore colour and renderer.
    /// Painting only the top-level items left the Typing Mode submenu with the system defaults —
    /// dark text on the dark background it inherited, which is unreadable.
    /// </summary>
    internal static void ApplyPaletteToMenu(ToolStrip menu, ThemePalette palette)
    {
        menu.BackColor = palette.Window;
        menu.ForeColor = palette.Text;
        menu.Renderer = new MenuRenderer(palette);
        ApplyPaletteToItems(menu.Items, palette);
    }

    private static void ApplyPaletteToItems(ToolStripItemCollection items, ThemePalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = palette.Window;
            item.ForeColor = palette.Text;

            if (item is ToolStripDropDownItem { HasDropDownItems: true } parent)
                ApplyPaletteToMenu(parent.DropDown, palette);
        }
    }

    /// <summary>
    /// Asks DWM to paint a dark title bar. Unsupported on Windows builds before 1809 and on
    /// non-Windows-11 builds using the older attribute id, so every failure is ignored.
    /// </summary>
    private static void TrySetDarkTitleBar(Form form, bool dark)
    {
        if (!form.IsHandleCreated)
            return;

        int value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Draws the text and the submenu arrow itself, because the base renderer does not use the
    /// item's <c>ForeColor</c> for either. Disabled text comes from <c>SystemColors.GrayText</c> and
    /// the arrow from <c>SystemColors.ControlText</c> — both fixed, and both near-invisible on a
    /// dark menu.
    /// </summary>
    private sealed class MenuRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new MenuColours(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? palette.Text : palette.DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? palette.Text : palette.DisabledText;
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColours(ThemePalette palette) : ProfessionalColorTable
    {
        // The tick beside the active typing mode sits in this box. Left at its default it is a pale
        // highlight that swallows the glyph on a dark menu.
        public override Color CheckBackground => palette.ButtonFace;
        public override Color CheckSelectedBackground => palette.Accent;
        public override Color CheckPressedBackground => palette.Accent;

        public override Color MenuItemSelected => palette.ButtonFace;
        public override Color MenuItemSelectedGradientBegin => palette.ButtonFace;
        public override Color MenuItemSelectedGradientEnd => palette.ButtonFace;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuBorder => palette.Border;
        public override Color ToolStripDropDownBackground => palette.Window;
        public override Color ImageMarginGradientBegin => palette.Window;
        public override Color ImageMarginGradientMiddle => palette.Window;
        public override Color ImageMarginGradientEnd => palette.Window;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Border;
    }
}
