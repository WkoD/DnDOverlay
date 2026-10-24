namespace DnDOverlay.Display;

/// <summary>
/// Where the setup card stands on a monitor: in the bottom right corner of its work area - above the
/// taskbar, never over it - and small enough that the device stays usable while the request lies before
/// the DM (DM decision, 24.10.2026).
/// <para>
/// <b>A card, not a screen.</b> The full-screen version lay topmost over every monitor of the device, so
/// on a machine that runs the control as well it covered the very window in which the DM would allow
/// the device - nothing could be paired there - and an unpaired display PC with autostart was blocked
/// for as long as a control in the network kept the request open.
/// </para>
/// </summary>
internal static class SetupCard
{
    /// <summary>The card's size at 96 DPI; it grows with the monitor's scaling.</summary>
    internal const double Width = 380;

    /// <summary>See <see cref="Width"/>.</summary>
    internal const double Height = 210;

    /// <summary>The gap to the work area's edges at 96 DPI.</summary>
    internal const double Gap = 16;

    /// <summary>Never more of the work area than this, in either direction - a small monitor at high scaling still keeps most of itself.</summary>
    internal const double MaxShare = 0.45;

    /// <summary>
    /// The card's rectangle in physical pixels, from the monitor's work area (physical pixels) and its
    /// DPI. Shrunk to fit, keeping its proportions, when the work area is small.
    /// </summary>
    internal static (int X, int Y, int Width, int Height) Place((int X, int Y, int Width, int Height) work, double dpi)
    {
        var scale = dpi > 0 ? dpi / 96 : 1;
        var width = Width * scale;
        var height = Height * scale;
        var fit = Math.Min(1, Math.Min(work.Width * MaxShare / width, work.Height * MaxShare / height));

        width *= fit;
        height *= fit;

        var gap = Gap * scale;
        var w = (int)Math.Round(width);
        var h = (int)Math.Round(height);

        return (
            work.X + work.Width - w - (int)Math.Round(gap),
            work.Y + work.Height - h - (int)Math.Round(gap),
            w,
            h);
    }
}
