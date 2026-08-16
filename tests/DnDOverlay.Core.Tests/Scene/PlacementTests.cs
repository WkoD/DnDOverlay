using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

public sealed class PlacementTests
{
    private const double SmallScale = 0.2;
    private const double Square = 1;
    private const double Wide = 16d / 9d;
    private const double Tall = 2d / 3d;

    /// <summary>
    /// The reason placement lives in the hub at all: the paste hotkey, the inventory double tap
    /// and later a mobile device could otherwise work out the same place and lay two images
    /// exactly on top of each other (Part 3).
    /// </summary>
    [Fact]
    public void Two_images_without_a_position_take_two_different_places()
    {
        var screen = Build.Screen();

        var first = Placement.NextPosition(SceneState.Empty, SmallScale, Square, screen);
        var scene = Build.SceneWith(Build.Item(first.X, first.Y, SmallScale, Square));

        var second = Placement.NextPosition(scene, SmallScale, Square, screen);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// <b>The correction the table forced twice</b> (hand-run of M2b, step 16, second round). The
    /// ported <c>OlPane.addImage</c> looked for a free place, which sounds helpful and is not: where
    /// a picture lands then depends on where every other picture happens to lie, and the same action
    /// gives a different answer each time.
    /// <para>
    /// Asserted as the property rather than as a coordinate: the same scene, with its items dragged
    /// anywhere at all, must place the next picture in the same spot. The old code fails this at the
    /// first item that was moved, which is exactly what "willkürlich" meant.
    /// </para>
    /// </summary>
    [Fact]
    public void Where_the_next_picture_lands_does_not_depend_on_where_the_others_lie()
    {
        var screen = Build.Screen();

        var tidy = Build.SceneWith(
            Build.Item(0.17, 0.22, SmallScale, Square),
            Build.Item(0.49, 0.22, SmallScale, Square));

        var dragged = Build.SceneWith(
            Build.Item(0.9, 0.9, SmallScale, Square),
            Build.Item(0.15, 0.8, SmallScale, Square));

        Assert.Equal(
            Placement.NextPosition(tidy, SmallScale, Square, screen),
            Placement.NextPosition(dragged, SmallScale, Square, screen));
    }

    /// <summary>
    /// Six places at the size a picture arrives in, filled in reading order, and the seventh starts
    /// over. Both numbers are written out rather than read from <c>ScreenContext</c>: reading the
    /// constant would assert that the code equals itself, and it is the SIX that was decided
    /// (Guide <c>C9</c>).
    /// </summary>
    [Fact]
    public void Six_places_are_filled_in_order_and_then_start_over()
    {
        var screen = Build.Screen();
        var places = new List<Point>();

        for (var count = 0; count < 13; count++)
        {
            var scene = Build.SceneWith([.. Enumerable
                .Range(0, count)
                .Select(_ => (SceneItem)Build.Item(0.5, 0.5, SmallScale, Square))]);

            places.Add(Placement.NextPosition(scene, SmallScale, Square, screen));
        }

        Assert.Equal(6, places.Take(6).Distinct().Count());
        Assert.Equal(places.Take(6), places.Skip(6).Take(6));
        Assert.Equal(places[0], places[12]);
    }

    /// <summary>
    /// <b>The other half of the same correction.</b> Deriving the grid from the picture being placed
    /// gave every shape its own grid, so nothing lined up with anything - measured at the table as
    /// "die Platzierung wirkt merkwürdig", with a screenshot of six pictures in a ragged
    /// arrangement. The cells are the same for every picture now, and each one is CENTRED in its
    /// cell.
    /// <para>
    /// A tall picture and a wide one placed at the same count therefore land on the same centre,
    /// although they are quite different sizes. Under the old rule they landed a good tenth of the
    /// screen apart.
    /// </para>
    /// </summary>
    [Fact]
    public void Pictures_of_any_shape_line_up_on_the_same_grid()
    {
        var screen = Build.Screen();
        var scene = Build.SceneWith(Build.Item(0.5, 0.5, SmallScale, Square));

        var tall = Placement.NextPosition(scene, 0.4, Tall, screen);
        var wide = Placement.NextPosition(scene, 0.4, Wide, screen);
        var square = Placement.NextPosition(scene, 0.4, Square, screen);

        Assert.Equal(tall, square);
        Assert.Equal(tall, wide);
    }

    /// <summary>
    /// <b>The promise of step 16, and until now nothing asserted it:</b> pictures lie side by side
    /// WITHOUT overlapping. The test that used to cover it went with the free-place search and was
    /// not replaced - so the one sentence the mode exists for stood unmeasured while five other
    /// tests looked after its arithmetic.
    /// <para>
    /// At the size a picture arrives in, and for the shape the cells are measured in. A wider
    /// picture does NOT hold this and cannot: the cell is 4:3 and the picture is not, so it reaches
    /// past it - which is a separate question and has its own test below.
    /// </para>
    /// </summary>
    [Fact]
    public void The_places_of_one_grid_do_not_overlap()
    {
        var screen = Build.Screen();
        var scale = Layout.ScaleOnLoad(4d / 3d, screen);
        var scene = SceneState.Empty;
        var rects = new List<Rect>();

        for (var i = 0; i < 6; i++)
        {
            var position = Placement.NextPosition(scene, scale, 4d / 3d, screen);
            var item = Build.Item(position.X, position.Y, scale, 4d / 3d);

            rects.Add(Layout.ItemToRect(item, screen));
            scene = Build.SceneWith([.. scene.Items, item]);
        }

        foreach (var (a, b) in rects.SelectMany(a => rects.Select(b => (a, b))).Where(pair => pair.a != pair.b))
        {
            var overlap = a.Intersect(b);

            Assert.True(
                overlap.Width <= 0 || overlap.Height <= 0,
                $"{a} and {b} overlap by {overlap.Width}x{overlap.Height}");
        }
    }

    /// <summary>
    /// <b>The picture found at the table:</b> 7000×4211 overlapped its neighbours on both sides.
    /// The cell decided only WHERE a picture went and never how big it was - the size came from the
    /// arrival height and a width cap against the SCREEN, which at that shape does not bite at all
    /// (0.96 against an arrival size of 0.4). Every picture wider than the cell's own 4:3 reached
    /// past it.
    /// <para>
    /// Run over six shapes so that this is a property and not one file: whatever arrives, six of
    /// them lie side by side without touching. That is the one sentence step 16 exists for.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(7000d / 4211d)]
    [InlineData(16d / 9d)]
    [InlineData(4d / 3d)]
    [InlineData(1d)]
    [InlineData(2d / 3d)]
    [InlineData(21d / 9d)]
    public void Six_pictures_of_any_shape_lie_side_by_side_without_overlapping(double aspectRatio)
    {
        var screen = Build.Screen();

        var scale = Placement.FitIntoItsPlace(
            Layout.ScaleOnLoad(aspectRatio, screen), aspectRatio, screen);

        var scene = SceneState.Empty;
        var rects = new List<Rect>();

        for (var i = 0; i < 6; i++)
        {
            var position = Placement.NextPosition(scene, scale, aspectRatio, screen);
            var item = Build.Item(position.X, position.Y, scale, aspectRatio);

            rects.Add(Layout.ItemToRect(item, screen));
            scene = Build.SceneWith([.. scene.Items, item]);
        }

        foreach (var (a, b) in rects.SelectMany(a => rects.Select(b => (a, b))).Where(pair => pair.a != pair.b))
        {
            var overlap = a.Intersect(b);

            Assert.True(
                overlap.Width <= 0 || overlap.Height <= 0,
                $"aspect ratio {aspectRatio}: {a} and {b} overlap by {overlap.Width}×{overlap.Height}");
        }
    }

    /// <summary>
    /// The counter-check, so the fitting is not simply "make everything small": a picture the cell
    /// can hold is not touched at all. Without this, returning zero would pass the test above.
    /// </summary>
    [Theory]
    [InlineData(4d / 3d)]
    [InlineData(1d)]
    [InlineData(2d / 3d)]
    public void A_picture_that_fits_its_place_arrives_at_the_size_it_was_promised(double aspectRatio)
    {
        var screen = Build.Screen();
        var wanted = Layout.ScaleOnLoad(aspectRatio, screen);

        Assert.Equal(wanted, Placement.FitIntoItsPlace(wanted, aspectRatio, screen), precision: 9);
    }

    /// <summary>
    /// Cascade has no places to fit into, so nothing is taken away there. Deliberately the opposite
    /// answer to the same question - the two modes are not two spellings of one arrangement, and a
    /// bound that leaked from one into the other would shrink pictures for no reason at all.
    /// </summary>
    [Fact]
    public void Cascade_takes_nothing_away_from_the_arrival_size()
    {
        var screen = Build.Screen(placement: PlacementMode.Cascade);
        var wanted = Layout.ScaleOnLoad(Wide, screen);

        Assert.Equal(wanted, Placement.FitIntoItsPlace(wanted, Wide, screen), precision: 9);

        // And the same picture IS taken down in Flow, which is what makes the line above a
        // measurement rather than a tautology.
        Assert.True(Placement.FitIntoItsPlace(wanted, Wide, Build.Screen()) < wanted);
    }

    /// <summary>
    /// Reading order: the second place lies to the RIGHT of the first at the same height, and the
    /// row below starts back at the left. Nothing said so - a grid emitted column by column would
    /// have left every other test in this file green, and the DM would have learnt a different
    /// order from the one written down.
    /// </summary>
    [Fact]
    public void The_places_run_left_to_right_and_then_down()
    {
        var screen = Build.Screen();
        var places = SixPlaces(screen);

        Assert.True(places[1].X > places[0].X, "the second place is not to the right of the first");
        Assert.Equal(places[0].Y, places[1].Y, precision: 9);

        // Three across on a 16:9 table, so the fourth starts the second row - back at the left, and
        // lower down.
        Assert.Equal(places[0].X, places[3].X, precision: 9);
        Assert.True(places[3].Y > places[0].Y, "the fourth place did not step down a row");
    }

    /// <summary>
    /// How many places there are follows the size a picture arrives in, and that is the knob the
    /// number six was chosen with. Fixing only the six would let the reference shape be changed
    /// underneath it without a single test noticing.
    /// <para>
    /// The numbers are MEASURED across screen shapes and sizes, not reckoned - the first version of
    /// the comment in <c>Placement</c> guessed at 21:9 and was wrong by a factor of two. The 4:3
    /// row is the one worth keeping in mind: such a projector has exactly ONE place at 0.5, so
    /// every picture lands on the last.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, 0.5, 2)]
    [InlineData(1920, 1080, 0.4, 6)]
    [InlineData(1920, 1080, 0.3, 12)]
    [InlineData(3840, 2160, 0.4, 6)]
    [InlineData(2560, 1080, 0.4, 8)]
    [InlineData(1600, 1200, 0.5, 1)]
    [InlineData(1600, 1200, 0.4, 4)]
    public void How_many_places_there_are_follows_the_arrival_size(int width, int height, double scale, int places)
    {
        var screen = Build.Screen(width, height) with { ScaleOnLoad = scale };
        var seen = new List<Point>();
        var scene = SceneState.Empty;

        for (var i = 0; i < places * 2; i++)
        {
            var position = Placement.NextPosition(scene, scale, 4d / 3d, screen);
            seen.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, scale, 4d / 3d)]);
        }

        Assert.Equal(places, seen.Distinct().Count());
    }

    /// <summary>
    /// A screen that holds no cell at all, because the arrival size is nonsense or larger than the
    /// table itself. The picture goes to the middle - the answer that shows the most of it - rather
    /// than off an edge or into an exception thrown at the DM.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void A_screen_without_a_single_place_puts_the_picture_in_the_middle(double scaleOnLoad)
    {
        var screen = Build.Screen() with { ScaleOnLoad = scaleOnLoad };

        var position = Placement.NextPosition(SceneState.Empty, 0.4, 4d / 3d, screen);

        Assert.Equal(new Point(0.5, 0.5), position);
    }

    private static List<Point> SixPlaces(ScreenContext screen)
    {
        var scale = Layout.ScaleOnLoad(4d / 3d, screen);
        var scene = SceneState.Empty;
        var places = new List<Point>();

        for (var i = 0; i < 6; i++)
        {
            var position = Placement.NextPosition(scene, scale, 4d / 3d, screen);
            places.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, scale, 4d / 3d)]);
        }

        return places;
    }

    /// <summary>Filling a whole row must step down, not run off the right edge.</summary>
    [Fact]
    public void A_full_row_wraps_to_the_next_one()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;
        var positions = new List<Point>();

        for (var i = 0; i < 6; i++)
        {
            var position = Placement.NextPosition(scene, SmallScale, Square, screen);
            positions.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, SmallScale, Square)]);
        }

        Assert.Contains(positions, p => p.Y > positions[0].Y);
        Assert.All(positions, p => Assert.InRange(p.X, 0, 1));
        Assert.All(positions, p => Assert.InRange(p.Y, 0, 1));
    }

    /// <summary>
    /// Everything stays on the screen, whatever the mode and whatever the shape. The wide case is
    /// the one that bites: a picture wider than its cell would hang over the left edge if it were
    /// simply centred in the leftmost one.
    /// </summary>
    [Theory]
    [InlineData(PlacementMode.Flow, Square)]
    [InlineData(PlacementMode.Flow, Wide)]
    [InlineData(PlacementMode.Flow, Tall)]
    [InlineData(PlacementMode.Cascade, Square)]
    public void Placement_stays_inside_the_screen(PlacementMode mode, double aspectRatio)
    {
        var screen = Build.Screen(placement: mode);
        var scene = SceneState.Empty;

        for (var i = 0; i < 10; i++)
        {
            var position = Placement.NextPosition(scene, 0.4, aspectRatio, screen);
            var item = Build.Item(position.X, position.Y, 0.4, aspectRatio);
            var rect = Layout.ItemToRect(item, screen);

            Assert.InRange(rect.X, 0, 1);
            Assert.InRange(rect.Y, 0, 1);
            Assert.InRange(rect.X + rect.Width, 0, 1);
            Assert.InRange(rect.Y + rect.Height, 0, 1);

            scene = Build.SceneWith([.. scene.Items, item]);
        }
    }

    /// <summary>Cascade is the deliberate opposite of flow: stacked with an offset, not side by side.</summary>
    [Fact]
    public void Cascade_offsets_each_image()
    {
        var screen = Build.Screen(placement: PlacementMode.Cascade);

        var first = Placement.NextPosition(SceneState.Empty, SmallScale, Square, screen);
        var scene = Build.SceneWith(Build.Item(first.X, first.Y, SmallScale, Square));
        var second = Placement.NextPosition(scene, SmallScale, Square, screen);

        Assert.NotEqual(first, second);
        Assert.True(second.X > first.X);
        Assert.True(second.Y > first.Y);
    }

    /// <summary>
    /// The row is exactly as tall as a picture, and this is what replaced the caption test (M2b).
    /// The Java version drew the name BELOW the image, which grew the row and let a captioned row
    /// overlap the one under it (commit 37e946c); ours draws it inside, so an item never reaches
    /// past its own rectangle.
    /// <para>
    /// Stated as an exact distance rather than as "no bigger than": a slack assertion would still
    /// hold if some allowance crept back in, and that allowance is precisely the thing that was
    /// removed.
    /// </para>
    /// </summary>
    [Fact]
    public void A_row_is_exactly_as_tall_as_a_picture()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;

        var rows = new List<double>();

        for (var i = 0; i < 6; i++)
        {
            var position = Placement.NextPosition(scene, screen.ScaleOnLoad, Square, screen);
            rows.Add(position.Y);
            scene = Build.SceneWith(
                [.. scene.Items, Build.Item(position.X, position.Y, screen.ScaleOnLoad, Square)]);
        }

        var distinct = rows.Distinct().OrderBy(y => y).ToList();
        Assert.True(distinct.Count > 1, "the run never reached a second row");

        // Both numbers are derived from what the placement itself produced, so the test carries no
        // second copy of the geometry: the height comes from a cell of the grid, and the gap is
        // whatever margin the first row kept from the top edge.
        var height = screen.ScaleOnLoad;
        var gap = distinct[0] - (height / 2);

        Assert.Equal(height + gap, distinct[1] - distinct[0], precision: 9);
    }
}
