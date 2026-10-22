using System.Runtime.InteropServices;
using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// The windows of other programs on this display PC: found, and laid (Part 6, "foreign windows").
/// <para>
/// <b>Four traps, each answered here.</b> The frame comes from <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>, not
/// <c>GetWindowRect</c>, which since Windows 10 includes the invisible shadow border, and a move is
/// widened by that border again before it goes to <c>SetWindowPos</c>; cloaked windows -
/// suspended Store apps that claim to be visible - are left out; tool windows and our own are left out,
/// or the tile would draw itself; and a maximised window is restored before it is moved, or nothing
/// visible happens.
/// </para>
/// <para>
/// <b>The program, never the title</b> (idea 9) - the executable's name without its extension.
/// </para>
/// </summary>
public static unsafe partial class ForeignWindowSurvey
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const int SwRestore = 9;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int ErrorAccessDenied = 5;

    /// <summary>A sanity bound - a dozen visible windows is many (Part 4: at most 200 per list).</summary>
    private const int MostWindows = 200;

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    /// <summary>
    /// Every visible window of another program on one of these monitors, topmost first - EnumWindows
    /// walks the Z order from the top, so the position in the walk IS the depth.
    /// </summary>
    public static IReadOnlyList<ForeignWindow> Survey(IReadOnlyList<MonitorInfo> monitors, int ownProcess)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var found = new List<ForeignWindow>();
        var depth = 0;

        EnumWindows(
            (window, context) =>
            {
                if (found.Count >= MostWindows)
                {
                    return false;
                }

                if (!IsWindowVisible(window) || (GetWindowLongPtr(window, GwlExStyle) & WsExToolWindow) != 0 || Cloaked(window))
                {
                    return true;
                }

                _ = GetWindowThreadProcessId(window, out var process);

                if (process == ownProcess || Frame(window) is not { } frame || frame.Right <= frame.Left || frame.Bottom <= frame.Top)
                {
                    return true;
                }

                var centreX = (frame.Left + frame.Right) / 2;
                var centreY = (frame.Top + frame.Bottom) / 2;
                var monitor = monitors.FirstOrDefault(candidate =>
                    centreX >= candidate.Left && centreX < candidate.Left + candidate.Width
                    && centreY >= candidate.Top && centreY < candidate.Top + candidate.Height);

                if (monitor is null || Program((int)process) is not { } program)
                {
                    return true;
                }

                found.Add(new ForeignWindow(window, monitor.Screen.ScreenId, program, Normalise(frame, monitor), depth++, IsIconic(window)));

                return true;
            },
            0);

        return found;
    }

    /// <summary>
    /// Lays one window and reads back what happened. Refuses a handle that now belongs to another
    /// program - handles are reused once a window is gone - and says when Windows refused because the
    /// program runs elevated.
    /// </summary>
    /// <param name="after">The frame as it stands afterwards, normalised to the monitor; null when it could not be read.</param>
    public static WindowOutcome Lay(long handle, string program, WindowAction action, Rect? bounds, MonitorInfo monitor, out Rect? after)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        after = null;

        var window = (nint)handle;

        _ = GetWindowThreadProcessId(window, out var process);

        if (!IsWindow(window) || !string.Equals(Program((int)process), program, StringComparison.OrdinalIgnoreCase))
        {
            return WindowOutcome.Gone;
        }

        bool laid;

        switch (action)
        {
            case WindowAction.Move when bounds is { } wanted:
                if (IsZoomed(window))
                {
                    _ = ShowWindow(window, SwRestore);
                }

                var placed = WindowRect(wanted, monitor, Border(window));

                laid = SetWindowPos(window, 0, placed.X, placed.Y, placed.Width, placed.Height, SwpNoZOrder | SwpNoActivate);
                break;

            case WindowAction.Minimize:
                laid = ShowWindow(window, SwMinimize) || IsIconic(window);
                break;

            case WindowAction.Maximize:
                laid = ShowWindow(window, SwMaximize) || IsZoomed(window);
                break;

            case WindowAction.Restore:
                laid = ShowWindow(window, SwRestore) || (!IsZoomed(window) && !IsIconic(window));
                break;

            default:
                return WindowOutcome.Done;
        }

        var error = Marshal.GetLastPInvokeError();

        if (Frame(window) is { } frame)
        {
            after = Normalise(frame, monitor);
        }

        return laid || error != ErrorAccessDenied ? WindowOutcome.Done : WindowOutcome.Elevated;
    }

    /// <summary>
    /// The rectangle to hand <c>SetWindowPos</c> so that the VISIBLE frame lands on
    /// <paramref name="wanted"/>. The wireframe is the visible frame (read from
    /// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>), but <c>SetWindowPos</c> sets the window rectangle, which
    /// since Windows 10 includes the invisible shadow border - Part 6: "ein Verschieben darauf
    /// gerechnet wäre um denselben Betrag daneben". Without widening by that border every move
    /// shifted the window by it and shrank it by twice it, and the next move started from the
    /// smaller frame.
    /// </summary>
    /// <param name="wanted">The visible frame, normalised to <paramref name="monitor"/>.</param>
    /// <param name="border">How far the window rectangle reaches beyond the visible frame on each side, in pixels; zero where it cannot be read.</param>
    public static (int X, int Y, int Width, int Height) WindowRect(
        Rect wanted,
        MonitorInfo monitor,
        (int Left, int Top, int Right, int Bottom) border)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var left = monitor.Left + (int)Math.Round(wanted.X * monitor.Width);
        var top = monitor.Top + (int)Math.Round(wanted.Y * monitor.Height);
        var width = (int)Math.Round(wanted.Width * monitor.Width);
        var height = (int)Math.Round(wanted.Height * monitor.Height);

        return (left - border.Left, top - border.Top, width + border.Left + border.Right, height + border.Top + border.Bottom);
    }

    /// <summary>
    /// The invisible border as it stands now - read after a maximised window is restored, because the
    /// border of a maximised window is not the one it has in its normal place. Zero when either
    /// rectangle cannot be read: seven pixels off is better than not moving at all.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom) Border(nint window) =>
        Native.GetWindowRect(window, out var outer) && Frame(window) is { } visible
            ? (visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom)
            : (0, 0, 0, 0);

    private static Rect Normalise(Native.RectL frame, MonitorInfo monitor) =>
        new(
            (frame.Left - monitor.Left) / (double)Math.Max(1, monitor.Width),
            (frame.Top - monitor.Top) / (double)Math.Max(1, monitor.Height),
            (frame.Right - frame.Left) / (double)Math.Max(1, monitor.Width),
            (frame.Bottom - frame.Top) / (double)Math.Max(1, monitor.Height));

    private static Native.RectL? Frame(nint window) =>
        Native.DwmGetWindowAttribute(window, Native.DwmwaExtendedFrameBounds, out var frame, Marshal.SizeOf<Native.RectL>()) == 0
            ? frame
            : null;

    private static bool Cloaked(nint window) =>
        DwmGetWindowAttributeInt(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>The executable's name without extension - "MapTool", "chrome" - or null when it cannot be read.</summary>
    private static string? Program(int process)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process);

        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024;

            return QueryFullProcessImageName(handle, 0, buffer, ref length)
                ? Path.GetFileNameWithoutExtension(new string(buffer, 0, length))
                : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial long GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static partial int DwmGetWindowAttributeInt(nint window, int attribute, out int value, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
