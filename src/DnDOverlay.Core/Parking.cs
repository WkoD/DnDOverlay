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
    /// Whether a point lies on the fan - within its band along the park edge, whether or not
    /// anything is parked there yet.
    /// <para>
    /// <b>This is what decides parking when a hand lets go</b> (hand-run of M3). Not how much of the
    /// picture is over the edge: a large picture can have half of itself outside and still be
    /// standing squarely on the table, so that reading parked things nobody meant to put away.
    /// Where the HAND is answers it without a threshold, and it answers it the way the table looks
    /// - the fan is drawn over everything, so a picture let go under it could not be picked up
    /// again anyway.
    /// </para>
    /// <para>
    /// The band is as deep as the graspable remainder the edge clamp leaves anything else, because
    /// that is exactly how far the cards lie onto the glass.
    /// </para>
    /// </summary>
    public static bool OnTheFan(Point at, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var band = Manipulation.Visible(screen, !alongX);

        var across = alongX ? at.Y : at.X;

        return screen.ParkEdge is ParkEdge.Left or ParkEdge.Top
            ? across <= band
            : across >= 1 - band;
    }

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

        var edges = Trailing(fan, screen);

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
                    var centre = Centre(stowed, edges[IndexIn(fan, item.ItemId)], screen);

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
    /// <para>
    /// <b>What is left is measured against the LAST CARD'S BODY, not against its slice</b>, and
    /// that correction came from the table (hand-run of M3, A12). Stepping the leading edges over
    /// the whole bar put the last card's body past the end of the screen: thirty pictures ran off
    /// the bottom, and the part hanging out could be seen but not reached, because picking stops
    /// where the bar does. The fan has to END at the bar's end, so the room for the steps is the
    /// bar minus one card.
    /// </para>
    /// </summary>
    public static double Pitch(IReadOnlyList<SceneItem> fan, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(fan);
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var slice = Manipulation.Visible(screen, alongX);
        var room = BarEnd - BarStart;

        if (fan.Count <= 1)
        {
            return slice;
        }

        var body = fan.Max(item => Extent(Stow(item, screen), screen));
        var left = room - body;

        // Stow holds every card to one step short of the bar, so there is always something left to
        // cascade in. The guard stays for a screen so small that one step IS the bar - then nothing
        // can be spread and the leading edges take what there is.
        return left <= 0 ? room / fan.Count : Math.Min(slice, left / (fan.Count - 1));
    }

    /// <summary>
    /// The trailing edge of every card along the fan: the place where the card behind it begins to
    /// show, and therefore both where it is drawn and what it can be touched by.
    /// <para>
    /// <b>Drawing and picking read this one list</b>, and that is the whole point of it. They used
    /// to compute their own: the layout stepped LEADING edges by the pitch while picking read
    /// TRAILING edges, so on a fan of unequal cards the two disagreed about which card was where -
    /// and a card can be reached only where it can be seen (hand-run of M3).
    /// </para>
    /// <para>
    /// <b>Every card shows at least one step of itself.</b> The cascade would otherwise swallow a
    /// short card standing behind a long one whole: measured on a right-hand fan, an 8:1 panorama
    /// between ordinary pictures was invisible AND unreachable, which is the one state parking may
    /// never produce (Part 11). The floor costs nothing where the cards are of a size - then it is
    /// exactly the plain cascade - and it cannot push the fan past the bar, because the pitch is
    /// measured against the longest card there is.
    /// </para>
    /// </summary>
    private static double[] Trailing(IReadOnlyList<SceneItem> fan, ScreenContext screen)
    {
        var pitch = Pitch(fan, screen);
        var edges = new double[fan.Count];

        for (var i = 0; i < fan.Count; i++)
        {
            var own = BarStart + (i * pitch) + Extent(Stow(fan[i], screen), screen);

            edges[i] = i == 0 ? own : Math.Max(own, edges[i - 1] + pitch);
        }

        return edges;
    }

    /// <summary>How long a card is along the fan, in the normalised unit of that axis.</summary>
    private static double Extent(SceneItem stowed, ScreenContext screen)
    {
        var rect = Layout.ItemToRect(stowed, screen);

        return screen.ParkEdge is ParkEdge.Left or ParkEdge.Right ? rect.Height : rect.Width;
    }

    /// <summary>
    /// Which parked picture a point on the fan means, or <see langword="null"/> if the point is not
    /// on the fan at all. The same arithmetic serves the table and the thumbnail (Part 1, rule 9).
    /// <para>
    /// <b>Every card owns the stretch of the fan it can be SEEN over</b> (hand-run of M3). The
    /// first reading stepped the strip by the pitch, which is where the cards' leading edges are -
    /// and a leading edge is the one part of a covered card nobody can see, because the card in
    /// front of it lies exactly there. Reaching for a picture then meant aiming at a place it was
    /// not.
    /// </para>
    /// <para>
    /// So the boundary between two cards is the TRAILING edge of the one in front: up to there the
    /// front card is what the eye has, past it the next one begins to show. The newest card is
    /// covered by nothing and therefore owns its whole body - it has the most room of all, which is
    /// right for the one most likely wanted.
    /// </para>
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

        if (!OnTheFan(at, screen) || along < BarStart || along > BarEnd)
        {
            return null;
        }

        var edges = Trailing(fan, screen);

        for (var i = 0; i < fan.Count - 1; i++)
        {
            if (along <= edges[i])
            {
                return fan[i].ItemId;
            }
        }

        return fan[^1].ItemId;
    }

    /// <summary>
    /// Where a picture is drawn while it is being looked at: <b>at its own place in the fan</b>,
    /// pulled clear across the park edge until the whole of it is on the glass.
    /// <para>
    /// <b>Whole, not half.</b> The point of the peek is that somebody can tell whether this is the
    /// picture they meant, and half a picture answers that question only sometimes. It stays
    /// against the park edge, so the movement that follows - away from the edge - is the one that
    /// takes it onto the table, and the hand never has to go back.
    /// </para>
    /// <para>
    /// <b>Its own place, and the hand has no say in it</b> (hand-run of M3). Two wrong places came
    /// before: under the hand, which dragged the shown card along the fan and made the eye chase
    /// it; and where the hand LANDED, which held still but showed every card of a long run at the
    /// one spot the finger first touched, so the fan turned into a slide viewer. A card steps out
    /// of the fan where it lies in the fan - that is the movement the eye can follow, and running
    /// on shows the next card further along, where it too actually is.
    /// </para>
    /// </summary>
    public static Point? Peek(SceneState scene, ScreenContext screen, ItemId card)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var fan = Fan(scene);

        return fan.FirstOrDefault(item => item.ItemId == card) is not { } found
            ? null
            : Centre(Stow(found, screen), Trailing(fan, screen)[IndexIn(fan, card)], screen, clear: true);
    }

    /// <summary>
    /// How many cards of this fan's size still show a finger's width each. Past that the cards
    /// close up and picking one gets fiddly - it is not a limit on how many can be parked, nothing
    /// is ever refused.
    /// <para>
    /// <b>It reserves the newest card's body, and that correction is worth the parameter.</b> The
    /// number used to be the bar divided by a finger, which says nine on a 1080 table - but the
    /// newest card is covered by nothing and lies there at its whole arrival length, half the bar.
    /// The true count is five, and the fan drops under a finger at SIX cards, not at ten. A number
    /// that answers is worse than none when it answers wrongly.
    /// </para>
    /// </summary>
    public static int Capacity(IReadOnlyList<SceneItem> fan, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(fan);
        ArgumentNullException.ThrowIfNull(screen);

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var slice = Manipulation.Visible(screen, alongX);

        if (slice <= 0)
        {
            return 1;
        }

        var body = fan.Count == 0 ? 0 : fan.Max(item => Extent(Stow(item, screen), screen));

        return 1 + Math.Max(0, (int)Math.Floor((BarEnd - BarStart - body) / slice));
    }

    /// <summary>
    /// The size and angle a picture has while it is in the fan: its arrival size on this screen,
    /// standing straight - <b>but at most one step short of the bar</b>.
    /// <para>
    /// The cap is the one place a parked picture is not at its arrival size, and it exists because
    /// a card longer than the bar cannot lie in the bar. Measured with the shipped defaults
    /// (<c>MaxWidthOnLoad</c> 0.9 against a bar of 0.8): five 4:1 pictures on a TOP park edge ran
    /// 64 % of a screen width off the glass, and every touch on the bar picked the same one of
    /// them - four pictures parked and unreachable at once (hand-run of M3).
    /// </para>
    /// <para>
    /// <b>One step short, not exactly the bar</b>, so there is always something left for the others
    /// to cascade into. It bites only on shapes that could never have been shown in the bar anyway;
    /// everything that fits keeps the size it arrived at.
    /// </para>
    /// </summary>
    private static SceneItem Stow(SceneItem item, ScreenContext screen)
    {
        var stowed = item with
        {
            Scale = Layout.ScaleOnLoad(item.AspectRatio, screen),
            RotationDeg = screen.DefaultRotationDeg,
        };

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var cap = BarEnd - BarStart - Manipulation.Visible(screen, alongX);
        var extent = Extent(stowed, screen);

        return cap <= 0 || extent <= cap ? stowed : stowed with { Scale = stowed.Scale * cap / extent };
    }

    /// <summary>
    /// The centre of one card, hung on its trailing edge. Across the fan it is the outermost place
    /// the edge clamp still permits - or, when the card is being looked at, the innermost place
    /// that has the whole of it on the glass.
    /// </summary>
    private static Point Centre(
        SceneItem stowed, double trailing, ScreenContext screen, bool clear = false)
    {
        var rect = Layout.ItemToRect(stowed, screen);

        // A fan on the left or right runs DOWN the screen, so its cards are stepped along Y and
        // hang off the X axis. Naming the axes rather than the edges is deliberate: the two are
        // perpendicular, and every mix-up here reads as "parking works but sideways".
        var alongY = screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;

        var extent = alongY ? rect.Height : rect.Width;
        var breadth = alongY ? rect.Width : rect.Height;
        var along = trailing - (extent / 2);

        var across = Across(breadth, screen, acrossX: alongY);

        if (clear)
        {
            // Clear of the fan is the mirror of lying in it: the same body, the whole of it inside
            // the screen instead of the least of it.
            across = screen.ParkEdge is ParkEdge.Left or ParkEdge.Top
                ? breadth / 2
                : 1 - (breadth / 2);
        }

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
