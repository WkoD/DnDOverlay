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
    [Fact]
    public void A_1080p_table_shows_nine_cards_at_a_fingers_width()
    {
        Assert.Equal(9, Parking.Capacity(Build.Screen()));
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
