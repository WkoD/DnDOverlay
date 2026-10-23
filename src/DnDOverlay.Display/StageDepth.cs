using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// How high a place lies on this window's stage. Four bands, each above the one before: the table by
/// the hub's depth, the fan above the whole table (<see cref="Parking.Depth"/>), whatever a hand holds
/// or a hand shows out of the fan above both, and the focus above everything (Part 3, Part 6).
/// <para>
/// <b>A held picture lies on top as a local statement about NOW</b>, not as a change to the scene: the
/// hand is on it, so it belongs on top, and the moment the hand leaves it falls back to whatever the hub
/// says. Found at the table (hand-run of M3, step 17): a picture arriving while somebody was pushing
/// another one landed on top of it - the hub hands every new item the next depth up, and a hand had no
/// way to outrank an arrival. Held pictures keep their order among themselves, so two hands on two
/// pictures do not swap them.
/// </para>
/// </summary>
internal static class StageDepth
{
    /// <summary>
    /// How far above the scene a held picture is drawn. Far enough that no depth the hub hands out can
    /// reach it, and an offset rather than a single top value so that two hands on two pictures keep
    /// their order relative to each other.
    /// </summary>
    internal const int HeldAbove = 1 << 20;

    /// <summary>
    /// The focus lies above everything of the scene - the table, the fan, a held picture - with the
    /// dimming one step below it (Part 3). Nothing takes a gesture while it lies, so nothing can outrank
    /// it from the table either.
    /// </summary>
    internal const int FocusAbove = 1 << 22;

    /// <param name="item">The item as it is drawn.</param>
    /// <param name="focusIndex">Its place in the focus, or <see langword="null"/> when it is not in focus.</param>
    /// <param name="inHand">Whether a hand holds it, or shows it out of the fan.</param>
    internal static int Of(SceneItem item, int? focusIndex, bool inHand)
    {
        ArgumentNullException.ThrowIfNull(item);

        return focusIndex is { } place ? FocusAbove + place
            : inHand ? HeldAbove + item.ZOrder
            : Parking.Depth(item);
    }
}
