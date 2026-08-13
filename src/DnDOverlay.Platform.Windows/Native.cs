using System.Runtime.InteropServices;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// The Win32 surface for the questions this project answers. Only the ASKING half is here -
/// what changes a window of our own stays with that window in its application (Part 2).
/// <para>
/// <c>LibraryImport</c> rather than <c>DllImport</c>; its generated marshalling code is what
/// <c>AllowUnsafeBlocks</c> is set for.
/// </para>
/// </summary>
internal static unsafe partial class Native
{
    internal const int EnumCurrentSettings = -1;
    internal const uint EddGetDeviceInterfaceName = 0x00000001;
    internal const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    internal const uint DisplayDeviceMirroringDriver = 0x00000008;
    internal const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        internal uint cb;
        internal fixed char DeviceName[32];
        internal fixed char DeviceString[128];
        internal uint StateFlags;
        internal fixed char DeviceId[128];
        internal fixed char DeviceKey[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointL
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RectL
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <summary>
    /// The window in PHYSICAL pixels of the virtual desktop - the same space the monitor bounds
    /// come in, so the two can be compared without a DPI calculation anywhere. Asking WPF for the
    /// rectangle would give device-independent units and need one, per monitor, at mixed scaling:
    /// three chances to be wrong for no gain.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out RectL rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    /// <summary>
    /// The rectangle the eye sees, which is NOT what <c>GetWindowRect</c> returns: since Windows 10
    /// that one includes the invisible drop-shadow border, about seven pixels to the left, right
    /// and bottom. A window snapped flush against the edge of a monitor therefore reaches seven
    /// pixels into the next one, and anything asking "which screens does this lie on" answers with
    /// one screen too many.
    /// <para>
    /// The plan names this trap for the foreign windows of Part 6; it is the same trap here, and
    /// the same answer.
    /// </para>
    /// </summary>
    internal const int DwmwaExtendedFrameBounds = 9;

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, int attribute, out RectL value, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        internal fixed char dmDeviceName[32];
        internal ushort dmSpecVersion;
        internal ushort dmDriverVersion;
        internal ushort dmSize;
        internal ushort dmDriverExtra;
        internal uint dmFields;
        internal PointL dmPosition;
        internal uint dmDisplayOrientation;
        internal uint dmDisplayFixedOutput;
        internal short dmColor;
        internal short dmDuplex;
        internal short dmYResolution;
        internal short dmTTOption;
        internal short dmCollate;
        internal fixed char dmFormName[32];
        internal ushort dmLogPixels;
        internal uint dmBitsPerPel;
        internal uint dmPelsWidth;
        internal uint dmPelsHeight;
        internal uint dmDisplayFlags;
        internal uint dmDisplayFrequency;
        internal uint dmICMMethod;
        internal uint dmICMIntent;
        internal uint dmMediaType;
        internal uint dmDitherType;
        internal uint dmReserved1;
        internal uint dmReserved2;
        internal uint dmPanningWidth;
        internal uint dmPanningHeight;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevices(
        string? device,
        uint devNum,
        ref DisplayDevice displayDevice,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(PointL point, uint flags);

    /// <summary>
    /// Effective DPI of one monitor. Mixed scaling across the screens of one device is the
    /// normal case - the Surface brings its own high-DPI panel and drives the table beside it -
    /// so every overlay has to compute with the values of ITS monitor (Part 6).
    /// </summary>
    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    internal static string ReadFixed(char* buffer, int length)
    {
        var span = new ReadOnlySpan<char>(buffer, length);
        var end = span.IndexOf('\0');

        return new string(end < 0 ? span : span[..end]);
    }
}
