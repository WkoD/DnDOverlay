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

        Assert.True(Manipulation.ShouldPark(atEdge, Manipulation.ParkVelocityDip + 1, 0, screen));
        Assert.False(Manipulation.ShouldPark(atEdge, 200, 0, screen));
    }

    [Fact]
    public void A_flick_away_from_the_park_edge_does_not_park()
    {
        var screen = Build.Screen();
        var atEdge = Manipulation.HoldAtEdge(Build.Item(centerX: 5), screen);

        Assert.False(Manipulation.ShouldPark(atEdge, -5000, 0, screen));
    }

    /// <summary>
    /// A flick from the middle of the table is a push. Inertia carries it to the edge, where it
    /// stops - parking is what happens to a picture that is already there (Part 6).
    /// </summary>
    [Fact]
    public void A_flick_from_the_middle_is_a_push()
    {
        Assert.False(Manipulation.ShouldPark(Build.Item(centerX: 0.5), 5000, 0, Build.Screen()));
    }

    [Fact]
    public void The_park_edge_decides_which_flick_counts()
    {
        var screen = Build.Screen() with { ParkEdge = ParkEdge.Bottom };
        var atBottom = Manipulation.HoldAtEdge(Build.Item(centerY: 5), screen);

        Assert.True(Manipulation.ShouldPark(atBottom, 0, 5000, screen));
        Assert.False(Manipulation.ShouldPark(atBottom, 5000, 0, screen));
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

        var atLimit = Manipulation.HoldAtEdge(item with { CenterX = 5 }, screen);
        var halfway = atLimit with { CenterX = atLimit.CenterX + (Layout.ItemToHullRect(item, screen).Width / 2) };

        Assert.Equal(0.5, Manipulation.EdgeResistance(halfway, screen), precision: 6);
        Assert.Equal(0, Manipulation.EdgeResistance(atLimit with { CenterX = 5 }, screen), precision: 9);
    }
}
