using System.Windows;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Where the window stood when it was last closed, and how to put it back there.
/// <para>
/// <b>Which view was open is deliberately NOT kept</b> (fourth hand-run, 15.09.2026). It used to
/// be, per monitor arrangement, on the reasoning that a DM docked at the desk works with one
/// screen open while the same DM undocked on the surface wants the overview. The DM asked for the
/// opposite, and the rule is now plain: <b>every start is the overview.</b> Opening one screen is
/// something done for a moment, not a state a session inherits from the one before it.
/// </para>
/// </summary>
internal static class StagePlace
{
    /// <summary>
    /// Puts the window back where it was - <b>if that place still exists</b>.
    /// <para>
    /// This is the check Part 7 asks for by name, and the reason is not tidiness: a window restored
    /// onto a monitor that has been unplugged lies outside every visible area. The application
    /// runs, answers nothing and cannot be found - which reads as a crash and is not one.
    /// </para>
    /// <para>
    /// <b>Overlap is enough, being contained is not.</b> A window half over the edge of a screen is
    /// where the DM left it; demanding that it fit entirely would move windows that were never
    /// lost.
    /// </para>
    /// </summary>
    internal static void Restore(Window window, WindowPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (placement is not { } place)
        {
            return;
        }

        var wanted = new Rect(place.Left, place.Top, place.Width, place.Height);

        if (!Screens.Enumerate(Environment.MachineName).Any(monitor => Overlaps(wanted, monitor)))
        {
            // Left where the window manager puts it. Saying nothing is right here: the DM did not
            // do anything wrong, and the window is where it can be seen.
            return;
        }

        window.Left = place.Left;
        window.Top = place.Top;
        window.Width = place.Width;
        window.Height = place.Height;
        window.WindowState = place.Maximised ? WindowState.Maximized : WindowState.Normal;
    }

    /// <summary>Where the window stands now, in the coordinates it was given.</summary>
    internal static WindowPlacement Taken(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // RestoreBounds rather than Left and Top: a maximised window reports the whole screen, and
        // restoring THAT would leave the DM with a window he cannot move by its title bar.
        var bounds = window.WindowState is WindowState.Normal
            ? new System.Windows.Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        return new WindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            window.WindowState is WindowState.Maximized);
    }

    private static bool Overlaps(Rect wanted, MonitorInfo monitor)
    {
        var (x, y, width, height) = monitor.Bounds;

        return wanted.Left < x + width
            && wanted.Left + wanted.Width > x
            && wanted.Top < y + height
            && wanted.Top + wanted.Height > y;
    }

    private readonly record struct Rect(double Left, double Top, double Width, double Height);
}
