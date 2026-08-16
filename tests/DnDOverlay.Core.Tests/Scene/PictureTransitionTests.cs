using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// What has to happen to one place on the screen when the scene changes.
/// <para>
/// It exists because of what the table found (hand-run of M2b, step 24): every change to the scene
/// restarted every animation, because the display drew the whole thing again on every patch. The
/// decision is here rather than in the window so that it can be asserted at all - the window is an
/// application and has no tests.
/// </para>
/// </summary>
public sealed class PictureTransitionTests
{
    private static readonly AssetId Same = Build.Asset('a');
    private static readonly AssetId Other = Build.Asset('b');

    /// <summary>
    /// The finding itself. A picture that is running and still allowed to run is left alone -
    /// whatever else changed in the scene. Anything but <c>Leave</c> here is the restart the DM saw.
    /// </summary>
    [Fact]
    public void A_running_picture_that_may_still_run_is_left_alone()
    {
        Assert.Equal(
            PictureAction.Leave,
            PictureTransition.Next(PictureState.Moving, Same, Same, admitted: true, paused: false));
    }

    /// <summary>
    /// The DM's pause and the budget's refusal both end in a picture that does not move, and they
    /// are NOT the same instruction. A pause is meant to be undone, so it keeps its place; a
    /// refusal has to let go of what it holds.
    /// <para>
    /// Written as one test over both, because what matters is that they DIFFER (Guide <c>G8</c>) -
    /// two separate green tests would not notice the two answers collapsing into one.
    /// </para>
    /// </summary>
    [Fact]
    public void A_pause_holds_where_a_refusal_freezes()
    {
        var paused = PictureTransition.Next(
            PictureState.Moving, Same, Same, admitted: false, paused: true);

        var refused = PictureTransition.Next(
            PictureState.Moving, Same, Same, admitted: false, paused: false);

        Assert.Equal(PictureAction.Hold, paused);
        Assert.Equal(PictureAction.Freeze, refused);
        Assert.NotEqual(paused, refused);
    }

    /// <summary>Un-pausing carries the animation on rather than building a new one.</summary>
    [Fact]
    public void A_held_picture_that_may_run_again_carries_on()
    {
        Assert.Equal(
            PictureAction.Resume,
            PictureTransition.Next(PictureState.Held, Same, Same, admitted: true, paused: false));
    }

    /// <summary>
    /// A place that has never shown anything, and one that shows something else, both have to build
    /// from scratch: there is nothing to carry on from.
    /// </summary>
    [Theory]
    [InlineData(PictureState.Nothing, null)]
    [InlineData(PictureState.Moving, 'b')]
    [InlineData(PictureState.Held, 'b')]
    public void A_different_picture_is_built_from_scratch(PictureState state, char? showing)
    {
        var before = showing is { } fill ? new AssetId(new string(fill, 64)) : (AssetId?)null;

        Assert.Equal(
            PictureAction.Start,
            PictureTransition.Next(state, before, Same, admitted: true, paused: false));

        Assert.Equal(
            PictureAction.Freeze,
            PictureTransition.Next(state, before, Same, admitted: false, paused: false));
    }

    /// <summary>
    /// A still picture that is meant to stay still is left alone - the ordinary case, and the one
    /// that decides whether a scene of thirty photographs costs anything to redraw. Freezing it
    /// again would replace a source with itself on every patch.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_still_picture_that_stays_still_is_left_alone(bool paused)
    {
        Assert.Equal(
            PictureAction.Leave,
            PictureTransition.Next(PictureState.Still, Same, Same, admitted: false, paused));
    }

    /// <summary>
    /// A still picture that is allowed to move has to be built: it has no animation to resume. This
    /// is the DM un-pausing something the budget had also turned away, and the case a
    /// <c>Resume</c>-if-same shortcut would get wrong by starting a clock that is not there.
    /// </summary>
    [Fact]
    public void A_still_picture_that_may_now_move_is_built()
    {
        Assert.Equal(
            PictureAction.Start,
            PictureTransition.Next(PictureState.Still, Same, Same, admitted: true, paused: false));
    }

    /// <summary>
    /// The state that follows each answer, so the caller does not have to keep a second copy of the
    /// same reasoning. <c>Leave</c> is the one that changes nothing.
    /// </summary>
    [Fact]
    public void The_state_follows_the_answer()
    {
        Assert.Equal(PictureState.Moving, PictureTransition.After(PictureState.Still, PictureAction.Start));
        Assert.Equal(PictureState.Moving, PictureTransition.After(PictureState.Held, PictureAction.Resume));
        Assert.Equal(PictureState.Held, PictureTransition.After(PictureState.Moving, PictureAction.Hold));
        Assert.Equal(PictureState.Still, PictureTransition.After(PictureState.Moving, PictureAction.Freeze));
        Assert.Equal(PictureState.Held, PictureTransition.After(PictureState.Held, PictureAction.Leave));
    }

    /// <summary>
    /// Nothing but <c>Leave</c> may leave the place untouched. Without this the enum could grow an
    /// answer whose after-state was forgotten, and the place would then be described as something it
    /// is not - which is exactly how a held animation would come to be treated as a running one.
    /// </summary>
    [Fact]
    public void Every_answer_but_leaving_settles_on_a_state_of_its_own()
    {
        var settled = Enum.GetValues<PictureAction>()
            .Where(action => action != PictureAction.Leave)
            .Select(action => PictureTransition.After(PictureState.Nothing, action))
            .ToList();

        Assert.DoesNotContain(PictureState.Nothing, settled);
    }
}
