using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// Which screens a window lies on — the judgement, as a pure function, so that the one part of
/// "the control must not cover its own stage" that CAN be checked is checked (Part 2).
/// <para>
/// The reading of the window rectangle is a Win32 call and stays next door; what is decided here
/// is only ever arithmetic over rectangles that were handed in.
/// </para>
/// </summary>
public static class StageCover
{
    /// <summary>
    /// Every monitor the window overlaps — <b>every</b> one, not the one it covers most of.
    /// <para>
    /// That is the whole decision. With the largest overlap, a window lying 60 % on A and 40 % on B
    /// would leave B to be played on, and an always-on-top overlay would drop onto the remaining
    /// 40 % of the stage: exactly the case the rule exists to prevent. "All of them" is also the
    /// simpler rule — no threshold, no tie, no flapping at the monitor border. The price is that a
    /// window lying across two screens blocks both, and that is said on both tiles and undone with
    /// one drag.
    /// </para>
    /// </summary>
    /// <param name="window">
    /// The window in physical pixels of the virtual desktop, as <c>GetWindowRect</c> gives it.
    /// An empty or minimised window covers nothing.
    /// </param>
    /// <param name="monitors">The monitors, as <see cref="Screens.Enumerate"/> gives them.</param>
    public static IReadOnlyList<ScreenId> Covered(
        (int X, int Y, int Width, int Height) window,
        IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (window.Width <= 0 || window.Height <= 0)
        {
            return [];
        }

        return [.. monitors.Where(monitor => Overlaps(window, monitor.Bounds))
            .Select(monitor => monitor.Screen.ScreenId)];
    }

    /// <summary>
    /// The same question for a window we have a handle to: reads the rectangle in physical pixels
    /// and answers with the screens it lies on. A minimised window covers nothing - the DM put it
    /// away, and every screen is to be played on again.
    /// </summary>
    public static IReadOnlyList<ScreenId> CoveredBy(nint window, IReadOnlyList<MonitorInfo> monitors)
    {
        if (window == 0 || Native.IsIconic(window) || !Native.GetWindowRect(window, out var rect))
        {
            return [];
        }

        return Covered(
            (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
            monitors);
    }

    /// <summary>
    /// A real overlap, so touching edges do not count. Without the strictness a window snapped
    /// flush against the border of the next monitor would block it - and the DM would be looking
    /// at a screen that says it is covered while nothing is on it.
    /// </summary>
    private static bool Overlaps(
        (int X, int Y, int Width, int Height) window,
        (int X, int Y, int Width, int Height) monitor) =>
        window.X < monitor.X + monitor.Width
        && monitor.X < window.X + window.Width
        && window.Y < monitor.Y + monitor.Height
        && monitor.Y < window.Y + window.Height;
}
