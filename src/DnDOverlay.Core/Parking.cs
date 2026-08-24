namespace DnDOverlay.Core;

/// <summary>
/// The fan of parked pictures along one edge of a screen: where they lie, in what order, which one
/// a finger on the fan means, and where that one is shown while it is being looked at.
/// <para>
/// <b>Parking is the players' tidying gesture</b>, not an edge case, and it has to be as reliable
/// as pushing (Part 6). Which edge is a per-screen setting, because "right" seen from one side of
/// a table is left from the other.
/// </para>
/// <para>
/// The positions are computed from the LIST of parked items rather than stored per item, and that
/// is what makes three promises hold without anybody sending a patch for them: taking one picture
/// out of the fan closes the gap, changing <c>ParkEdge</c> during play moves the whole fan, and a
/// scene loaded onto another screen lays its parked pictures along that screen's edge, in the
/// order they had (Part 11).
/// </para>
/// <para>
/// <b>Rebuilt at the end of M3, and the shape is a fan of cards rather than a row of slots.</b> The
/// row of slots kept every picture at its own size and let them overlap once there were more than
/// nine; past thirty the visible sliver of each was fifteen DIP, which is parked and out of reach
/// at the same time. A fan says three things a row could not: a picture in it is at the size it
/// arrived at, so the fan is even; the newest lies on top at the near end, so the one most likely
/// wanted is where the hand goes first; and <b>the length of the fan is the count</b> - nobody has
/// to be told how many pictures are put away.
/// </para>
/// </summary>
public static class Parking
{
    /// <summary>Where the fan begins and ends along its edge, as a fraction of that edge.</summary>
    /// <remarks>
    /// Not corner to corner, for the same reason the rescue marker keeps away from them (Part 6):
    /// the corners belong to Windows - notification area, start menu, close box, "show desktop".
    /// A picture parked there is either unhittable or hits something else when the finger misses.
    /// <para>
    /// It bounds the TOUCH TARGETS, not the pixels. A parked picture is drawn at the size it
    /// arrived at, which is far longer than its slice of the fan, so the body of the last one
    /// reaches past the end. That is a matter of looks; what has to stay out of the corners is
    /// the place a finger must land, and every one of those lies between these two numbers.
    /// </para>
    /// </remarks>
    private const double BarStart = 0.1;
    private const double BarEnd = 0.9;

    /// <summary>
    /// How far above every unparked picture the fan is drawn. <b>The fan lies over the table, and
    /// that is the reversal of an earlier rule.</b>
    /// <para>
    /// Parking used to keep an item's depth so that a parked picture "would not cover the table it
    /// was tidied off". That reads well and is wrong: a parked picture is PUT AWAY, and the one
    /// thing the players must always be able to reach is the way to get it back. A fan lying under
    /// a full table is a drawer with a wardrobe in front of it.
    /// </para>
    /// <para>
    /// Coming back out is the ordinary rule and needs no exception: a picture pulled from the fan
    /// has been touched, so it goes to the front like anything else that is touched.
    /// </para>
    /// </summary>
    public const int FanAbove = 1 << 18;

    /// <summary>
    /// The parked pictures in the fan's own order: <b>newest first</b>. The near end of the fan is
    /// the top edge on a side bar and the left edge on a top or bottom one, and that is where the
    /// most recently parked picture lies - the one a hand reaching for the fan most likely wants.
    /// </summary>
    public static IReadOnlyList<SceneItem> Fan(SceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return [.. scene.Items.Where(item => item.Parked).OrderByDescending(item => item.ParkedAt)];
    }

    /// <summary>
    /// How deep an item is drawn, for a renderer that has to put the scene in order. Both ends use
    /// it, so the table and the thumbnail agree on what covers what (Part 1, rule 9).
    /// </summary>
    public static int Depth(SceneState scene, SceneItem item)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(item);

        if (!item.Parked)
        {
            return item.ZOrder;
        }

        var fan = Fan(scene);

        // Oldest lowest, so the newest ends up on top of the pile - and the fan as a whole above
        // everything on the table.
        return FanAbove + (fan.Count - 1 - IndexIn(fan, item.ItemId));
    }

    /// <summary>
    /// Lays every parked item of this scene into the fan, in the order the fan keeps them. Items
    /// that are not parked are not touched.
    /// <para>
    /// <b>It sets the size and the angle too, and it does so every time.</b> A parked picture is at
    /// the size it arrives at on THIS screen and stands straight; that is what makes the fan even
    /// and what lets a scene move to a screen with a different shape and still have a fan rather
    /// than a heap. The price is written down where it belongs (Part 6): the size and the angle a
    /// picture had on the table are spent when it is parked, and it comes back out the way it went
    /// in. Nothing is remembered, deliberately - a picture that changed size in the moment it is
    /// pulled from the fan could not be dragged in one movement, and dragging it out IS the way
    /// back.
    /// </para>
    /// </summary>
    public static SceneState Arrange(SceneState scene, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var fan = Fan(scene);

        if (fan.Count == 0)
        {
            return scene;
        }

        var pitch = Pitch(fan.Count, screen);

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

                    var stowed = Stow(item, screen);
                    var centre = Centre(stowed, IndexIn(fan, item.ItemId), pitch, screen);

                    return stowed with { CenterX = centre.X, CenterY = centre.Y };
                }),
            ],
        };
    }

    /// <summary>
    /// The step from one card of the fan to the next, in the normalised unit of the axis the fan
    /// runs along.
    /// <para>
    /// <b>A finger's width until the fan is full, and after that whatever is left.</b> Up to that
    /// point every picture shows as much of itself as the edge clamp would leave it, so the fan is
    /// a row anybody can pick from directly. Beyond it the cards close up with no floor under
    /// them - which is the whole reason the fan can be TAKEN HOLD OF: once the slices are thinner
    /// than a finger, picking one is the fan's job and not the eye's.
    /// </para>
    /// </summary>
    public static double Pitch(int count, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var slice = Manipulation.Visible(screen, alongX);
        var room = BarEnd - BarStart;

        return count <= 1 ? slice : Math.Min(slice, room / count);
    }

    /// <summary>
    /// Which parked picture a point on the fan means, or <see langword="null"/> if the point is not
    /// on the fan at all. The same arithmetic serves the table and the thumbnail (Part 1, rule 9).
    /// </summary>
    public static ItemId? Pick(SceneState scene, ScreenContext screen, Point at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var fan = Fan(scene);

        if (fan.Count == 0)
        {
            return null;
        }

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var along = alongX ? at.X : at.Y;
        var across = alongX ? at.Y : at.X;

        // Across the fan: the strip the cards actually show, which is what the edge clamp leaves.
        var depth = Manipulation.Visible(screen, !alongX);
        var outside = screen.ParkEdge is ParkEdge.Left or ParkEdge.Top
            ? across > depth
            : across < 1 - depth;

        if (outside || along < BarStart || along > BarEnd)
        {
            return null;
        }

        var pitch = Pitch(fan.Count, screen);
        var index = pitch <= 0 ? 0 : (int)Math.Floor((along - BarStart) / pitch);

        return fan[Math.Clamp(index, 0, fan.Count - 1)].ItemId;
    }

    /// <summary>
    /// Where a picture is drawn while it is being looked at: pulled clear of the fan towards the
    /// middle of the screen until the whole of it is on the glass, and still in its own place along
    /// the fan.
    /// <para>
    /// <b>Whole, not half.</b> The point of the peek is that somebody can tell whether this is the
    /// picture they meant, and half a picture answers that question only sometimes. It stays
    /// against the park edge, so the movement that follows - away from the edge - is the one that
    /// takes it onto the table, and the hand never has to go back.
    /// </para>
    /// </summary>
    public static Point Peek(SceneItem item, int index, int count, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var stowed = Stow(item, screen);
        var rect = Layout.ItemToRect(stowed, screen);
        var alongY = screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;
        var extent = alongY ? rect.Width : rect.Height;

        var along = Centre(stowed, index, Pitch(count, screen), screen);
        var across = screen.ParkEdge is ParkEdge.Left or ParkEdge.Top
            ? extent / 2
            : 1 - (extent / 2);

        return alongY ? new Point(across, along.Y) : new Point(along.X, across);
    }

    /// <summary>
    /// Where a picture is peeked at, resolved from the scene. The overload every caller should
    /// reach for; the indexed one exists because the layout needs it too.
    /// </summary>
    public static Point? Peek(SceneState scene, ScreenContext screen, ItemId card)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var fan = Fan(scene);

        for (var i = 0; i < fan.Count; i++)
        {
            if (fan[i].ItemId == card)
            {
                return Peek(fan[i], i, fan.Count, screen);
            }
        }

        return null;
    }

    /// <summary>
    /// How far a hand has moved AWAY from the park edge and how far ALONG it, both in DIP.
    /// <para>
    /// The two together are what tells the two halves of the fan gesture apart: running along the
    /// fan chooses a card, and pulling away from it takes that card onto the table. Both ends need
    /// this arithmetic - the table now and the thumbnail in M4 - so it lives here.
    /// </para>
    /// </summary>
    public static (double Away, double Along) Pull(ScreenContext screen, Point from, Point to)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var dx = (to.X - from.X) * screen.WidthInDip;
        var dy = (to.Y - from.Y) * screen.HeightInDip;

        return screen.ParkEdge switch
        {
            ParkEdge.Left => (dx, dy),
            ParkEdge.Right => (-dx, dy),
            ParkEdge.Top => (dy, dx),
            _ => (-dy, dx),
        };
    }

    /// <summary>
    /// How far away from the edge a hand has to move before the peeked card comes out of the fan,
    /// in DIP.
    /// <para>
    /// <b>Small on purpose, because the direction carries the decision and not the distance.</b>
    /// Running along the fan and pulling away from it are perpendicular; what this number guards
    /// against is a wobble, not a mistake. A proposal until the closing run of M3 has had fingers
    /// on it (Guide G6).
    /// </para>
    /// </summary>
    public const double PullOutDip = 24;

    /// <summary>
    /// Whether this movement means "take this card out" rather than "show me the next one". Both
    /// halves are needed: far enough away from the edge, and more away than along.
    /// </summary>
    public static bool PullsOut(ScreenContext screen, Point from, Point to)
    {
        var (away, along) = Pull(screen, from, to);

        return away > PullOutDip && away > Math.Abs(along);
    }

    /// <summary>
    /// How many pictures the fan shows at a finger's width each, before the cards start closing up.
    /// It is not a limit on how many can be parked - nothing is ever refused - only the point past
    /// which picking one is the fan's job rather than the eye's.
    /// </summary>
    public static int Capacity(ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var slice = Manipulation.Visible(screen, alongX);

        return slice <= 0 ? 1 : Math.Max(1, (int)Math.Floor((BarEnd - BarStart) / slice));
    }

    /// <summary>The size and angle a picture has while it is in the fan.</summary>
    private static SceneItem Stow(SceneItem item, ScreenContext screen) =>
        item with
        {
            Scale = Layout.ScaleOnLoad(item.AspectRatio, screen),
            RotationDeg = screen.DefaultRotationDeg,
        };

    /// <summary>
    /// The centre of one card. Along the fan it is the card's own leading edge set at its step from
    /// the near end; across it, the outermost place the edge clamp still permits.
    /// </summary>
    private static Point Centre(SceneItem stowed, int index, double pitch, ScreenContext screen)
    {
        var rect = Layout.ItemToRect(stowed, screen);

        // A fan on the left or right runs DOWN the screen, so its cards are stepped along Y and
        // hang off the X axis. Naming the axes rather than the edges is deliberate: the two are
        // perpendicular, and every mix-up here reads as "parking works but sideways".
        var alongY = screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;

        var extent = alongY ? rect.Height : rect.Width;
        var along = BarStart + (index * pitch) + (extent / 2);
        var across = Across(alongY ? rect.Width : rect.Height, screen, acrossX: alongY);

        return alongY ? new Point(across, along) : new Point(along, across);
    }

    private static int IndexIn(IReadOnlyList<SceneItem> fan, ItemId item)
    {
        for (var i = 0; i < fan.Count; i++)
        {
            if (fan[i].ItemId == item)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The position across the fan: the outermost place the edge clamp still permits. It is the
    /// same computation <see cref="Manipulation.HoldAtEdge"/> makes, at its limit, and it has to be
    /// - the two disagreeing would let a card lie somewhere the clamp immediately pulls back.
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
