using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// What lies under a place, and what a frame catches.
/// <para>
/// <b>The cascade is the point, not the arithmetic.</b> M3 lost a picture at the table because
/// laying out and picking were two formulas over the same fan; this asks the same scene both ways
/// round - what is drawn on top has to be what answers a tap (Guide <c>G22</c>).
/// </para>
/// <para>
/// Nothing here is square or centred: a hit test that only ever meets symmetric data cannot find a
/// swapped axis (Guide <c>C14</c>).
/// </para>
/// </summary>
public sealed class PickingTests
{
    /// <summary>The plain case, and the one every other rests on.</summary>
    [Fact]
    public void A_point_on_a_picture_finds_it()
    {
        var item = Build.Item(centerX: 0.3, centerY: 0.6, scale: 0.2);
        var scene = Build.SceneWith(item);

        Assert.Equal(item.ItemId, Picking.At(scene, Build.Screen(), new Point(0.3, 0.6)));
    }

    /// <summary>Beside it is free area, and free area is what clears a selection.</summary>
    [Fact]
    public void A_point_beside_a_picture_finds_nothing()
    {
        var scene = Build.SceneWith(Build.Item(centerX: 0.3, centerY: 0.6, scale: 0.2));

        Assert.Null(Picking.At(scene, Build.Screen(), new Point(0.8, 0.15)));
    }

    /// <summary>
    /// Two pictures on the same spot: the upper one answers. It is the whole reason a grab raises
    /// the ZOrder - what is on top is what the hand reaches.
    /// </summary>
    [Fact]
    public void The_upper_picture_answers()
    {
        var below = Build.Item(centerX: 0.4, centerY: 0.4, scale: 0.4, zOrder: 3);
        var above = Build.Item(centerX: 0.4, centerY: 0.4, scale: 0.2, zOrder: 9);
        var scene = Build.SceneWith(below, above);

        Assert.Equal(above.ItemId, Picking.At(scene, Build.Screen(), new Point(0.4, 0.4)));
    }

    /// <summary>
    /// The fan lies above everything on the table, so a parked card wins against an item that
    /// covers the same place - the same order the drawing uses (Part 7).
    /// </summary>
    [Fact]
    public void A_parked_card_wins_against_the_table_beneath_it()
    {
        var screen = Build.Screen();
        var card = Build.Item(scale: 0.2, parked: true, parkedAt: 5, zOrder: 1);
        var lying = Build.Item(centerX: 0.5, centerY: 0.5, scale: 2, zOrder: 99);

        var scene = Parking.Arrange(Build.SceneWith(card, lying), screen);
        var at = Parking.Peek(scene, screen, card.ItemId);

        Assert.NotNull(at);

        // Where the card really lies in the fan, not where a test guessed it might.
        var found = scene.Items.First(item => item.ItemId == card.ItemId);

        Assert.Equal(
            card.ItemId,
            Picking.At(scene, screen, new Point(found.CenterX, found.CenterY)));
    }

    /// <summary>
    /// A turned picture is picked by its corners and not by the box around them. At 45 degrees the
    /// box is half again as large as the picture, and all of the difference is corner - the place
    /// where the neighbour would answer for it.
    /// </summary>
    [Fact]
    public void A_turned_picture_does_not_answer_for_its_corners()
    {
        var screen = Build.Screen();
        var item = Build.Item(centerX: 0.5, centerY: 0.5, scale: 0.4, aspectRatio: 1, rotationDeg: 45);
        var scene = Build.SceneWith(item);

        var hull = Layout.ItemToHullRect(item, screen);

        // Just inside the top left of the hull: in the box, off the picture.
        var corner = new Point(hull.X + (hull.Width * 0.03), hull.Y + (hull.Height * 0.03));

        Assert.Null(Picking.At(scene, screen, corner));
        Assert.Equal(item.ItemId, Picking.At(scene, screen, new Point(0.5, 0.5)));
    }

    /// <summary>
    /// Locked pictures are found. The lock guards against the table, not against the DM (Part 3) -
    /// if it kept the thumbnail out too, there would be no way to correct a locked picture at all.
    /// </summary>
    [Fact]
    public void A_locked_picture_is_still_found()
    {
        var item = Build.Item(centerX: 0.25, centerY: 0.7, scale: 0.3, locked: true);
        var scene = Build.SceneWith(item);

        Assert.Equal(item.ItemId, Picking.At(scene, Build.Screen(), new Point(0.25, 0.7)));
    }

    /// <summary>
    /// With the images switched off the tile shows an empty screen, and an empty screen has nothing
    /// to take hold of - otherwise a grip would move a picture the DM cannot see (Part 7).
    /// </summary>
    [Fact]
    public void Hidden_images_take_no_hits()
    {
        var item = Build.Item(centerX: 0.3, centerY: 0.6, scale: 0.2);
        var scene = Build.SceneWith(item) with { ItemsVisible = false };

        Assert.Null(Picking.At(scene, Build.Screen(), new Point(0.3, 0.6)));
        Assert.Empty(Picking.Within(scene, Build.Screen(), new Rect(0, 0, 1, 1)));
    }

    /// <summary>Touching is enough: a frame that clips a corner has caught the picture (Part 7).</summary>
    [Fact]
    public void A_frame_catches_what_it_only_touches()
    {
        var screen = Build.Screen();
        var item = Build.Item(centerX: 0.6, centerY: 0.4, scale: 0.3);
        var scene = Build.SceneWith(item);

        var hull = Layout.ItemToHullRect(item, screen);
        var corner = new Rect(hull.X - 0.05, hull.Y - 0.05, 0.06, 0.06);

        Assert.Equal([item.ItemId], Picking.Within(scene, screen, corner));
    }

    /// <summary>
    /// A frame that lies against the edge of a picture without overlapping it catches nothing - a
    /// frame that reached further than it is drawn would promise the wrong thing.
    /// </summary>
    [Fact]
    public void A_frame_that_only_abuts_catches_nothing()
    {
        var screen = Build.Screen();
        var item = Build.Item(centerX: 0.6, centerY: 0.4, scale: 0.3);
        var scene = Build.SceneWith(item);

        var hull = Layout.ItemToHullRect(item, screen);

        Assert.Empty(Picking.Within(scene, screen, new Rect(hull.X - 0.1, hull.Y, 0.1, 0.1)));
    }

    /// <summary>
    /// What comes back is ordered from the bottom of the stack upwards, and it is a LIST. The order
    /// has no reader in M4 and one in M5b - "four items focused, all four in selection order".
    /// </summary>
    [Fact]
    public void The_catch_comes_back_in_drawing_order()
    {
        var top = Build.Item(centerX: 0.5, centerY: 0.5, scale: 0.2, zOrder: 7);
        var bottom = Build.Item(centerX: 0.45, centerY: 0.55, scale: 0.2, zOrder: 2);
        var scene = Build.SceneWith(top, bottom);

        Assert.Equal(
            [bottom.ItemId, top.ItemId],
            Picking.Within(scene, Build.Screen(), new Rect(0, 0, 1, 1)));
    }

    /// <summary>
    /// Parked and locked pictures count towards a frame - Part 7 says so in as many words, and both
    /// are exactly the ones a sweeping gesture would be expected to miss.
    /// </summary>
    [Fact]
    public void Parked_and_locked_pictures_count()
    {
        var screen = Build.Screen();
        var locked = Build.Item(centerX: 0.5, centerY: 0.5, scale: 0.2, locked: true);
        var parked = Build.Item(scale: 0.2, parked: true, parkedAt: 3);

        var scene = Parking.Arrange(Build.SceneWith(locked, parked), screen);

        Assert.Equal(2, Picking.Within(scene, screen, new Rect(0, 0, 1, 1)).Count);
    }

    /// <summary>
    /// A picture the frame does not reach stays out. Without this the test above would pass on a
    /// function that simply hands back everything (Guide C16).
    /// </summary>
    [Fact]
    public void A_frame_leaves_what_it_does_not_reach()
    {
        var near = Build.Item(centerX: 0.2, centerY: 0.2, scale: 0.15);
        var far = Build.Item(centerX: 0.85, centerY: 0.8, scale: 0.15);
        var scene = Build.SceneWith(near, far);

        Assert.Equal([near.ItemId], Picking.Within(scene, Build.Screen(), new Rect(0.05, 0.05, 0.3, 0.3)));
    }
}
