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

        var body = fan.Max(item => Shown(Stow(item, screen), screen));
        var left = room - body;

        // A card shows at most one step short of the bar, so there is always something left to
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
            var own = BarStart + (i * pitch) + Shown(Stow(fan[i], screen), screen);

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
    /// The window the fan shows of one card, when the card is longer than the bar can hold - as
    /// fractions of the card's own length, measured from its head end.
    /// <para>
    /// <b>Cut, not shrunk</b>, and that is a correction from the table (hand-run of M3, N11). The
    /// fan used to hold an over-long card by reducing its SCALE, so a tall picture sat small in the
    /// bar and then grew back to its arrival size the moment it was pulled out - it jumped under
    /// the hand. Cutting keeps the scale exactly as it always was: the card shows less of itself in
    /// the fan and unfolds when it is looked at. Nothing is ever resized, at any point.
    /// </para>
    /// <para>
    /// <b>And it is a window, not a tail</b> - the second correction from the same row. The card
    /// keeps its own place, so the window is wherever the fan's slot happens to fall ON it, which
    /// for a long card means it is cut at BOTH ends. That is what lets the picture unfold without
    /// moving: it grows out of the window in both directions instead of sliding to a place where it
    /// fits. The peek used to hold an over-long card onto the screen, and holding is moving.
    /// </para>
    /// </summary>
    /// <param name="From">Where the window starts, as a fraction of the card's length. Nought is the head.</param>
    /// <param name="To">Where it ends. One is the tail.</param>
    /// <param name="Fade">
    /// How far each cut edge fades out, as a fraction of the card's length - <b>so that a cut edge
    /// reads as "there is more of this" rather than as the edge of the picture</b>. It is one step
    /// wide, the same finger the whole fan is measured in, and it is the arrival fade turned from
    /// time into space: a picture coming in fades from nothing to itself, a cut card fades from
    /// itself to nothing.
    /// </param>
    public readonly record struct Cut(double From, double To, double Fade)
    {
        /// <summary>Nothing is cut: the card lies in the fan whole.</summary>
        public static Cut Whole { get; } = new(0, 1, 0);

        /// <summary>Whether there is anything to cut at all.</summary>
        public bool IsWhole => From <= 0 && To >= 1;
    }

    /// <summary>
    /// What the fan shows of this card. <see cref="Cut.Whole"/> for anything that fits, which is
    /// every ordinary picture - the cut bites on shapes that could never have lain in the bar.
    /// </summary>
    public static Cut CutOf(SceneState scene, ScreenContext screen, ItemId card)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var fan = Fan(scene);

        if (fan.FirstOrDefault(item => item.ItemId == card) is not { } found)
        {
            return Cut.Whole;
        }

        var stowed = Stow(found, screen);
        var full = Extent(stowed, screen);
        var shown = Shown(stowed, screen);

        if (full <= 0 || shown >= full)
        {
            return Cut.Whole;
        }

        var trailing = Trailing(fan, screen)[IndexIn(fan, card)];
        var along = AlongOf(stowed, trailing, screen);

        // The window is the fan's slot; the card lies where it lies. Both in screen units first,
        // then read off against the card's own length - which is what a renderer can use without
        // knowing anything about fans.
        var head = along - (full / 2);
        var from = (trailing - shown - head) / full;
        var to = (trailing - head) / full;

        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var fade = Math.Min(Manipulation.Visible(screen, alongX), shown / 2) / full;

        return new Cut(Math.Max(0, from), Math.Min(1, to), fade);
    }

    /// <summary>
    /// Where a card's whole body lies along the fan: the place the cascade gives it, <b>held on the
    /// screen</b> so that unfolding it never has to move it.
    /// <para>
    /// A card longer than the screen is centred and runs off both ends, which is the honest answer -
    /// it cannot be shown whole anywhere, so it is shown where the most of it is.
    /// </para>
    /// </summary>
    private static double AlongOf(SceneItem stowed, double trailing, ScreenContext screen)
    {
        var full = Extent(stowed, screen);

        return full >= 1 ? 0.5 : Math.Clamp(trailing - (full / 2), full / 2, 1 - (full / 2));
    }

    /// <summary>
    /// The size and angle a picture has while it is in the fan: its arrival size on this screen,
    /// standing straight - <b>and held between the same bounds every other size is held between</b>.
    /// <para>
    /// The clamp is not decoration, it closes a hole the table found (hand-run of M3, N11): a very
    /// tall picture parked SMALLER and then grew when it was pulled out. Two rules, each right on
    /// its own, disagreeing at their intersection (`G15`). <see cref="Layout.ScaleOnLoad"/> has no
    /// lower bound - deliberately, because on an extreme shape that bound explodes and a picture
    /// that does not FIT is unusable for everyone (M2b). <see cref="Layout.ClampScale"/> keeps its
    /// lower bound where it belongs, at the gesture, so the DM cannot zoom a picture away. The fan
    /// used only the first, so a parked card could sit BELOW a size the gesture clamp accepts - and
    /// the first transform after it came out snapped it up. Measured on a 1080 table: a 1:10 tower
    /// parked at 0.400 and came out at 0.740, nearly double, under the hand.
    /// </para>
    /// <para>
    /// So the fan asks for a size the rest of the system already agrees with. It costs nothing for
    /// any ordinary shape - the lower bound bites only on portraits - and where it makes a card
    /// longer than the bar, <see cref="CutOf"/> takes over and shows less of it rather than
    /// shrinking it.
    /// </para>
    /// <para>
    /// <b>And it fills the band across, because a card that does not is a card with a gap beside
    /// it</b> (hand-run of M3, N11). <c>MinScale</c> means "80 DIP on the shorter edge" and the
    /// band is 96 DIP deep, so a tower came to lie 80 DIP into a 96 DIP band and the older cards
    /// showed through the 16 DIP that were left. The fan's measure is the stricter of the two and
    /// it is the one that counts here: the band is as deep as a FINGER, and a card that does not
    /// reach across it is a card that cannot be grabbed reliably either. The card is drawn to the
    /// band and the length that comes with it is cut - pulled out, it is that same size, so
    /// nothing jumps.
    /// </para>
    /// </summary>
    private static SceneItem Stow(SceneItem item, ScreenContext screen)
    {
        var stowed = item with
        {
            Scale = Layout.ClampScale(
                Layout.ScaleOnLoad(item.AspectRatio, screen), item.AspectRatio, screen),
            RotationDeg = screen.DefaultRotationDeg,
        };

        // A fan on a side hangs its cards off the X axis, one on the top or bottom off Y - the band
        // is measured ACROSS the fan, which is the axis the cascade does not run along.
        var alongY = screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;
        var rect = Layout.ItemToRect(stowed, screen);

        var breadth = alongY ? rect.Width : rect.Height;
        var band = Manipulation.Visible(screen, alongY);

        return breadth <= 0 || breadth >= band
            ? stowed
            : stowed with { Scale = stowed.Scale * band / breadth };
    }

    /// <summary>The longest a card may be along the fan, leaving one step for the others.</summary>
    private static double Cap(ScreenContext screen) =>
        BarEnd - BarStart - Manipulation.Visible(screen, screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom);

    /// <summary>
    /// How much of a card the fan shows of itself along its length - its whole body, or the cap if
    /// the body is longer than that. <b>This, not the body, is what the cascade is built from</b>,
    /// because it is what the eye has.
    /// </summary>
    private static double Shown(SceneItem stowed, ScreenContext screen)
    {
        var cap = Cap(screen);
        var extent = Extent(stowed, screen);

        return cap <= 0 ? extent : Math.Min(extent, cap);
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

        var breadth = alongY ? rect.Width : rect.Height;

        // The SAME place whether the card is lying in the fan or being looked at. Only the across
        // moves, and that is the whole of the peek: a card steps out of the bar, it does not travel
        // along it (hand-run of M3, N11).
        var along = AlongOf(stowed, trailing, screen);
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
