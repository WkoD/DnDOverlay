using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
/// <para>
/// <b>One unit system, physical pixels, from end to end.</b> The place is taken with
/// <c>GetWindowPlacement</c>, checked against the monitors <see cref="Screens.Enumerate"/> reports
/// and put back with <c>SetWindowPlacement</c> - all three in physical pixels. It used to be taken
/// from <c>Window.Left/Top/Width/Height</c>, which a PerMonitorV2 application gets in
/// device-independent units of whichever monitor the window stood on. At 100 % the two agree; on
/// the Surface at 200 % beside a monitor at 150 % a place on the unplugged second monitor could
/// still seem to overlap the primary, passed the check, and was then applied at the primary's scale
/// - off-screen, which is exactly what the check exists to prevent (Part 7).
/// </para>
/// <para>
/// <b>A file written before the change is read once with the new meaning.</b> Its numbers were
/// device-independent units; they are now taken as pixels, without a schema version for it. At
/// 100 % that is the same place. At any other scale the window comes up once somewhere else, and
/// smaller - but never off-screen, because the check and the restore read the numbers the same way.
/// The next close writes pixels and the matter is over. A version bump with a converter would have
/// had to guess the DPI the old numbers were taken at, which nothing recorded.
/// </para>
/// </summary>
internal static partial class StagePlace
{
    private const uint SwShowNormal = 1;
    private const uint SwShowMaximized = 3;

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
    /// <para>
    /// <b>Applied once the window has a handle, not now.</b> <c>SetWindowPlacement</c> needs the
    /// HWND, and the constructor that calls this runs before there is one. <c>SourceInitialized</c>
    /// comes inside <c>Show</c>, after WPF has applied its own initial size and before the window is
    /// drawn - so it still never appears in one place and jumps to another. The monitors are asked
    /// then too, which is the moment that counts.
    /// </para>
    /// </summary>
    internal static void Restore(Window window, WindowPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (placement is not { } place)
        {
            return;
        }

        window.SourceInitialized += Apply;

        void Apply(object? sender, EventArgs args)
        {
            window.SourceInitialized -= Apply;

            if (!Reachable(place, Screens.Enumerate(Environment.MachineName)))
            {
                // Left where the window manager puts it. Saying nothing is right here: the DM did not
                // do anything wrong, and the window is where it can be seen.
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            var native = new NativePlacement { Length = (uint)Marshal.SizeOf<NativePlacement>() };

            if (handle == 0 || !GetWindowPlacement(handle, ref native))
            {
                return;
            }

            native.Flags = 0;
            native.ShowCmd = place.Maximised ? SwShowMaximized : SwShowNormal;
            native.NormalPosition = Normal(place);

            // TWICE, and the second is not a slip. When the place lies on a monitor with another scale
            // than the one the window was created on, the first call moves it there, Windows sends
            // WM_DPICHANGED, and WPF answers by resizing the window to the suggested rectangle - the
            // old size scaled, not the size kept. The second call finds the window already at the
            // target's scale, so nothing changes scale and the kept size stands. On one scale the
            // second call changes nothing at all.
            _ = SetWindowPlacement(handle, ref native);
            _ = SetWindowPlacement(handle, ref native);
        }
    }

    /// <summary>
    /// Where the window stands now, in physical pixels - or <see langword="null"/> when Windows cannot
    /// say, in which case the caller keeps what it had.
    /// </summary>
    internal static WindowPlacement? Taken(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        var native = new NativePlacement { Length = (uint)Marshal.SizeOf<NativePlacement>() };

        if (handle == 0 || !GetWindowPlacement(handle, ref native))
        {
            return null;
        }

        return Kept(native.NormalPosition, native.ShowCmd);
    }

    /// <summary>
    /// What is kept of a placement Windows reports. <b>The normal position, never the current one</b>:
    /// a maximised window covers the whole screen, and restoring THAT as its normal size would leave
    /// the DM with a window he cannot move by its title bar. Minimised counts as not maximised, as it
    /// did before - the window comes back at its normal position.
    /// </summary>
    internal static WindowPlacement Kept(NativeRect normal, uint showCmd) =>
        new(normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, showCmd == SwShowMaximized);

    /// <summary>
    /// The kept place as the rectangle <c>SetWindowPlacement</c> takes. Rounded, because a file written
    /// before the change can hold fractional device-independent units.
    /// </summary>
    internal static NativeRect Normal(WindowPlacement place)
    {
        ArgumentNullException.ThrowIfNull(place);

        var left = (int)Math.Round(place.Left);
        var top = (int)Math.Round(place.Top);

        return new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + (int)Math.Round(place.Width),
            Bottom = top + (int)Math.Round(place.Height),
        };
    }

    /// <summary>
    /// Whether a kept place still lies on a monitor - <b>overlapping one is enough</b>; touching its edge
    /// is not. Both sides in physical pixels: the place as <c>GetWindowPlacement</c> gave it, the
    /// monitors' bounds as <see cref="Screens.Enumerate"/> reports them.
    /// <para>
    /// One difference is accepted: <c>GetWindowPlacement</c> speaks WORKSPACE coordinates, which are
    /// shifted against screen coordinates by an app bar docked at the top or left of the primary
    /// monitor - a taskbar's thickness, a few dozen pixels. An overlap test can afford that, and
    /// <c>SetWindowPlacement</c> pulls a placement that lies on no monitor at all back by itself.
    /// </para>
    /// </summary>
    internal static bool Reachable(WindowPlacement place, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors.Any(monitor =>
        {
            var (x, y, width, height) = monitor.Bounds;

            return place.Left < x + width
                && place.Left + place.Width > x
                && place.Top < y + height
                && place.Top + place.Height > y;
        });
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint window, ref NativePlacement placement);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint window, ref NativePlacement placement);

    /// <summary><c>RECT</c>, in physical pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    /// <summary><c>WINDOWPLACEMENT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        internal uint Length;
        internal uint Flags;
        internal uint ShowCmd;
        internal NativePoint MinPosition;
        internal NativePoint MaxPosition;
        internal NativeRect NormalPosition;
    }
}
