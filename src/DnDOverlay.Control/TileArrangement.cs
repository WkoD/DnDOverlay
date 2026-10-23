using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control;

/// <summary>
/// The order of the tiles on the stage, as the DM arranged it (Part 7, Prüfschritt 24b).
/// <para>
/// <b>What is not in the saved order is new</b>, and new hangs itself on the end - a screen pushing into
/// the middle of an arrangement the DM built would be worse than one he has to place himself. <b>A saved
/// place with no screen behind it is kept</b>: it is the screen that is unplugged right now, and it takes
/// its place again when it comes back - also through a rearrangement made while it was away.
/// </para>
/// </summary>
internal static class TileArrangement
{
    /// <summary>The screens in the saved order, anything new at the end in the order it came.</summary>
    internal static IReadOnlyList<ScreenView> Arranged(IReadOnlyList<ScreenView> screens, IReadOnlyList<ScreenKey> saved)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(saved);

        if (saved.Count == 0)
        {
            return screens;
        }

        var places = saved
            .Select((key, index) => (Key: key, Index: index))
            .ToDictionary(entry => entry.Key, entry => entry.Index);

        return
        [
            .. screens.OrderBy(view =>
                places.TryGetValue(Key(view.Screen), out var place) ? place : int.MaxValue),
        ];
    }

    /// <summary>
    /// The order with one tile dragged onto another: it takes the place the target had, and the others
    /// make way. Null when either is not on the stage.
    /// </summary>
    internal static List<ScreenRef>? Moved(IReadOnlyList<ScreenRef> order, ScreenRef taken, ScreenRef target)
    {
        ArgumentNullException.ThrowIfNull(order);

        var moved = order.ToList();
        var from = moved.IndexOf(taken);
        var to = moved.IndexOf(target);

        if (from < 0 || to < 0)
        {
            return null;
        }

        moved.RemoveAt(from);
        moved.Insert(to, taken);

        return moved;
    }

    /// <summary>What is written when the hand lets go: the screens that are here, then the saved places of the ones that are not.</summary>
    internal static IReadOnlyList<ScreenKey> Kept(IReadOnlyList<ScreenRef> order, IReadOnlyList<ScreenKey> saved)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(saved);

        var here = order.Select(Key).ToList();

        return [.. here, .. saved.Where(key => !here.Contains(key))];
    }

    /// <summary>
    /// "Reset the arrangement" (Part 7): the order by device name, and within a device by screen name -
    /// ignoring case, as the device list sorts (Part 3). It covers the screens on the stage AND the saved
    /// places of the ones that are not: an unplugged screen takes its place by name as well, from the
    /// names <c>control.json</c> keeps, and stands there when it comes back. A saved place whose screen
    /// has no name anywhere goes to the end, in the order it had.
    /// <para>
    /// The devices' screens stay together under one device even if two had the same name - the
    /// device is the second key, its name the first.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<ScreenKey> Reset(
        IReadOnlyList<ScreenView> screens,
        IReadOnlyList<ScreenKey> saved,
        IReadOnlyList<KnownScreen> knownScreens,
        IReadOnlyList<KnownDevice> knownDevices)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(knownScreens);
        ArgumentNullException.ThrowIfNull(knownDevices);

        var here = screens.ToDictionary(view => Key(view.Screen), view => view.Info.Label);
        var devices = knownDevices.ToDictionary(device => device.DeviceId, device => device.Name);
        var labels = knownScreens.ToDictionary(screen => new ScreenKey(screen.DeviceId, screen.ScreenId), screen => screen.Label);
        var keys = here.Keys.Concat(saved.Where(key => !here.ContainsKey(key))).Distinct().ToList();

        string? Label(ScreenKey key) => here.TryGetValue(key, out var label) ? label : labels.GetValueOrDefault(key);

        var named = keys.Where(key => Label(key) is not null).ToList();
        var comparer = StringComparer.CurrentCultureIgnoreCase;

        return
        [
            .. named
                .OrderBy(key => devices.GetValueOrDefault(key.DeviceId, string.Empty), comparer)
                .ThenBy(key => key.DeviceId)
                .ThenBy(key => Label(key), comparer)
                .ThenBy(key => key.ScreenId, StringComparer.Ordinal),
            .. keys.Except(named),
        ];
    }

    private static ScreenKey Key(ScreenRef screen) => new(screen.Device.Value, screen.Screen.Value);
}
