using System.Runtime.InteropServices;

namespace DnDOverlay.Display;

/// <summary>
/// The Win32 surface the display needs. It lives in the APPLICATION and nowhere else: the five
/// libraries must not call into native code at all, and the architecture test enforces that
/// (Part 2, Part 11).
/// <para>
/// <c>LibraryImport</c> rather than <c>DllImport</c> - the generated marshalling code is what
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

    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;

    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint newLong);

    internal static string ReadFixed(char* buffer, int length)
    {
        var span = new ReadOnlySpan<char>(buffer, length);
        var end = span.IndexOf('\0');

        return new string(end < 0 ? span : span[..end]);
    }
}
