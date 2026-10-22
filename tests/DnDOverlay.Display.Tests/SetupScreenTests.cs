namespace DnDOverlay.Display.Tests;

/// <summary>The setup screen and the rescue mark never stand over each other (Part 4, Part 6).</summary>
public sealed class SetupScreenTests
{
    [Fact]
    public void A_pairing_request_of_its_own_shows_the_setup_screen() =>
        Assert.True(SetupScreen.Wanted(answeringCall: false));

    /// <summary>The veto on the mark has to stay reachable while the device offers itself.</summary>
    [Fact]
    public void An_answer_to_a_call_leaves_the_rescue_mark_free() =>
        Assert.False(SetupScreen.Wanted(answeringCall: true));
}
