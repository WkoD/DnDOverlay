using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The fan of parked pictures. The promise being tested is a single sentence from Part 11 -
/// "parked items are always reachable, even when there are more of them than places" - and it is
/// the one the players find out about on their own.
/// <para>
/// Rebuilt at the end of M3: what used to be a row of slots is a fan of cards. The row let every
/// picture keep its own size and overlapped once there were more than nine; past thirty the
/// visible sliver was fifteen DIP, which is parked and out of reach at once. The fan is even, its
/// newest card lies at the near end, and its LENGTH is the count.
/// </para>
/// </summary>
public sealed class ParkingTests
{
    /// <summary>Parked pictures, oldest first in the scene, each with its own place in the fan.</summary>
    private static SceneState Parked(int count, ScreenContext screen) =>
        Parking.Arrange(
            Build.SceneWith(
                [.. Enumerable.Range(0, count).Select(index => Build.Item(parked: true, parkedAt: index + 1))]),
            screen);

    /// <summary>How far a card sits along its fan, whichever axis that fan runs on.</summary>
    private static double Along(SceneItem item, ScreenContext screen) =>
        screen.ParkEdge is ParkEdge.Left or ParkEdge.Right ? item.CenterY : item.CenterX;

    /// <summary>
    /// The middle of the stretch of the fan card <paramref name="index"/> can be SEEN over. The
    /// newest card is covered by nothing and owns its whole body; every one behind it shows from
    /// the trailing edge of the card in front to its own, which is one pitch.
    /// </summary>
    private static Point Probe(int index, SceneState scene, ScreenContext screen)
    {
        var fan = Parking.Fan(scene);
        var body = Layout.ItemToRect(fan[0], screen).Height;
        var pitch = Parking.Pitch(fan, screen);

        var along = index == 0
            ? 0.1 + (body / 2)
            : 0.1 + body + ((index - 0.5) * pitch);

        return new Point(0.99, along);
    }

    /// <summary>
    /// <b>The near end of the fan is where the newest card is</b>, and the fan is ordered by the
    /// moment of parking rather than by when the picture came onto the table. That difference is
    /// the whole reason the field exists: the hand goes to the same place every time and finds the
    /// picture most likely wanted.
    /// </summary>
    [Fact]
    public void The_newest_card_lies_at_the_near_end()
    {
        var screen = Build.Screen();
        var fan = Parking.Fan(Parked(5, screen));

        Assert.Equal(5, fan[0].ParkedAt);
        Assert.Equal([5L, 4, 3, 2, 1], [.. fan.Select(item => item.ParkedAt)]);

        var places = fan.Select(item => Along(item, screen)).ToList();

        Assert.Equal([.. places.Order()], places);
    }

    /// <summary>And it lies on top, so the newest is the one that is fully visible.</summary>
    [Fact]
    public void The_newest_card_lies_on_top_and_the_whole_fan_over_the_table()
    {
        var screen = Build.Screen();
        var table = Build.Item(zOrder: 5000);
        var scene = Parking.Arrange(
            Build.SceneWith(table, Build.Item(parked: true, parkedAt: 1), Build.Item(parked: true, parkedAt: 2)),
            screen);

        var fan = Parking.Fan(scene);

        Assert.True(Parking.Depth(scene, fan[0]) > Parking.Depth(scene, fan[1]));
        Assert.True(Parking.Depth(scene, fan[1]) > Parking.Depth(scene, table));
    }

    /// <summary>
    /// <b>Every touch target lies inside the fan's span</b>, whatever the count. The corners belong
    /// to Windows - notification area, start menu, close box - so a place a finger must land may
    /// not be there. The BODY of a card may reach past the end; that is a matter of looks.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(20)]
    [InlineData(60)]
    public void Every_card_can_be_picked_from_inside_the_fan(int count)
    {
        var screen = Build.Screen();
        var scene = Parked(count, screen);
        var fan = Parking.Fan(scene);

        var picked = Enumerable.Range(0, count)
            .Select(index => Parking.Pick(scene, screen, Probe(index, scene, screen)))
            .ToList();

        Assert.Equal([.. fan.Select(item => (ItemId?)item.ItemId)], picked);
        Assert.All(picked, item => Assert.NotNull(item));

        // And the whole fan lies inside the bar - body included, which is what A12 found: stepping
        // the leading edges over the bar let the last card hang off the screen, visible and
        // unreachable at once.
        foreach (var card in scene.Items.Where(item => item.Parked))
        {
            var rect = Layout.ItemToRect(card, screen);

            Assert.InRange(rect.Y, -1e-9, 1);
            Assert.InRange(rect.Y + rect.Height, 0, 0.9 + 1e-9);
        }
    }

    /// <summary>
    /// The fan grows with the count until it fills its edge, and only then do the cards close up.
    /// <b>That is what makes the length of the fan the number of pictures</b> - and once the slices
    /// are thinner than a finger, picking one is the fan's job rather than the eye's.
    /// </summary>
    [Fact]
    public void The_fan_grows_with_the_count_and_then_closes_up()
    {
        var screen = Build.Screen();

        double Step(int count) => Parking.Pitch(Parking.Fan(Parked(count, screen)), screen);

        Assert.Equal(Step(1), Step(2));
        Assert.True(Step(30) < Step(2));
        Assert.True(Step(30) > 0, "the fan collapsed onto one place");
    }

    /// <summary>
    /// A card is at the size a picture arrives at on THIS screen, and straight. Applied on every
    /// pass, so a scene carried to a screen of another shape arrives as a fan and not as a heap.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(800, 600)]
    public void A_card_is_at_arrival_size_and_straight(int width, int height)
    {
        var screen = ScreenContext.Default(new PixelSize(width, height), 96) with { DefaultRotationDeg = 90 };

        var scene = Parking.Arrange(
            Build.SceneWith(Build.Item(parked: true, parkedAt: 1, scale: 0.9, rotationDeg: 33)),
            screen);

        Assert.Equal(Layout.ScaleOnLoad(scene.Items[0].AspectRatio, screen), scene.Items[0].Scale);
        Assert.Equal(90, scene.Items[0].RotationDeg);
    }

    /// <summary>
    /// The corners belong to Windows, so the fan stays out of them - and a card keeps exactly the
    /// graspable remainder the edge clamp grants everything else.
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void A_card_sits_where_the_clamp_would_leave_it(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };

        var parked = Parked(3, screen).Items[1];

        Assert.Equal(parked, Manipulation.HoldAtEdge(parked, screen));
    }

    /// <summary>
    /// A point that is not on the fan means nothing. Without this the whole table would answer as
    /// though it were the fan, and every gesture out on the glass would pull a parked picture.
    /// </summary>
    [Fact]
    public void A_point_off_the_fan_picks_nothing()
    {
        var screen = Build.Screen();
        var scene = Parked(4, screen);

        Assert.Null(Parking.Pick(scene, screen, new Point(0.5, 0.5)));
        Assert.Null(Parking.Pick(scene, screen, new Point(0.99, 0.02)));
        Assert.Null(Parking.Pick(scene, screen, new Point(0.99, 0.98)));
        Assert.Null(Parking.Pick(Build.SceneWith(Build.Item()), screen, new Point(0.99, 0.5)));
    }

    /// <summary>
    /// The peek shows the WHOLE picture, still against the park edge - so the movement that follows,
    /// away from the edge, is the one that takes it onto the table and the hand never goes back.
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void A_peeked_picture_is_whole_and_still_at_the_edge(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };
        var scene = Parked(3, screen);
        var fan = Parking.Fan(scene);

        var at = Parking.Peek(scene, screen, fan[1].ItemId);

        Assert.NotNull(at);

        var peeked = fan[1] with { CenterX = at.Value.X, CenterY = at.Value.Y };
        var rect = Layout.ItemToRect(peeked, screen);

        Assert.InRange(rect.X, -1e-9, 1);
        Assert.InRange(rect.Y, -1e-9, 1);
        Assert.InRange(rect.X + rect.Width, 0, 1 + 1e-9);
        Assert.InRange(rect.Y + rect.Height, 0, 1 + 1e-9);

        // Whole means whole: the edge clamp has nothing left to do to it.
        Assert.Equal(peeked, Manipulation.HoldAtEdge(peeked, screen));
    }

    /// <summary>
    /// Nothing is ever refused. Sixty parked pictures still leave sixty pickable places, which is
    /// what replaced the old floor under the slot width - the fan is the mechanism, not the slot.
    /// </summary>
    [Fact]
    public void Sixty_parked_pictures_are_still_sixty_pickable_places()
    {
        var screen = Build.Screen();
        var scene = Parked(60, screen);

        var picked = Parking.Fan(scene)
            .Select((_, index) => Parking.Pick(scene, screen, Probe(index, scene, screen)))
            .Distinct()
            .Count();

        Assert.Equal(60, picked);
    }

    [Fact]
    public void Pictures_that_are_not_parked_are_not_touched()
    {
        var screen = Build.Screen();
        var lying = Build.Item(centerX: 0.2, centerY: 0.3);

        var scene = Parking.Arrange(Build.SceneWith(lying, Build.Item(parked: true, parkedAt: 1)), screen);

        Assert.Equal(lying, scene.Items[0]);
    }

    /// <summary>
    /// The fan keeps its order and closes up when one leaves - the positions are a function of the
    /// list, so nobody has to send a patch for the gap.
    /// </summary>
    [Fact]
    public void The_fan_keeps_its_order_and_closes_up_when_one_leaves()
    {
        var screen = Build.Screen();
        var scene = Parked(4, screen);
        var newest = Parking.Fan(scene)[0];

        var shorter = Parking.Arrange(
            scene with { Items = [.. scene.Items.Where(item => item.ItemId != newest.ItemId)] },
            screen);

        Assert.Equal([4L, 3, 2], [.. Parking.Fan(scene).Select(item => item.ParkedAt).Take(3)]);
        Assert.Equal([3L, 2, 1], [.. Parking.Fan(shorter).Select(item => item.ParkedAt)]);

        // The one that was second is now first, so it moved to the near end.
        Assert.Equal(Along(Parking.Fan(scene)[0], screen), Along(Parking.Fan(shorter)[0], screen), precision: 9);
    }

    /// <summary>
    /// Changing the edge during play moves the whole fan, because the positions are computed from
    /// the list rather than stored - at a table "right" is left from the other side (Part 6).
    /// </summary>
    [Fact]
    public void Changing_the_park_edge_moves_the_whole_fan()
    {
        var screen = Build.Screen();
        var scene = Parked(3, screen);

        var moved = Parking.Arrange(scene, screen with { ParkEdge = ParkEdge.Left });

        Assert.All(scene.Items, item => Assert.True(item.CenterX > 0.5));
        Assert.All(moved.Items, item => Assert.True(item.CenterX < 0.5));
    }

    /// <summary>Nine at 96 DIP along a 1080-DIP edge - the number the parameter table produces.</summary>
    /// <summary>
    /// <b>Five, not nine</b>, and the difference is the newest card's own body. The old number
    /// divided the bar by a finger and forgot that the card at the near end is covered by nothing
    /// and lies there at its whole arrival length - half the bar on a 1080 table. Measured: the
    /// pitch is a finger at five cards and 80 DIP at six.
    /// </summary>
    [Fact]
    public void A_1080p_table_shows_five_cards_at_a_fingers_width()
    {
        var screen = Build.Screen();

        Assert.Equal(5, Parking.Capacity(Parking.Fan(Parked(5, screen)), screen));

        // And the number is not a claim of its own - it says where the pitch gives way.
        Assert.Equal(
            screen.MinVisibleNormalisedY,
            Parking.Pitch(Parking.Fan(Parked(5, screen)), screen),
            precision: 9);

        Assert.True(
            Parking.Pitch(Parking.Fan(Parked(6, screen)), screen) < screen.MinVisibleNormalisedY,
            "the sixth card was expected to close the fan up");
    }

    /// <summary>
    /// <b>A card too long for the bar is drawn shorter, and that is the one place a parked picture
    /// is not at its arrival size.</b> With the shipped defaults a picture may be 0.9 of the screen
    /// long while the bar is 0.8, so on a TOP park edge five 4:1 pictures ran 64 % of a screen
    /// width off the glass and every touch on the bar picked the same one of them - four parked and
    /// unreachable at once (hand-run of M3).
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void Panoramas_wider_than_the_bar_still_make_a_fan(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };
        var scene = Parking.Arrange(
            Build.SceneWith(
                [.. Enumerable.Range(0, 5).Select(i => Build.Item(parked: true, parkedAt: i + 1, aspectRatio: 4))]),
            screen);

        var fan = Parking.Fan(scene);

        Assert.Equal(5, Reachable(scene, screen).Count);

        foreach (var card in fan)
        {
            var cut = Parking.CutOf(card, screen);
            var rect = Layout.ItemToRect(card, screen);

            // Cut, and what is left of it ends inside the bar. The head runs back off the near end
            // on purpose - it is faded out there, and the card in front lies over it anyway.
            Assert.False(cut.IsWhole, "a picture longer than the bar was expected to be cut");
            Assert.InRange(rect.X + rect.Width, 0, 0.9 + 1e-9);
        }
    }

    /// <summary>
    /// <b>The size a card has in the fan is a size the gesture clamp accepts</b>, so coming back out
    /// changes nothing. It did not use to be: a 1:10 tower parked at 0.400 and came out at 0.740,
    /// nearly double, under the hand (hand-run of M3, N11).
    /// <para>
    /// Two rules, each right on its own, disagreeing at their intersection (`G15`).
    /// <c>ScaleOnLoad</c> has no lower bound because on an extreme shape it explodes; <c>ClampScale</c>
    /// keeps one because the DM must not be able to zoom a picture away. The fan asked only the
    /// first and produced sizes the second would not have allowed - and the first transform after
    /// the card came out was the second's turn to speak.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Right, 0.05)]
    [InlineData(ParkEdge.Right, 0.1)]
    [InlineData(ParkEdge.Right, 4d / 3d)]
    [InlineData(ParkEdge.Top, 4)]
    [InlineData(ParkEdge.Bottom, 20)]
    public void A_card_comes_out_of_the_fan_at_the_size_it_had_in_it(ParkEdge edge, double aspect)
    {
        var screen = Build.Screen() with { ParkEdge = edge };
        var item = Build.Item(parked: true, parkedAt: 1, aspectRatio: aspect);

        var parked = Parking.Arrange(Build.SceneWith(item), screen).Items[0];

        // What the hub does to the very first transform after the card is pulled out.
        Assert.Equal(
            parked.Scale, Layout.ClampScale(parked.Scale, aspect, screen), precision: 9);
    }

    /// <summary>
    /// The cut takes the head and fades over one step - the same finger the whole fan is measured
    /// in. A picture that fits is not cut at all, which is every ordinary shape.
    /// </summary>
    [Fact]
    public void The_cut_takes_one_step_of_fade_and_only_where_it_is_needed()
    {
        var screen = Build.Screen();

        Assert.True(Parking.CutOf(Build.Item(parked: true), screen).IsWhole);

        // 0.1 is a tower: at the arrival scale it is far longer than the bar down a side fan.
        var tall = Build.Item(parked: true, aspectRatio: 0.1);
        var cut = Parking.CutOf(tall, screen);

        Assert.False(cut.IsWhole);

        // The length the card really has in the fan, read off the fan itself rather than rebuilt
        // from the formula - a second copy of that arithmetic here is a second thing to keep true.
        var parked = Parking.Arrange(Build.SceneWith(tall), screen).Items[0];
        var extent = Layout.ItemToRect(parked, screen).Height;

        Assert.True(extent > 0.8 - screen.MinVisibleNormalisedY, "the tower was expected to be too long");

        // What is shown is the bar minus one step, and the fade is that one step.
        Assert.Equal(0.8 - screen.MinVisibleNormalisedY, cut.Shown * extent, precision: 9);
        Assert.Equal(screen.MinVisibleNormalisedY, cut.Fade * extent, precision: 9);
    }

    /// <summary>
    /// <b>A short card behind a long one still shows a step of itself.</b> Without the floor the
    /// cascade swallowed it whole: an 8:1 panorama between ordinary pictures was invisible AND
    /// unreachable, which is the one state parking may never produce (Part 11).
    /// </summary>
    [Fact]
    public void A_short_card_between_long_ones_is_not_swallowed()
    {
        var screen = Build.Screen();
        var scene = Parking.Arrange(
            Build.SceneWith(
                Build.Item(parked: true, parkedAt: 1),
                Build.Item(parked: true, parkedAt: 2),
                Build.Item(parked: true, parkedAt: 3, aspectRatio: 8),
                Build.Item(parked: true, parkedAt: 4),
                Build.Item(parked: true, parkedAt: 5)),
            screen);

        var fan = Parking.Fan(scene);
        var short_ = fan.Single(card => Layout.ItemToRect(card, screen).Height < 0.3);

        Assert.Contains(short_.ItemId, Reachable(scene, screen));

        // Seen as well as reached: its own trailing edge lies past the card in front of it.
        var ahead = fan[Array.IndexOf([.. fan], short_) - 1];
        var tail = Along(short_, screen) + (Layout.ItemToRect(short_, screen).Height / 2);
        var cover = Along(ahead, screen) + (Layout.ItemToRect(ahead, screen).Height / 2);

        Assert.True(tail > cover, "the short card was completely covered by the one in front");
    }

    /// <summary>
    /// <b>Every card fills the band across, so nothing shows through beside it.</b> A tower came to
    /// lie 80 DIP into a 96 DIP band - <c>MinScale</c> means "80 DIP on the shorter edge", the band
    /// is a finger deep - and the older cards showed through the 16 DIP that were left (hand-run of
    /// M3, N11). The fan's measure is the stricter one and the one that counts here.
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Right, 0.05)]
    [InlineData(ParkEdge.Right, 0.1)]
    [InlineData(ParkEdge.Right, 4d / 3d)]
    [InlineData(ParkEdge.Left, 0.1)]
    [InlineData(ParkEdge.Top, 0.1)]
    [InlineData(ParkEdge.Bottom, 20)]
    public void A_card_reaches_all_the_way_across_the_band(ParkEdge edge, double aspect)
    {
        var screen = Build.Screen() with { ParkEdge = edge };
        var alongY = edge is ParkEdge.Left or ParkEdge.Right;

        var parked = Parking.Arrange(
            Build.SceneWith(Build.Item(parked: true, parkedAt: 1, aspectRatio: aspect)), screen)
            .Items[0];

        var rect = Layout.ItemToRect(parked, screen);
        var breadth = alongY ? rect.Width : rect.Height;
        var band = alongY ? screen.MinVisibleNormalisedX : screen.MinVisibleNormalisedY;

        Assert.True(
            breadth >= band - 1e-9,
            $"the card reaches {breadth:F4} across a band of {band:F4}");

        // And it is still a size the gesture clamp accepts, so coming out changes nothing.
        Assert.Equal(parked.Scale, Layout.ClampScale(parked.Scale, aspect, screen), precision: 9);
    }

    /// <summary>Every card a finger can land on, swept over the whole bar.</summary>
    private static HashSet<ItemId> Reachable(SceneState scene, ScreenContext screen)
    {
        var alongX = screen.ParkEdge is ParkEdge.Top or ParkEdge.Bottom;
        var found = new HashSet<ItemId>();

        for (var i = 0; i <= 4000; i++)
        {
            var along = 0.1 + (i * 0.8 / 4000);
            var at = alongX
                ? new Point(along, screen.ParkEdge is ParkEdge.Top ? 0.005 : 0.995)
                : new Point(screen.ParkEdge is ParkEdge.Left ? 0.005 : 0.995, along);

            if (Parking.Pick(scene, screen, at) is { } card)
            {
                found.Add(card);
            }
        }

        return found;
    }

    /// <summary>
    /// <b>A card steps out where it LIES, and the hand has no say in it</b> (hand-run of M3). Two
    /// wrong places came before: under the hand, which dragged the shown card along the fan and
    /// made the eye chase it; and where the hand landed, which held still but showed every card of
    /// a long run at the one spot the finger first touched, so the fan turned into a slide viewer.
    /// Running on shows the next card further along, because that is where it actually is.
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void A_card_is_peeked_at_its_own_place_in_the_fan(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };
        var scene = Parked(5, screen);
        var fan = Parking.Fan(scene);

        var places = fan
            .Select(card => Parking.Peek(scene, screen, card.ItemId))
            .Select(at => screen.ParkEdge is ParkEdge.Left or ParkEdge.Right ? at!.Value.Y : at!.Value.X)
            .ToList();

        // Each card's own step along the fan, unchanged by the peek - only the across moves.
        Assert.Equal([.. fan.Select(card => Along(card, screen))], places);
    }

    /// <summary>
    /// <b>The band is the one boundary the fan has</b>, and both halves of the gesture read it: a
    /// hand inside it is choosing a card, a hand outside it has taken one. Measuring the pull from
    /// where the hand landed instead - and demanding it be longer than the run along the fan - meant
    /// that after a good scroll it took half a screen to get a card out (hand-run of M3).
    /// </summary>
    [Fact]
    public void Leaving_the_band_is_all_it_takes_however_far_the_hand_ran()
    {
        var screen = Build.Screen();
        var band = screen.MinVisibleNormalisedX;

        // Landed at the top of the fan, ran the whole way down it, then in by a hair.
        Assert.True(Parking.OnTheFan(new Point(1 - (band / 2), 0.15), screen));
        Assert.True(Parking.OnTheFan(new Point(1 - (band / 2), 0.85), screen));
        Assert.False(Parking.OnTheFan(new Point(1 - band - 0.001, 0.85), screen));
    }

    /// <summary>
    /// And every peek is on the screen. A card lies inside the bar, so stepping it clear across the
    /// edge cannot put it past a corner - the one place a parked picture may never be.
    /// </summary>
    [Fact]
    public void Every_peek_stays_on_the_screen()
    {
        var screen = Build.Screen();
        var scene = Parked(20, screen);

        foreach (var card in Parking.Fan(scene))
        {
            var at = Parking.Peek(scene, screen, card.ItemId);

            Assert.NotNull(at);

            var rect = Layout.ItemToRect(card with { CenterX = at.Value.X, CenterY = at.Value.Y }, screen);

            Assert.InRange(rect.X, -1e-9, 1);
            Assert.InRange(rect.Y, -1e-9, 1);
            Assert.InRange(rect.X + rect.Width, 0, 1 + 1e-9);
            Assert.InRange(rect.Y + rect.Height, 0, 1 + 1e-9);
        }
    }

    /// <summary>A card that is not in the fan has no peek, and asking for one is not a crash.</summary>
    [Fact]
    public void A_picture_that_is_not_parked_has_no_peek()
    {
        var screen = Build.Screen();
        var lying = Build.Item();

        Assert.Null(Parking.Peek(Build.SceneWith(lying), screen, lying.ItemId));
    }
}
