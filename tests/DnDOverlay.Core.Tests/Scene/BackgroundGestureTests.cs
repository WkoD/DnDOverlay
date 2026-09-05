using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// A hand on the background layer. Since M4 it carries a place and a size like any picture, so the
/// gesture arithmetic is the same one - and these tests are here to keep it that way: a second
/// version of "how far may this be pushed" is what rule 9 exists against.
/// </summary>
public sealed class BackgroundGestureTests
{
    /// <summary>
    /// The plain case. Nothing is symmetric about it: an over-wide picture on a 16:9 screen, pushed
    /// on both axes at once, so a swapped pair could not pass (Guide C14).
    /// </summary>
    [Fact]
    public void A_push_moves_the_background()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(3200, 900), scale: 2, centerX: 0.5, centerY: 0.5);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0.07, -0.03, 1, 0, new Point(0.5, 0.5)),
            screen);

        Assert.Equal(0.57, moved.CenterX, 6);
        Assert.Equal(0.47, moved.CenterY, 6);
    }

    /// <summary>A pinch scales it, and the scale stays between the screen's own bounds.</summary>
    [Fact]
    public void A_pinch_scales_the_background()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), scale: 1);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0, 0, 1.5, 0, new Point(0.5, 0.5)),
            screen);

        Assert.Equal(1.5, moved.Scale, 6);
    }

    /// <summary>
    /// It turns, and through the SAME dead zone a picture has: a background nudged a degree by two
    /// fingers must not end an evening standing crooked either (Part 6).
    /// </summary>
    [Fact]
    public void The_dead_zone_holds_for_the_background_too()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), rotationDeg: 0);

        var (still, turning) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0, 0, 1, screen.RotationDeadZoneDeg - 1, new Point(0.5, 0.5)),
            screen);

        Assert.Equal(0, still.RotationDeg, 6);
        Assert.False(turning.Engaged);

        var (turned, engaged) = Manipulation.Step(
            still,
            turning,
            new GestureStep(0, 0, 1, 4, new Point(0.5, 0.5)),
            screen);

        Assert.True(engaged.Engaged);
        Assert.True(turned.RotationDeg > 0, "the background did not turn once the dead zone was left");
    }

    /// <summary>
    /// And it snaps on release, like a picture - these are the same quarter turns "turn to me"
    /// produces, so no second frame of reference comes into being (Part 6).
    /// </summary>
    [Fact]
    public void A_release_snaps_the_background_onto_a_quarter_turn()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), rotationDeg: 88);

        Assert.Equal(90, Manipulation.Settle(background, screen).RotationDeg, 6);
    }

    /// <summary>
    /// <b>A background large enough to cover leaves no edge bare</b> - and that is a harder rule
    /// than a picture's (hand-run of M4, 38b). A picture may hang out over the side, because one
    /// zooms in to bring a detail closer; behind a background there is nothing to see, so a black
    /// stripe along the table is simply pushed too far.
    /// </summary>
    [Fact]
    public void The_background_cannot_uncover_an_edge_it_could_cover()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), scale: 1.4);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(9, 9, 1, 0, new Point(0.5, 0.5)),
            screen);

        var rect = Layout.BackgroundRect(moved, screen);

        Assert.True(rect.X <= 1e-9 && rect.Right >= 1 - 1e-9, "a vertical edge was left bare");
        Assert.True(rect.Y <= 1e-9 && rect.Bottom >= 1 - 1e-9, "a horizontal edge was left bare");
    }

    /// <summary>
    /// One that is too small to cover is left where the hand put it - the counter-check, without
    /// which the rule above would also pass on a clamp that simply centred everything (Guide C16).
    /// </summary>
    [Fact]
    public void A_background_smaller_than_the_screen_keeps_its_place()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), scale: 0.4, centerX: 0.5, centerY: 0.5);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0.12, 0.07, 1, 0, new Point(0.5, 0.5)),
            screen);

        Assert.Equal(0.62, moved.CenterX, 6);
        Assert.Equal(0.57, moved.CenterY, 6);
    }

    /// <summary>
    /// Nothing else about the layer changes. It is the point of the stand-in being private: the
    /// asset, the name and the paused animation belong to the background and not to the gesture.
    /// </summary>
    [Fact]
    public void A_gesture_touches_nothing_but_place_size_and_angle()
    {
        var screen = Build.Screen();
        var background = Build.Background(name: "Sturmküste", showName: true, animationPaused: true);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0.02, 0.02, 1.1, 0, new Point(0.4, 0.4)),
            screen);

        Assert.Equal(background.AssetId, moved.AssetId);
        Assert.Equal(background.Meta, moved.Meta);
        Assert.Equal("Sturmküste", moved.Name);
        Assert.True(moved.ShowName);
        Assert.True(moved.AnimationPaused);
    }
}
