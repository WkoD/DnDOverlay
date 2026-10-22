using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// The hub's answers to this device's own gestures: a late answer to a grab must not pull the
/// picture back, a correction must win, and a release is measured against its own answer
/// (Part 4, conflict rule 2; <c>3028</c>).
/// </summary>
public sealed class OwnGesturesTests
{
    private static readonly ItemId Dragon = new(Guid.Parse("55555555-5555-5555-5555-555555555555"));
    private static readonly ItemId Stranger = new(Guid.Parse("66666666-6666-6666-6666-666666666666"));

    /// <summary>1000 x 500 pixels, so a thousandth of the width is one pixel and of the height half of one.</summary>
    private static readonly ScreenContext Screen = ScreenContext.Default(new PixelSize(1000, 500), 96);

    /// <summary>Where the hand took the picture, and where it let go of it.</summary>
    private static readonly ItemTransform GrabbedAt = new(Dragon, 0.2, 0.3, 0.25, 0);

    private static readonly ItemTransform LetGoAt = new(Dragon, 0.7, 0.6, 0.3, 90);

    /// <summary>
    /// The answer to the grab arrives after the release (game session, 18.10.2026): it brings the
    /// depth the grab earned and keeps the place the hand put the picture at.
    /// </summary>
    [Fact]
    public void A_late_answer_to_the_own_grab_takes_the_depth_and_keeps_the_place()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 100);

        var answer = gestures.LateAnswerToGrab(Answer(GrabbedAt, zOrder: 12, revision: 40), SceneAt(LetGoAt), Screen);

        Assert.Equal(new TransformItem(Dragon, 0.7, 0.6, 0.3, 90, 12, 40), answer);
    }

    /// <summary>In time - the hand is still on the picture - the answer is applied as it comes, and the hold keeps the finger's place.</summary>
    [Fact]
    public void An_answer_to_the_grab_while_the_hand_is_on_is_applied_as_it_comes()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(GrabbedAt), Screen));
    }

    /// <summary>
    /// A grab is answered once. Recognised in time, it is used up: the same place arriving again after
    /// the release is somebody putting the picture back there, and that wins.
    /// </summary>
    [Fact]
    public void A_grab_is_answered_once()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);

        _ = gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(GrabbedAt), Screen);
        gestures.LetGo(LetGoAt, atMs: 100);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(LetGoAt), Screen));
    }

    /// <summary>The DM moving the picture, or the hub clamping it, is a correction and wins (conflict rule 2).</summary>
    [Fact]
    public void Any_other_place_is_a_correction()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 100);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt with { CenterX = 0.21 }), SceneAt(LetGoAt), Screen));
        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt with { Scale = 0.26 }), SceneAt(LetGoAt), Screen));
        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt with { RotationDeg = 1 }), SceneAt(LetGoAt), Screen));

        // The grab is still waiting for its own answer.
        Assert.NotNull(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(LetGoAt), Screen));
    }

    /// <summary>Half a pixel is agreement, measured on each axis against its own edge.</summary>
    [Fact]
    public void Half_a_pixel_is_the_same_place()
    {
        var within = new OwnGestures();
        within.Grabbed(GrabbedAt);
        within.LetGo(LetGoAt, atMs: 100);

        // Four tenths of a thousandth of 1000 pixels is under half a pixel.
        Assert.NotNull(within.LateAnswerToGrab(Answer(GrabbedAt with { CenterX = 0.2004 }), SceneAt(LetGoAt), Screen));

        var beyond = new OwnGestures();
        beyond.Grabbed(GrabbedAt);
        beyond.LetGo(LetGoAt, atMs: 100);

        // Two thousandths of 500 pixels is one pixel.
        Assert.Null(beyond.LateAnswerToGrab(Answer(GrabbedAt with { CenterY = 0.302 }), SceneAt(LetGoAt), Screen));
    }

    /// <summary>An operation on a picture nobody here took hold of is a stranger's.</summary>
    [Fact]
    public void A_stranger_s_picture_is_applied_as_it_comes()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 100);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt with { Item = Stranger }), SceneAt(LetGoAt), Screen));
        Assert.Null(gestures.Answered(Answer(LetGoAt with { Item = Stranger }), Screen, nowMs: 200));
    }

    /// <summary>A picture that has gone from the scene has no place of its own to keep.</summary>
    [Fact]
    public void A_picture_gone_from_the_scene_keeps_nothing()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 100);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneState.Empty, Screen));
    }

    /// <summary>A picture removed while its grab was unanswered leaves nothing behind for a later one.</summary>
    [Fact]
    public void A_removed_picture_forgets_its_grab()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 100);

        gestures.Removed(Dragon);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(LetGoAt), Screen));
    }

    /// <summary>The ordinary case: the answer is the release, to the pixel, and how long it took is said.</summary>
    [Fact]
    public void The_answer_to_a_release_agrees_and_says_how_long_it_took()
    {
        var gestures = new OwnGestures();
        gestures.LetGo(LetGoAt, atMs: 1_000);

        var answer = gestures.Answered(Answer(LetGoAt), Screen, nowMs: 1_250);

        Assert.NotNull(answer);
        Assert.True(answer.Agrees);
        Assert.Equal(0, answer.DriftDip);
        Assert.Equal(250, answer.SpentMs);
        Assert.Equal(LetGoAt, answer.Sent);
    }

    /// <summary>
    /// The drift is in pixels, each axis with its own edge: three thousandths across 1000 and four across
    /// 500 make three and two, so 3.6 - not the five a square screen would give.
    /// </summary>
    [Fact]
    public void A_corrected_release_says_how_far_in_pixels()
    {
        var gestures = new OwnGestures();
        gestures.LetGo(LetGoAt, atMs: 0);

        var answer = gestures.Answered(Answer(LetGoAt with { CenterX = 0.703, CenterY = 0.604 }), Screen, nowMs: 10);

        Assert.NotNull(answer);
        Assert.False(answer.Agrees);
        Assert.Equal(3.6, answer.DriftDip);
    }

    /// <summary>
    /// Two pushes before the first answer: each answer is measured against its own release, oldest
    /// first. One slot per picture once reported 918 DIP where the two ends agreed.
    /// </summary>
    [Fact]
    public void Each_answer_is_measured_against_its_own_release()
    {
        var second = LetGoAt with { CenterX = 0.1 };
        var gestures = new OwnGestures();
        gestures.LetGo(LetGoAt, atMs: 0);
        gestures.LetGo(second, atMs: 50);

        var first = gestures.Answered(Answer(LetGoAt), Screen, nowMs: 100);
        var then = gestures.Answered(Answer(second), Screen, nowMs: 100);

        Assert.True(first!.Agrees);
        Assert.True(then!.Agrees);
        Assert.Equal(50, then.SpentMs);
        Assert.Null(gestures.Answered(Answer(second), Screen, nowMs: 100));
    }

    /// <summary>Releases the hub never answers do not pile up: the oldest beyond sixteen are dropped.</summary>
    [Fact]
    public void Unanswered_releases_are_bounded()
    {
        var gestures = new OwnGestures();

        for (var i = 0; i <= OwnGestures.MaxUnanswered; i++)
        {
            gestures.LetGo(LetGoAt with { CenterX = i / 100d }, atMs: i);
        }

        var oldest = gestures.Answered(Answer(LetGoAt with { CenterX = 0.01 }), Screen, nowMs: 100);

        Assert.True(oldest!.Agrees);
        Assert.Equal(99, oldest.SpentMs);
    }

    /// <summary>Without a release waiting, a late answer to the grab cannot be told from the hand still being on it.</summary>
    [Fact]
    public void Answered_releases_no_longer_make_a_grab_answer_late()
    {
        var gestures = new OwnGestures();
        gestures.Grabbed(GrabbedAt);
        gestures.LetGo(LetGoAt, atMs: 0);

        _ = gestures.Answered(Answer(LetGoAt), Screen, nowMs: 10);

        Assert.Null(gestures.LateAnswerToGrab(Answer(GrabbedAt), SceneAt(LetGoAt), Screen));
    }

    private static TransformItem Answer(ItemTransform at, int zOrder = 3, long revision = 7) =>
        new(at.Item, at.CenterX, at.CenterY, at.Scale, at.RotationDeg, zOrder, revision);

    private static SceneState SceneAt(ItemTransform at) =>
        SceneState.Empty with
        {
            Items =
            [
                new TextItem(at.Item, at.CenterX, at.CenterY, at.Scale, 1, at.RotationDeg, 2, false, false, 5, "Dilwyn"),
            ],
        };
}
