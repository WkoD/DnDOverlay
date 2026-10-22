using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// Which screens the control's own window covers - the finding behind
/// <see cref="SuppressReason.ControlWindow"/> (Part 2, Part 3), and what changes when it moves.
/// <para>
/// <b>A screen is held back when three things hold</b>: its device is connected and on THIS machine
/// (by its connection, never by its name or its device path - cloned display PCs share both,
/// Part 3), the control's window lies on it, and it shows an overlay. A screen shown as a window can
/// slip behind the stage like any window, so it is not held back (Part 6).
/// </para>
/// <para>
/// <b>Only our own finding is ever undone</b>: what is released is what this guard held back and no
/// longer wants, and what is suppressed is what it wants and did not hold yet - so a settled window
/// sends nothing, and a release never touches a screen the guard did not hold.
/// </para>
/// <para>
/// The window, the monitors and the network are asked by <see cref="StageGuard"/>; this decides on
/// their answers.
/// </para>
/// </summary>
internal static class CoveredScreens
{
    /// <summary>The screens to hold back, given the devices, this machine's addresses and the monitors the window lies on.</summary>
    /// <param name="presentation">How a screen is shown - an overlay or a window.</param>
    internal static HashSet<ScreenRef> Wanted(
        IReadOnlyList<DeviceView> devices,
        IReadOnlyList<LocalAddress> own,
        IReadOnlySet<ScreenId> covered,
        Func<ScreenRef, ScreenPresentation> presentation)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(covered);
        ArgumentNullException.ThrowIfNull(presentation);

        var wanted = new HashSet<ScreenRef>();

        foreach (var device in devices.Where(device => Here(device, own)))
        {
            foreach (var screen in device.Screens.Where(screen =>
                covered.Contains(screen.Screen.Screen)
                && presentation(screen.Screen) is not ScreenPresentation.Window))
            {
                _ = wanted.Add(screen.Screen);
            }
        }

        return wanted;
    }

    /// <summary>What to suppress and what to release, going from what is held to what is wanted.</summary>
    internal static (IReadOnlyList<ScreenRef> Suppress, IReadOnlyList<ScreenRef> Release) Changes(
        IReadOnlySet<ScreenRef> held, IReadOnlySet<ScreenRef> wanted)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(wanted);

        return ([.. wanted.Where(screen => !held.Contains(screen))], [.. held.Where(screen => !wanted.Contains(screen))]);
    }

    /// <summary>
    /// Whether that device is on THIS machine, and the connection is the only thing that can say
    /// so. A machine name would not do - it is renameable and, on cloned display PCs, identical;
    /// a <see cref="ScreenId"/> would not either, because two machines from one disk image can
    /// report the very same device instance path (Part 3). Without this the control would suppress
    /// a stranger's table because its monitor happens to carry the same path as ours.
    /// <para>
    /// <b>Not the loopback interface</b>, although the plan said so - measured, a display on this
    /// very machine connects to the LAN address, because it takes whichever beacon it hears first
    /// and the beacon goes out on every interface. The rule that holds is the wider one, and it is
    /// no weaker: an address this machine answers on cannot belong to another machine.
    /// </para>
    /// </summary>
    private static bool Here(DeviceView device, IReadOnlyList<LocalAddress> own) =>
        device.Connected && LocalAddresses.IsThisMachine(device.Address, own);
}
