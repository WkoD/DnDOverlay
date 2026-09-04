using System.Windows;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Where the window stands and which view was open in it - the two values Part 7 keeps PER MONITOR
/// ARRANGEMENT rather than per screen.
/// <para>
/// The difference is the one rule 4 draws. The tile order describes the ROOM and does not change
/// when the surface is docked; these two describe how the DM is sitting right now. Docked at the
/// desk he works on the big monitor with one screen open, undocked on the surface he wants the
/// overview - carrying one answer across both would be wrong twice a day.
/// </para>
/// </summary>
internal static class StagePlace
{
    /// <summary>
    /// What identifies this arrangement: the screens of this machine, in a fixed order.
    /// <para>
    /// The identifiers rather than the positions, because moving a monitor on the desk does not
    /// make it another arrangement - unplugging one does. The order is sorted, so that the same
    /// two monitors enumerated the other way round are still the same arrangement.
    /// </para>
    /// </summary>
    internal static string Arrangement()
    {
        var screens = Screens.Enumerate(Environment.MachineName)
            .Select(monitor => monitor.Screen.ScreenId.Value)
            .OrderBy(id => id, StringComparer.Ordinal);

        return string.Join('|', screens);
    }

    /// <summary>The view that was open in this arrangement, and on which screen.</summary>
    internal static StageView? Remembered(ControlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var here = Arrangement();

        return configuration.StageViews.FirstOrDefault(view => view.Monitors == here);
    }

    /// <summary>Keeps the view for this arrangement and leaves every other arrangement alone.</summary>
    internal static IReadOnlyList<StageView> With(
        IReadOnlyList<StageView> views, ScreenRef? opened)
    {
        ArgumentNullException.ThrowIfNull(views);

        var here = Arrangement();
        var mine = new StageView(
            here,
            opened is { } screen ? new ScreenKey(screen.Device.Value, screen.Screen.Value) : null);

        return [.. views.Where(view => view.Monitors != here), mine];
    }

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
