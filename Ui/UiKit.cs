using System.Drawing.Drawing2D;

namespace TypeyTypey;

/// <summary>
/// The small set of shared visual primitives the windows are built from: a type scale, a spacing
/// scale, and three control types that exist so <see cref="ThemeManager"/> can recognise them.
///
/// This is deliberately not a widget framework. It is the minimum needed to stop every window
/// inventing its own margins, and to let a card and a muted caption survive a theme change.
/// </summary>
internal static class UiKit
{
    /// <summary>Spacing scale. Logical units; the deferred DPI pass scales them.</summary>
    internal const int SpaceTight = 4;
    internal const int Space = 8;
    internal const int SpaceWide = 12;
    internal const int SpaceSection = 18;
    internal const int CardPadding = 14;
    internal const int PagePadding = 20;

    /// <summary>
    /// Type scale derived from the user's own message-box font rather than a hardcoded family, so
    /// this follows their font and scaling choices. Point sizes are DPI-independent by definition.
    /// </summary>
    private static Font Base => SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

    public static Font Title { get; } = new(Base.FontFamily, Base.SizeInPoints + 5.25f, FontStyle.Regular);

    public static Font EyebrowFont { get; } = new(Base.FontFamily, Base.SizeInPoints - 0.75f, FontStyle.Bold);

    public static Font Body { get; } = new(Base.FontFamily, Base.SizeInPoints, FontStyle.Regular);

    public static Font Helper { get; } = new(Base.FontFamily, Base.SizeInPoints - 0.75f, FontStyle.Regular);

    /// <summary>
    /// Fixed-pitch face for command flags and hotkey combinations, so they align in a column.
    /// Falls back rather than throwing: naming a font family that is not installed throws from a
    /// static initialiser, which would take the whole application down before it drew anything.
    /// </summary>
    public static Font Mono { get; } = ResolveMono(Base.SizeInPoints);

    private static Font ResolveMono(float size)
    {
        foreach (string name in new[] { "Cascadia Mono", "Consolas", "Lucida Console" })
        {
            try
            {
                using var family = new FontFamily(name);
                return new Font(family, size, FontStyle.Regular);
            }
            catch (ArgumentException)
            {
                // Not installed. Try the next one.
            }
        }
        return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular);
    }

    /// <summary>A section eyebrow: short, uppercase, quiet. Names the card that follows it.</summary>
    public static EyebrowLabel Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = EyebrowFont,
        AutoSize = true,
        Margin = new Padding(SpaceTight, SpaceSection, 0, SpaceTight)
    };

    /// <summary>Secondary text explaining what a control does. Never repeats the control's label.</summary>
    public static MutedLabel Caption(string text) => new()
    {
        Text = text,
        Font = Helper,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, Space)
    };
}

/// <summary>
/// A rounded surface panel. Painted rather than styled because WinForms has no rounded border: the
/// panel keeps its parent's colour as <see cref="Control.BackColor"/> so the corners blend, and the
/// card itself is filled inside <see cref="OnPaint"/>.
/// </summary>
internal sealed class CardPanel : Panel
{
    private const int Radius = 6;

    public CardPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Padding = new Padding(UiKit.CardPadding);
        Margin = new Padding(0);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Fill colour. Distinct from BackColor, which stays matched to the page behind it.</summary>
    public Color CardColor { get; set; } = SystemColors.Window;

    public Color BorderColor { get; set; } = SystemColors.ControlDark;

    /// <summary>When set, a thick accent bar is drawn down the left edge. Used by the elevation notice.</summary>
    public Color AccentEdge { get; set; } = Color.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedPath(bounds, Radius);
        using var fill = new SolidBrush(CardColor);
        e.Graphics.FillPath(fill, path);

        if (AccentEdge != Color.Empty)
        {
            // Clipped to the rounded path so the bar follows the corner rather than overhanging it.
            using var clip = new Region(path);
            e.Graphics.Clip = clip;
            using var accent = new SolidBrush(AccentEdge);
            e.Graphics.FillRectangle(accent, bounds.X, bounds.Y, 3, bounds.Height + 1);
            e.Graphics.ResetClip();
        }

        using var border = new Pen(BorderColor);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Marker type so the theme can paint secondary text without a second pass over every label.</summary>
internal sealed class MutedLabel : Label;

/// <summary>A one-pixel rule, used to separate a docked action bar from the page scrolling above it.</summary>
internal sealed class SeparatorPanel : Panel
{
    public SeparatorPanel()
    {
        Height = 1;
        Dock = DockStyle.Top;
        Margin = new Padding(0);
    }
}

/// <summary>Marker type for a section eyebrow. Muted, like a caption, but never wrapped in a card.</summary>
internal sealed class EyebrowLabel : Label;
