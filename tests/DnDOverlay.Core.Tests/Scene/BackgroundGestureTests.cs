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
    /// <b>A background goes where it is pushed, edge or no edge.</b>
    /// <para>
    /// It briefly had a clamp of its own - a background large enough to cover the screen was pulled
    /// back so that it never left a bare stripe, because there is nothing behind it to see. The DM
    /// asked for the opposite and gave the reason: the background is an ordinary picture as far as
    /// the hand is concerned, and whatever it does not cover is transparent, which is fine. This
    /// test is the decision, so that the clamp cannot come back by accident.
    /// </para>
    /// </summary>
    [Fact]
    public void A_large_background_may_be_pushed_until_an_edge_shows()
    {
        var screen = Build.Screen();
        var background = Build.Background(meta: Build.Meta(1600, 900), scale: 1.4);

        var (moved, _) = Manipulation.Step(
            background,
            Turning.Beginning,
            new GestureStep(0.3, 0.2, 1, 0, new Point(0.5, 0.5)),
            screen);

        var rect = Layout.BackgroundRect(moved, screen);

        Assert.Equal(0.8, moved.CenterX, 6);
        Assert.Equal(0.7, moved.CenterY, 6);
        Assert.True(rect.X > 0, "the background was pulled back to the edge it was pushed away from");
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
