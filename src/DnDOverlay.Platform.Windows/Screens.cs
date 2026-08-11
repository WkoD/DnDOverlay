using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>One monitor as Windows reports it, plus the rectangle an overlay has to cover.</summary>
/// <param name="Bounds">In PHYSICAL pixels, which is what <c>SetWindowPos</c> speaks.</param>
public sealed record MonitorInfo(ScreenInfo Screen, int Left, int Top, int Width, int Height)
{
    public (int X, int Y, int Width, int Height) Bounds => (Left, Top, Width, Height);
}

/// <summary>
/// Finding the screens - and the single reason this project exists.
/// <para>
/// The control needs this as much as the display: it has to know which screens its own window
/// covers, because an always-on-top overlay must not land on the DM's stage (Part 2). Both
/// processes therefore derive the <see cref="ScreenId"/> HERE, from the same code. Were the
/// derivation written twice, one of the small decisions would eventually differ - which level
/// of <c>EnumDisplayDevices</c>, with or without <c>EDD_GET_DEVICE_INTERFACE_NAME</c>, trimmed,
/// lower-cased - and the result would not be an error but a comparison that never matches:
/// the control suppresses nothing and covers its own stage. No test can catch that, because a
/// test in one process proves nothing about a second one.
/// </para>
/// </summary>
public static unsafe class Screens
{
    /// <summary>
    /// Every monitor attached to the desktop, with its device instance path as
    /// <see cref="ScreenId"/>.
    /// <para>
    /// The identifier comes from the device INTERFACE name, never from the enumeration index.
    /// Indexes shift when a monitor is plugged or unplugged - measured, not assumed: after
    /// reattaching the touch table it moved from index 2 to index 1 with the desk arrangement
    /// unchanged (M0). Every stored screen setting would then point at the wrong panel (Part 3).
    /// </para>
    /// </summary>
    /// <param name="deviceName">
    /// The device name the labels are derived from - the DEVICE name, not the raw machine name,
    /// so screens inherit the uniqueness of their device (Part 3).
    /// </param>
    public static IReadOnlyList<MonitorInfo> Enumerate(string deviceName)
    {
        var monitors = new List<MonitorInfo>();

        for (uint index = 0; ; index++)
        {
            var adapter = new Native.DisplayDevice { cb = (uint)sizeof(Native.DisplayDevice) };

            if (!Native.EnumDisplayDevices(null, index, ref adapter, 0))
            {
                break;
            }

            var attached = (adapter.StateFlags & Native.DisplayDeviceAttachedToDesktop) != 0;
            var mirror = (adapter.StateFlags & Native.DisplayDeviceMirroringDriver) != 0;

            if (!attached || mirror)
            {
                continue;
            }

            var adapterName = Native.ReadFixed(adapter.DeviceName, 32);
            var monitor = Describe(adapterName, deviceName);

            if (monitor is not null)
            {
                monitors.Add(monitor);
            }
        }

        return monitors;
    }

    private static MonitorInfo? Describe(string adapterName, string deviceName)
    {
        var mode = new Native.DevMode { dmSize = (ushort)sizeof(Native.DevMode) };

        if (!Native.EnumDisplaySettings(adapterName, Native.EnumCurrentSettings, ref mode))
        {
            return null;
        }

        var origin = mode.dmPosition;
        var width = (int)mode.dmPelsWidth;
        var height = (int)mode.dmPelsHeight;

        var handle = Native.MonitorFromPoint(origin, Native.MonitorDefaultToNearest);
        var dpi = 96d;

        if (handle != 0 && Native.GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0)
        {
            dpi = dpiX;
        }

        // The device interface name, e.g. \\?\DISPLAY#IVM1234#5&1a2b… - unique per machine and
        // no further, which is why ScreenRef puts a DeviceId in front of it (Part 3).
        var target = new Native.DisplayDevice { cb = (uint)sizeof(Native.DisplayDevice) };
        var screenId = adapterName;

        if (Native.EnumDisplayDevices(adapterName, 0, ref target, Native.EddGetDeviceInterfaceName))
        {
            var interfaceName = Native.ReadFixed(target.DeviceId, 128);

            if (!string.IsNullOrEmpty(interfaceName))
            {
                screenId = interfaceName;
            }
        }

        // The short Windows name is what a human can read; the interface path is unusable as a
        // caption and never appears in any surface (Part 6).
        var shortName = adapterName.TrimStart('\\', '.');
        var label = $"{deviceName}//{shortName}";
        var isPrimary = origin is { X: 0, Y: 0 };

        return new MonitorInfo(
            new ScreenInfo(
                new ScreenId(screenId),
                label,
                CustomName: null,
                new PixelSize(width, height),
                dpi,
                isPrimary),
            origin.X,
            origin.Y,
            width,
            height);
    }
}
