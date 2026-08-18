namespace DnDOverlay.Core;

/// <summary>
/// The slot bar along one edge of a screen: where parked pictures lie, and how they share the
/// room when there are more of them than places.
/// <para>
/// <b>Parking is the players' tidying gesture</b>, not an edge case, and it has to be as reliable
/// as pushing (Part 6). Which edge is a per-screen setting, because "right" seen from one side of
/// a table is left from the other.
/// </para>
/// <para>
/// The positions are computed from the LIST of parked items rather than stored per item, and that
/// is what makes three promises hold without anybody sending a patch for them: taking one picture
/// out of the bar closes the gap, changing <c>ParkEdge</c> during play moves the whole bar, and a
/// scene loaded onto another screen lays its parked pictures along that screen's edge, in the
/// order they had (Part 11).
/// </para>
/// </summary>
public static class Parking
{
    /// <summary>Where the bar begins and ends along its edge, as a fraction of that edge.</summary>
    /// <remarks>
    /// Not corner to corner, for the same reason the rescue marker keeps away from them (Part 6):
    /// the corners belong to Windows - notification area, start menu, close box, "show desktop".
    /// A picture parked there is either unhittable or hits something else when the finger misses.
    /// </remarks>
    private const double BarStart = 0.1;
    private const double BarEnd = 0.9;

    /// <summary>
    /// Lays every parked item of this scene into its slot, in the order the scene carries them.
    /// Items that are not parked are not touched.
    /// </summary>
    public static SceneState Arrange(SceneState scene, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var count = scene.Items.Count(item => item.Parked);

        if (count == 0)
        {
            return scene;
        }

        var index = 0;

        return scene with
        {
            Items =
            [
                .. scene.Items.Select(item =>
                {
                    if (!item.Parked)
                    {
                        return item;
                    }

                    var centre = SlotCentre(item, index++, count, screen);

                    return item with { CenterX = centre.X, CenterY = centre.Y };
                }),
            ],
        };
    }

    /// <summary>
    /// The centre of one slot, for an item of this shape and size. Across the edge the picture
    /// lies exactly as far out as the edge clamp allows, so a parked picture and one merely pushed
    /// to the edge show the same amount - what tells them apart is that the parked one is IN a
    /// slot, and stays in it (Part 6).
    /// </summary>
    public static Point SlotCentre(SceneItem item, int index, int count, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var hull = Layout.ItemToHullRect(item, screen);

        // A bar on the left or right runs DOWN the screen, so its slots are spaced along Y and the
        // pictures hang off the X axis. Naming the axes rather than the edges is deliberate: the
        // two are perpendicular, and every mix-up here reads as "parking works but sideways".
        var alongY = screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;

        var along = Along(index, count, screen, alongX: !alongY);
        var across = Across(alongY ? hull.Width : hull.Height, screen, acrossX: alongY);

        return alongY ? new Point(across, along) : new Point(along, across);
    }

    /// <summary>
    /// How many pictures fit into the bar side by side before they have to overlap. At 96 DIP a
    /// 1080p table takes nine along its long edge.
    /// </summary>
    public static int Capacity(ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var pitch = Manipulation.Visible(screen, alongX);

        return pitch <= 0 ? 1 : Math.Max(1, (int)Math.Floor((BarEnd - BarStart) / pitch));
    }

    /// <summary>
    /// The position along the bar. Up to the capacity the pictures sit centred in the middle of
    /// the edge at a slot's pitch; beyond it they are spread over the whole bar and overlap.
    /// <para>
    /// <b>Overlapping rather than shrinking</b> is the decision (Part 6): a slot keeps its size,
    /// so every parked picture stays as large as a finger needs, and there is no state in which
    /// something is parked and out of reach. What overcrowding costs is that the pictures cover
    /// one another - which they survive, because taking the top one away closes the bar up again.
    /// </para>
    /// </summary>
    private static double Along(int index, int count, ScreenContext screen, bool alongX)
    {
        var pitch = Manipulation.Visible(screen, alongX);
        var room = BarEnd - BarStart;

        if (pitch <= 0 || count <= 1)
        {
            return (BarStart + BarEnd) / 2;
        }

        var wanted = count * pitch;

        if (wanted <= room)
        {
            // They fit: the group sits in the middle of the edge rather than at its start, so a
            // bar of two does not look like the beginning of a bar of nine.
            //
            // The spare room is worked out FIRST and the shift held inside it, rather than clamping
            // the start between two bounds. At exactly full - nine slots of 96 DIP on a 1080 DIP
            // edge come to 0.79999999999999993 - the upper bound lands a hair below the lower one
            // and Math.Clamp throws. A bar that is exactly full is the normal case, not an edge
            // case, and it must not be the one that ends the evening.
            var spare = room - wanted;
            var shift = Math.Clamp(0.5 - (wanted / 2) - BarStart, 0, spare);

            return BarStart + shift + (pitch * (index + 0.5));
        }

        // More than fit. The first and the last touch the ends of the bar and everything in
        // between is evenly spaced; the step is smaller than a slot, which IS the overlap.
        return BarStart + (pitch / 2) + (index * (room - pitch) / (count - 1));
    }

    /// <summary>
    /// The position across the bar: the outermost place the edge clamp still permits. It is the
    /// same computation <see cref="Manipulation.HoldAtEdge"/> makes, at its limit, and it has to be
    /// - the two disagreeing would let a park slot lie somewhere the clamp immediately pulls back.
    /// </summary>
    private static double Across(double extent, ScreenContext screen, bool acrossX)
    {
        var required = Math.Min(Manipulation.Visible(screen, acrossX), extent);
        var slack = extent / 2;

        return screen.ParkEdge is ParkEdge.Left or ParkEdge.Top
            ? required - slack
            : 1 - required + slack;
    }
}
