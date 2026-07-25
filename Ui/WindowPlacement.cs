namespace TypeyTypey;

/// <summary>
/// Deterministic window sizing and placement policy, kept free of any Form or screen dependency so
/// it can be tested without a display. Sizes are expressed in logical (96 DPI) pixels; WinForms
/// autoscaling converts them to device pixels via <see cref="AutoScaleMode.Dpi"/>.
/// </summary>
internal static class WindowPlacement
{
    /// <summary>
    /// Default Settings client area in logical pixels, sized so every section fits without clipping.
    /// The 615:700 client ratio yields an outer window of roughly 0.855 width-to-height once the
    /// non-client frame is added, matching the proportions the maintainer measured on a correctly
    /// sized dialog. Absolute pixel counts from that measurement were unusable because the
    /// screenshot's DPI metadata was unknown, but the ratio is scale-invariant.
    /// </summary>
    public static Size DefaultSettingsClientSize => new(615, 700);

    /// <summary>
    /// Outer-window floor for Settings, in logical pixels. Below this the root panel scrolls rather
    /// than clipping its sections.
    /// </summary>
    public static Size MinimumSettingsWindowSize => new(460, 420);

    public static Size DefaultPickerClientSize => new(640, 420);

    /// <summary>
    /// A saved position is only honoured when a meaningful part of the window's title bar remains
    /// reachable on some connected monitor. This rejects positions left behind by a disconnected
    /// display or a resolution change.
    /// </summary>
    /// <param name="window">Proposed window bounds in virtual-screen coordinates.</param>
    /// <param name="workingAreas">Working area of every connected monitor.</param>
    internal static bool IsPositionUsable(Rectangle window, IReadOnlyList<Rectangle> workingAreas)
    {
        if (window.Width <= 0 || window.Height <= 0 || workingAreas.Count == 0)
            return false;

        foreach (Rectangle area in workingAreas)
        {
            Rectangle overlap = Rectangle.Intersect(area, window);
            if (overlap.Width < MinimumVisibleWidth || overlap.Height < MinimumVisibleHeight)
                continue;

            // The grab area must be on-screen, otherwise the window cannot be moved by mouse.
            if (window.Top >= area.Top && window.Top <= area.Bottom - MinimumVisibleHeight)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Moves <paramref name="window"/> fully onto the monitor it most overlaps, or onto
    /// <paramref name="fallback"/> when it overlaps none. Never returns an off-screen location.
    /// </summary>
    internal static Point ClampToWorkingArea(Rectangle window, IReadOnlyList<Rectangle> workingAreas, Rectangle fallback)
    {
        Rectangle target = fallback;
        int bestOverlap = 0;
        foreach (Rectangle area in workingAreas)
        {
            Rectangle overlap = Rectangle.Intersect(area, window);
            int score = Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
            if (score > bestOverlap)
            {
                bestOverlap = score;
                target = area;
            }
        }

        // Clamp the low edge last so an oversized window shows its top-left rather than its centre.
        int x = Math.Min(window.Left, target.Right - window.Width);
        int y = Math.Min(window.Top, target.Bottom - window.Height);
        return new Point(Math.Max(x, target.Left), Math.Max(y, target.Top));
    }

    /// <summary>
    /// Resolves the start location for a window, given an optional saved position. Returns null when
    /// the caller should fall back to the framework's centre-screen behaviour.
    /// </summary>
    internal static Point? ResolveStartLocation(
        int? savedLeft, int? savedTop, Size windowSize,
        IReadOnlyList<Rectangle> workingAreas, Rectangle activeArea)
    {
        if (savedLeft is null || savedTop is null)
            return null;

        var proposed = new Rectangle(savedLeft.Value, savedTop.Value, windowSize.Width, windowSize.Height);
        if (IsPositionUsable(proposed, workingAreas))
            return proposed.Location;

        // A saved position that is no longer reachable is clamped rather than discarded, so the
        // window stays near where the user last put it when a monitor merely changed resolution.
        return ClampToWorkingArea(proposed, workingAreas, activeArea);
    }

    private const int MinimumVisibleWidth = 120;
    private const int MinimumVisibleHeight = 32;
}
