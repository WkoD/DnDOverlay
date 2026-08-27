using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The gesture arithmetic. Everything here is a promise from Part 6 that the players lean on all
/// evening: nothing gets lost, nothing stands crooked, and nothing clicks into place under a
/// finger.
/// </summary>
public sealed class ManipulationTests
{
    private static GestureStep Move(double x, double y) => new(x, y, 1, 0, new Point(0.5, 0.5));

    private static GestureStep Zoom(double factor, Point origin) => new(0, 0, factor, 0, origin);

    private static GestureStep Rotate(double degrees) => new(0, 0, 1, degrees, new Point(0.5, 0.5));

    private static SceneItem Apply(SceneItem item, ScreenContext screen, params GestureStep[] steps)
    {
        var turning = Turning.Beginning;

        foreach (var step in steps)
        {
            (item, turning) = Manipulation.Step(item, turning, step, screen);
        }

        return item;
    }

    /// <summary>
    /// How much of the item is left on the screen, in DIP, on the axis that shows least. The unit
    /// matters: the promise is 96 DIP, and normalised units say two different things about the two
    /// axes.
    /// </summary>
    private static double VisibleDip(SceneItem item, ScreenContext screen)
    {
        var hull = Layout.ItemToHullRect(item, screen);

        var x = (Math.Min(hull.X + hull.Width, 1) - Math.Max(hull.X, 0)) * screen.WidthInDip;
        var y = (Math.Min(hull.Y + hull.Height, 1) - Math.Max(hull.Y, 0)) * screen.HeightInDip;

        return Math.Min(x, y);
    }

    /// <summary>
    /// <b>Nothing gets lost</b> (Part 11), and it is asked of a long random sequence rather than of
    /// three tidy cases: pushing, zooming and turning interact, and the hull of a turned item is a
    /// different shape after every step. The seed is fixed so a failure can be walked into again.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1080)]
    [InlineData(1080, 1920)]
    public void After_any_sequence_of_gestures_a_graspable_remainder_is_left(int width, int height)
    {
        var screen = Build.Screen(width, height);
        var random = new Random(20260818);
        var item = (SceneItem)Build.Item();

        for (var round = 0; round < 500; round++)
        {
            var step = new GestureStep(
                TranslationX: (random.NextDouble() - 0.5) * 0.6,
                TranslationY: (random.NextDouble() - 0.5) * 0.6,
                ScaleFactor: 0.5 + (random.NextDouble() * 1.4),
                RotationDeg: (random.NextDouble() - 0.5) * 90,
                Origin: new Point(random.NextDouble(), random.NextDouble()));

            item = Apply(item, screen, step);

            Assert.True(
                VisibleDip(item, screen) >= screen.MinVisiblePixels - 1e-6,
                $"round {round} left only {VisibleDip(item, screen):F1} DIP showing");

            Assert.InRange(
                item.Scale,
                Layout.ClampScale(0, item.AspectRatio, screen) - 1e-9,
                screen.MaxScale + 1e-9);
        }
    }

    /// <summary>
    /// The same rule on both axes, in DIP - and this is the test that would have caught using one
    /// normalised minimum for both. On a 16:9 table the sideways one is 1.78 times as long, so a
    /// single number leaves 96 DIP at the top and 54 at the side.
    /// </summary>
    /// <summary>
    /// <b>Nothing vanishes, and in a corner that takes a different measurement.</b> The clamp holds
    /// each axis on its own against the hull, which is exactly right at an edge and blind at a
    /// corner: there both hold at once, satisfied by two DIFFERENT corners of a turned picture,
    /// with nothing between them.
    /// <para>
    /// Found at the table twice. The first time it was a unit error in the hull; that was fixed and
    /// the picture <b>still</b> disappeared, which is what this test is really about - measured at
    /// 37 degrees with both axes reporting their full 96 DIP and <b>zero</b> square DIP of picture
    /// on the screen (checks/M3.md, G31).
    /// </para>
    /// <para>
    /// Walked into the corner step by step rather than placed there, because that is how a hand
    /// does it and because the clamp is applied per step - a single jump could pass while the path
    /// to it does not.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0.6, 37)]
    [InlineData(1.0, 37)]
    [InlineData(1.5, 37)]
    [InlineData(2.5, 37)]
    [InlineData(1.5, 45)]
    [InlineData(1.5, 135)]
    [InlineData(1.5, 200)]
    public void A_turned_picture_keeps_a_graspable_patch_in_every_corner(double aspectRatio, double rotationDeg)
    {
        var screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
        var patch = screen.MinVisiblePixels * screen.MinVisiblePixels;

        foreach (var (towardsX, towardsY) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            SceneItem item = Build.Item(aspectRatio: aspectRatio) with
            {
                CenterX = 0.5,
                CenterY = 0.5,
                Scale = 0.39,
                RotationDeg = rotationDeg,
            };

            for (var step = 0; step < 400; step++)
            {
                item = Manipulation.HoldAtEdge(
                    item with
                    {
                        CenterX = item.CenterX + (towardsX * 0.01),
                        CenterY = item.CenterY + (towardsY * 0.01),
                    },
                    screen);
            }

            var showing = Layout.VisibleAreaInDip(item, screen);

            Assert.True(
                showing >= patch - 1,
                $"corner {towardsX}/{towardsY}: only {showing:F0} of {patch} square DIP left on the screen");
        }
    }

    [Fact]
    public void The_remainder_is_the_same_length_sideways_as_it_is_downwards()
    {
        var screen = Build.Screen();
        var item = Build.Item(scale: 0.3, aspectRatio: 1);

        var right = Manipulation.HoldAtEdge(item with { CenterX = 5 }, screen);
        var bottom = Manipulation.HoldAtEdge(item with { CenterY = 5 }, screen);

        var showingSideways = (1 - Layout.ItemToHullRect(right, screen).X) * screen.WidthInDip;
        var showingDownwards = (1 - Layout.ItemToHullRect(bottom, screen).Y) * screen.HeightInDip;

        Assert.Equal(screen.MinVisiblePixels, showingSideways, precision: 6);
        Assert.Equal(screen.MinVisiblePixels, showingDownwards, precision: 6);
    }

    /// <summary>
    /// Sticking out is expressly allowed - under strong zoom one brings a detail closer, and the
    /// rule is about vanishing, not about overhang (Part 6).
    /// </summary>
    [Fact]
    public void A_zoomed_picture_may_hang_far_over_the_edge()
    {
        var screen = Build.Screen();

        var item = Apply(
            Build.Item(scale: 0.5),
            screen,
            Zoom(6, new Point(0.5, 0.5)),
            Move(0.8, 0));

        var hull = Layout.ItemToHullRect(item, screen);

        Assert.True(hull.X + hull.Width > 1.5, "the picture was pulled back onto the screen");
        Assert.True(VisibleDip(item, screen) >= screen.MinVisiblePixels - 1e-6);
    }

    /// <summary>A picture smaller than the minimum cannot leave more behind than it has.</summary>
    [Fact]
    public void A_small_picture_stays_whole_at_the_edge()
    {
        var screen = Build.Screen();
        var item = Build.Item(scale: Layout.ClampScale(0, 1, Build.Screen()), aspectRatio: 1);

        var pushed = Manipulation.HoldAtEdge(item with { CenterX = 5 }, screen);
        var hull = Layout.ItemToHullRect(pushed, screen);

        Assert.InRange(hull.X + hull.Width, 0, 1 + 1e-9);
        Assert.True(hull.X >= -1e-9, "a picture below the minimum was allowed to hang out anyway");
    }

    [Fact]
    public void Below_the_dead_zone_nothing_turns_and_everything_else_still_moves()
    {
        var screen = Build.Screen();
        var item = Build.Item(rotationDeg: 0);

        var after = Apply(
            item,
            screen,
            new GestureStep(0.1, 0, 1.2, screen.RotationDeadZoneDeg - 1, new Point(0.5, 0.5)));

        Assert.Equal(0, after.RotationDeg);
        Assert.Equal(0.6, after.CenterX, precision: 9);
        Assert.Equal(item.Scale * 1.2, after.Scale, precision: 9);
    }

    /// <summary>
    /// Crossing the dead zone subtracts the threshold exactly once - otherwise the picture jumps by
    /// the whole zone the moment it begins to move, which is the thing the zone exists to prevent.
    /// </summary>
    [Fact]
    public void Crossing_the_dead_zone_costs_the_threshold_once_and_never_again()
    {
        var screen = Build.Screen();

        var crossed = Apply(Build.Item(), screen, Rotate(screen.RotationDeadZoneDeg + 3));

        Assert.Equal(3, crossed.RotationDeg, precision: 9);

        var further = Apply(
            Build.Item(),
            screen,
            Rotate(screen.RotationDeadZoneDeg + 3),
            Rotate(10));

        Assert.Equal(13, further.RotationDeg, precision: 9);
    }

    /// <summary>The zone counts the whole gesture, not one delta: ten small turns add up.</summary>
    [Fact]
    public void The_dead_zone_measures_the_gesture_and_not_the_step()
    {
        var screen = Build.Screen();

        var item = Apply(Build.Item(), screen, [.. Enumerable.Repeat(Rotate(1), 10)]);

        Assert.Equal(10 - screen.RotationDeadZoneDeg, item.RotationDeg, precision: 9);
    }

    /// <summary>A picture that clicks into place under the finger feels broken (Part 6).</summary>
    [Fact]
    public void Nothing_snaps_while_the_fingers_are_still_on_it()
    {
        var screen = Build.Screen();

        var item = Apply(Build.Item(rotationDeg: 88), screen, Rotate(screen.RotationDeadZoneDeg + 1));

        Assert.Equal(89, item.RotationDeg, precision: 9);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(88, 90)]
    [InlineData(182, 180)]
    [InlineData(357, 0)]
    [InlineData(30, 30)]
    [InlineData(45, 45)]
    public void Letting_go_pulls_a_near_quarter_turn_onto_it(double before, double after)
    {
        Assert.Equal(after, Manipulation.Settle(Build.Item(rotationDeg: before), Build.Screen()).RotationDeg, precision: 9);
    }

    [Fact]
    public void A_tolerance_of_zero_switches_snapping_off()
    {
        var screen = Build.Screen() with { RotationSnapToleranceDeg = 0 };

        Assert.Equal(88, Manipulation.Settle(Build.Item(rotationDeg: 88), screen).RotationDeg, precision: 9);
    }

    /// <summary>
    /// Zooming around the pointer keeps what is under it under it. Without this a pinch walks the
    /// picture out from under the fingers - the Java bug this arithmetic exists to avoid.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1080)]
    public void What_lies_under_the_pointer_stays_under_the_pointer(int width, int height)
    {
        var screen = Build.Screen(width, height);
        var origin = new Point(0.3, 0.8);
        var item = Build.Item(centerX: 0.5, centerY: 0.5, scale: 0.3);

        var zoomed = Apply(item, screen, Zoom(1.5, origin));

        // The offset from the pivot must have grown by exactly the factor, on both axes.
        Assert.Equal((item.CenterX - origin.X) * 1.5, zoomed.CenterX - origin.X, precision: 9);
        Assert.Equal((item.CenterY - origin.Y) * 1.5, zoomed.CenterY - origin.Y, precision: 9);
    }

    /// <summary>
    /// Turning around a pivot keeps the distance to it - in DIP. On a 21:9 screen a rotation
    /// applied to normalised offsets directly would drag the picture sideways across the table.
    /// </summary>
    [Fact]
    public void Turning_around_a_pivot_does_not_drag_the_picture_sideways()
    {
        var screen = Build.Screen(2560, 1080);
        var origin = new Point(0.5, 0.5);
        // Close enough to the pivot that the turn does not push it off the screen: this asks about
        // the rotation, and a clamped centre would answer a different question.
        var item = Build.Item(centerX: 0.55, centerY: 0.5, scale: 0.2);

        var turned = Apply(item, screen, Rotate(screen.RotationDeadZoneDeg + 90));

        static double DistanceDip(double x, double y, Point origin, ScreenContext screen) =>
            Math.Sqrt(
                Math.Pow((x - origin.X) * screen.WidthInDip, 2)
                + Math.Pow((y - origin.Y) * screen.HeightInDip, 2));

        Assert.Equal(
            DistanceDip(item.CenterX, item.CenterY, origin, screen),
            DistanceDip(turned.CenterX, turned.CenterY, origin, screen),
            precision: 6);
    }

    [Theory]
    [InlineData(0.5, 0.95, 0)]
    [InlineData(0.5, 0.05, 180)]
    [InlineData(0.02, 0.5, 90)]
    [InlineData(0.98, 0.5, 270)]
    public void Turn_to_me_faces_the_edge_the_finger_came_from(double x, double y, int expected)
    {
        Assert.Equal(expected, Manipulation.TurnToMe(new Point(x, y), Build.Screen()));
    }

    /// <summary>
    /// Nearest is measured in DIP: on a 21:9 table the middle is much further from the sides than
    /// from top and bottom, and a normalised comparison would say the opposite.
    /// </summary>
    [Fact]
    public void Turn_to_me_measures_in_dip_and_not_in_fractions()
    {
        Assert.Equal(0, Manipulation.TurnToMe(new Point(0.2, 0.75), Build.Screen(2560, 1080)));
    }

    [Fact]
    public void A_flick_at_the_park_edge_parks_and_a_slow_drag_does_not()
    {
        var screen = Build.Screen();
        var atEdge = Manipulation.HoldAtEdge(Build.Item(centerX: 5), screen);

        Assert.True(Manipulation.ShouldPark(atEdge, Manipulation.ParkVelocityDip + 1, 0, travelDip: 0, screen));
        Assert.False(Manipulation.ShouldPark(atEdge, 200, 0, travelDip: 0, screen));
    }

    [Fact]
    public void A_flick_away_from_the_park_edge_does_not_park()
    {
        var screen = Build.Screen();
        var atEdge = Manipulation.HoldAtEdge(Build.Item(centerX: 5), screen);

        Assert.False(Manipulation.ShouldPark(atEdge, -5000, 0, travelDip: 0, screen));
    }

    /// <summary>
    /// <b>A flick from the middle of the table parks, and that is a change</b> (end of M3). It used
    /// to be a push: the picture had to be at the edge already, and inertia carried it there. At a
    /// table four people sit round, that meant shoving a picture half an arm's length before the
    /// tidying gesture would take (hand-run of M3, G30).
    /// </summary>
    [Fact]
    public void A_flick_from_the_middle_parks_and_a_slow_push_from_the_middle_does_not()
    {
        var screen = Build.Screen();
        var middle = Build.Item(centerX: 0.5);

        Assert.True(Manipulation.ShouldPark(middle, Manipulation.ParkVelocityDip + 1, 0, travelDip: 0, screen));
        Assert.False(Manipulation.ShouldPark(middle, Manipulation.ParkVelocityDip - 1, 0, travelDip: 0, screen));
    }

    /// <summary>
    /// <b>A flick is speed AND a short way.</b> Without the second half a long fast drag towards the
    /// park edge parked instead of moving the picture - which is how one hands something across the
    /// table (hand-run of M3, A2).
    /// </summary>
    [Fact]
    public void A_long_fast_drag_is_not_a_flick()
    {
        var screen = Build.Screen();
        var item = Build.Item(centerX: 0.5);
        var fast = Manipulation.ParkVelocityDip * 2;

        Assert.True(Manipulation.ShouldPark(item, fast, 0, Manipulation.ParkFlickDip - 1, screen));
        Assert.False(Manipulation.ShouldPark(item, fast, 0, Manipulation.ParkFlickDip + 1, screen));
    }

    /// <summary>
    /// <b>A glide stops before the fan; a hand may push right up to it.</b> A picture that slid to
    /// a stop with its remainder under the fan would be parked without being parked - the fan is
    /// drawn over the table, so nothing of it could be picked up again (hand-run of M3).
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void A_glide_stops_before_the_fan_and_a_hand_does_not(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };

        SceneItem hand = Build.Item(centerX: 0.5, centerY: 0.5);
        SceneItem glide = Build.Item(centerX: 0.5, centerY: 0.5);
        var turning = Turning.Beginning;

        var towards = edge switch
        {
            ParkEdge.Left => new GestureStep(-0.05, 0, 1, 0, new Point(0.5, 0.5)),
            ParkEdge.Right => new GestureStep(0.05, 0, 1, 0, new Point(0.5, 0.5)),
            ParkEdge.Top => new GestureStep(0, -0.05, 1, 0, new Point(0.5, 0.5)),
            _ => new GestureStep(0, 0.05, 1, 0, new Point(0.5, 0.5)),
        };

        for (var i = 0; i < 40; i++)
        {
            (hand, turning) = Manipulation.Step(hand, Turning.Beginning, towards, screen);
            (glide, _) = Manipulation.Step(glide, Turning.Beginning, towards, screen, gliding: true);
        }

        // The hand's picture ends where the ordinary clamp puts it: its remainder is the fan's own
        // depth, so it lies exactly under the fan. The glide's is a whole band further in.
        Assert.Equal(hand, Manipulation.HoldAtEdge(hand, screen));
        Assert.Equal(glide, Manipulation.HoldAtEdge(glide, screen, clearOfTheFan: true));
        Assert.NotEqual(hand, glide);

        // What matters is how much glass the picture shows OUTSIDE the fan band, because that is
        // what a finger can land on. The hand's picture shows none; the glide's shows a finger's
        // worth. Measured as a distance rather than asked as a yes/no, because the hand's picture
        // sits exactly ON the boundary and a boundary is the one place a boolean is a coin toss.
        Assert.Equal(
            screen.MinVisiblePixels,
            Manipulation.FromParkEdge(Inner(hand, screen), screen),
            tolerance: 0.5);

        Assert.Equal(
            2 * screen.MinVisiblePixels,
            Manipulation.FromParkEdge(Inner(glide, screen), screen),
            tolerance: 0.5);
    }

    /// <summary>The picture's edge on the TABLE side of the park edge - where its glass ends.</summary>
    private static Point Inner(SceneItem item, ScreenContext screen)
    {
        var hull = Layout.ItemToHullRect(item, screen);

        return screen.ParkEdge switch
        {
            ParkEdge.Left => new Point(hull.X + hull.Width, item.CenterY),
            ParkEdge.Right => new Point(hull.X, item.CenterY),
            ParkEdge.Top => new Point(item.CenterX, hull.Y + hull.Height),
            _ => new Point(item.CenterX, hull.Y),
        };
    }

    /// <summary>
    /// <b>Where the hand let go decides, not how much of the picture is over the edge.</b> A large
    /// picture can have half of itself outside and still stand squarely on the table, so reading
    /// the picture parked things nobody meant to put away (hand-run of M3).
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void The_fan_band_is_a_finger_deep_along_its_edge(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };

        var on = edge switch
        {
            ParkEdge.Left => new Point(0.01, 0.5),
            ParkEdge.Right => new Point(0.99, 0.5),
            ParkEdge.Top => new Point(0.5, 0.01),
            _ => new Point(0.5, 0.99),
        };

        Assert.True(Parking.OnTheFan(on, screen));
        Assert.False(Parking.OnTheFan(new Point(0.5, 0.5), screen));

        // The depth is the graspable remainder itself - the cards lie exactly that far onto the
        // glass, so the band is what the eye sees rather than a number of its own.
        Assert.Equal(
            Math.Round(screen.MinVisiblePixels),
            Math.Round(Manipulation.FromParkEdge(
                edge is ParkEdge.Left or ParkEdge.Right
                    ? new Point(edge is ParkEdge.Left ? screen.MinVisibleNormalisedX : 1 - screen.MinVisibleNormalisedX, 0.5)
                    : new Point(0.5, edge is ParkEdge.Top ? screen.MinVisibleNormalisedY : 1 - screen.MinVisibleNormalisedY),
                screen)));
    }

    /// <summary>
    /// A hand let go in the middle of the table parks nothing, however far the picture hangs over
    /// the edge - the whole point of reading the hand instead of the picture.
    /// </summary>
    [Fact]
    public void A_picture_hanging_over_the_edge_is_not_parked_by_itself()
    {
        var screen = Build.Screen();
        var item = Manipulation.HoldAtEdge(Build.Item(centerX: 5), screen);

        Assert.False(Parking.OnTheFan(new Point(0.5, 0.5), screen));
        Assert.True(item.CenterX > 1, "the picture was not actually hanging over the edge");
    }

    [Fact]
    public void The_park_edge_decides_which_flick_counts()
    {
        var screen = Build.Screen() with { ParkEdge = ParkEdge.Bottom };
        var atBottom = Manipulation.HoldAtEdge(Build.Item(centerY: 5), screen);

        Assert.True(Manipulation.ShouldPark(atBottom, 0, 5000, travelDip: 0, screen));
        Assert.False(Manipulation.ShouldPark(atBottom, 5000, 0, travelDip: 0, screen));
    }

    /// <summary>
    /// <b>Three things suppress a gesture and they are asked in one place</b> (Part 6). The answer
    /// to the finger is the same for all three, so a player never has to guess which of them it
    /// was - and one place cannot be the one that forgets the fourth.
    /// </summary>
    [Fact]
    public void A_hand_may_take_hold_of_a_plain_item_on_a_plain_screen()
    {
        var item = Build.Item();

        Assert.True(Manipulation.AcceptsGestures(Build.SceneWith(item), item, ScreenState.Enabled));
        Assert.True(Manipulation.AcceptsGestures(Build.SceneWith(item), item, ScreenState.Diagnostic));
    }

    [Fact]
    public void A_locked_item_takes_no_gesture()
    {
        var locked = Build.Item(locked: true);

        Assert.False(Manipulation.AcceptsGestures(Build.SceneWith(locked), locked, ScreenState.Enabled));
    }

    [Theory]
    [InlineData(ScreenState.Disabled)]
    [InlineData(ScreenState.Blackout)]
    [InlineData(ScreenState.Inactive)]
    public void A_screen_that_is_not_playing_takes_no_gesture(ScreenState state)
    {
        var item = Build.Item();

        Assert.False(Manipulation.AcceptsGestures(Build.SceneWith(item), item, state));
    }

    /// <summary>
    /// A focus suppresses the WHOLE screen rather than the focused pictures: it is a way of showing
    /// one picture, and a table that is being looked at is not being arranged (Part 3). It cannot
    /// occur before M5b and is asked for anyway - a condition checked from the day the field exists
    /// is not the one that gets forgotten when it starts being filled.
    /// </summary>
    [Fact]
    public void A_focus_lying_on_the_screen_takes_the_gestures_of_every_item()
    {
        var focused = Build.Item();
        var other = Build.Item();

        var scene = Build.SceneWith(focused, other) with { FocusItems = [focused.ItemId] };

        Assert.False(Manipulation.AcceptsGestures(scene, focused, ScreenState.Enabled));
        Assert.False(Manipulation.AcceptsGestures(scene, other, ScreenState.Enabled));
    }

    /// <summary>
    /// Friction that rises towards the edge, not a wall: full push in the middle, nothing left
    /// where the clamp takes over.
    /// </summary>
    [Fact]
    public void An_inertial_push_is_braked_towards_the_edge_and_not_stopped_at_a_wall()
    {
        var screen = Build.Screen();
        var item = Build.Item(scale: 0.3);

        Assert.Equal(1, Manipulation.EdgeResistance(item, screen), precision: 9);

        // Against the boundary a glide really has, which is the fan and not the screen.
        var atLimit = Manipulation.HoldAtEdge(item with { CenterX = 5 }, screen, clearOfTheFan: true);
        var halfway = atLimit with { CenterX = atLimit.CenterX + (Layout.ItemToHullRect(item, screen).Width / 2) };

        Assert.Equal(0.5, Manipulation.EdgeResistance(halfway, screen), precision: 6);
        Assert.Equal(0, Manipulation.EdgeResistance(atLimit with { CenterX = 5 }, screen), precision: 9);
    }
}
