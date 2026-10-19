using System.Runtime.InteropServices;
using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// The machine's battery, as Windows reports it in the taskbar.
/// </summary>
public static partial class Power
{
    private const byte AcOnline = 1;
    private const byte NoSystemBattery = 128;
    private const byte Unknown = 255;

    /// <summary>
    /// The battery right now, or <see langword="null"/> on a machine without one - and when Windows
    /// cannot say. A device without a battery shows nothing at all, no empty symbol and no
    /// placeholder number (Part 7), so "unknown" has to come out the same way.
    /// </summary>
    public static BatteryInfo? Battery()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return null;
        }

        if ((status.BatteryFlag & NoSystemBattery) != 0
            || status.BatteryFlag == Unknown
            || status.BatteryLifePercent == Unknown)
        {
            return null;
        }

        return new BatteryInfo(Math.Clamp((int)status.BatteryLifePercent, 0, 100), status.ACLineStatus == AcOnline);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte ACLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal uint BatteryLifeTime;
        internal uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
