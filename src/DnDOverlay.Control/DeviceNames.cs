using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// Whether a name typed in the device band is taken. <b>Device and screen names are unique across all
/// devices</b> (Part 3): two indistinguishable labels confuse in three places - the tiles, the target
/// lists, the log bubbles - and cost a wrong decision in one. Compared without case, as the DM reads them.
/// <para>
/// The device that is coming in is asked by <see cref="PairingDesk.TakenNames"/>, which leaves its own
/// screens out; a rename in the band asks here.
/// </para>
/// </summary>
internal static class DeviceNames
{
    /// <summary>
    /// A screen's own name: taken when any device carries it, or any OTHER screen's label. Empty is
    /// never taken - it gives the default label back.
    /// </summary>
    internal static bool ScreenNameTaken(IReadOnlyList<DeviceView> all, ScreenRef screen, string typed)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(typed);

        var taken = all.Select(other => other.Name)
            .Concat(all.SelectMany(other => other.Screens).Where(other => other.Screen != screen).Select(other => other.Info.Label));

        return typed.Length > 0 && taken.Contains(typed, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A device's name: taken when another device carries it, or any screen's label - its own screens' too.</summary>
    internal static bool DeviceNameTaken(IReadOnlyList<DeviceView> all, DeviceId device, string typed)
    {
        ArgumentNullException.ThrowIfNull(all);

        var taken = all.Where(other => other.Device != device).Select(other => other.Name)
            .Concat(all.SelectMany(other => other.Screens).Select(screen => screen.Info.Label));

        return taken.Contains(typed, StringComparer.OrdinalIgnoreCase);
    }
}
