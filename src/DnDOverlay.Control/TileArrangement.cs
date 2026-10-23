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

    private static ScreenKey Key(ScreenRef screen) => new(screen.Device.Value, screen.Screen.Value);
}
