using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// Whether a screen carries an overlay: by the DM's wish, with every finding - the control's
/// suppression, the rescue mark's hiding - taking it away and leaving the wish underneath (Part 3,
/// Part 6).
/// </summary>
public sealed class ScreenStatesTests
{
    private static readonly ScreenId Table = new("table");
    private static readonly ScreenId Wall = new("wall");

    /// <summary>The silent start: a screen Windows offers is inactive and carries nothing until a control speaks (Part 3).</summary>
    [Fact]
    public void A_found_screen_starts_inactive_without_an_overlay()
    {
        var states = new ScreenStates();

        states.Found(Table);

        Assert.Null(states.Overlay(Table));
        Assert.True(states.IsInactive(Table));
        Assert.Equal(1, states.Count);
    }

    /// <summary>A monitor change surveys again; what the control wished for a screen already known stays.</summary>
    [Fact]
    public void A_second_survey_keeps_the_wish()
    {
        var states = new ScreenStates();
        var enabled = new ScreenCommand(ScreenState.Enabled);

        states.Found(Table);
        states.Wish(Table, enabled);
        states.Found(Table);

        Assert.Same(enabled, states.Overlay(Table));
    }

    /// <summary>Every state but Inactive puts an overlay down, and hands over its command - the state decides about gestures.</summary>
    [Theory]
    [InlineData(ScreenState.Enabled)]
    [InlineData(ScreenState.Disabled)]
    [InlineData(ScreenState.Diagnostic)]
    public void A_playing_state_carries_an_overlay_with_its_command(ScreenState state)
    {
        var states = new ScreenStates();
        states.Found(Table);

        states.Wish(Table, new ScreenCommand(state));

        Assert.Equal(new ScreenCommand(state), states.Overlay(Table));
        Assert.False(states.IsInactive(Table));
    }

    /// <summary>Suppressed behaves like inactive, and the wish is back the moment the finding is cleared (Part 3).</summary>
    [Fact]
    public void A_suppressed_screen_behaves_like_inactive_and_keeps_its_wish()
    {
        var states = new ScreenStates();
        states.Found(Table);

        states.Wish(Table, new ScreenCommand(ScreenState.Disabled, SuppressReason.Unavailable));

        Assert.Null(states.Overlay(Table));
        Assert.False(states.IsInactive(Table));

        states.Wish(Table, new ScreenCommand(ScreenState.Disabled));

        Assert.Equal(new ScreenCommand(ScreenState.Disabled), states.Overlay(Table));
    }

    /// <summary>
    /// "Hide" on the rescue mark takes the overlay away and leaves the state; lifting it brings the
    /// window back in the state that stood there all along (Part 6, step 43a).
    /// </summary>
    [Fact]
    public void Hidden_at_the_device_comes_back_as_it_was()
    {
        var states = new ScreenStates();
        var disabled = new ScreenCommand(ScreenState.Disabled);
        states.Found(Table);
        states.Found(Wall);
        states.Wish(Table, disabled);
        states.Wish(Wall, new ScreenCommand(ScreenState.Enabled));

        states.HideHere(Table);

        Assert.Null(states.Overlay(Table));
        Assert.NotNull(states.Overlay(Wall));

        Assert.Equal([Table], states.LiftHidden());
        Assert.Same(disabled, states.Overlay(Table));
    }

    /// <summary>A wish arriving while hidden does not lift the hiding by itself - that is the ConfigUpdate's first step.</summary>
    [Fact]
    public void A_wish_does_not_lift_the_hiding()
    {
        var states = new ScreenStates();
        states.Found(Table);
        states.HideHere(Table);

        states.Wish(Table, new ScreenCommand(ScreenState.Enabled));

        Assert.Null(states.Overlay(Table));
    }

    /// <summary>Lifting twice lifts nothing the second time: the hidden screens are handed back once.</summary>
    [Fact]
    public void Lifting_hands_back_the_hidden_screens_once()
    {
        var states = new ScreenStates();
        states.Found(Table);
        states.HideHere(Table);

        _ = states.LiftHidden();

        Assert.Empty(states.LiftHidden());
    }

    /// <summary>A screen Windows never offered has no overlay and is not "inactive" either - it is unknown.</summary>
    [Fact]
    public void An_unknown_screen_has_no_overlay_and_is_not_inactive()
    {
        var states = new ScreenStates();

        Assert.Null(states.Overlay(Table));
        Assert.False(states.IsInactive(Table));
    }

    /// <summary>The control's goodbye: every screen back to the start state, every one handed back to give up its overlay (Part 3).</summary>
    [Fact]
    public void Going_idle_makes_every_screen_inactive()
    {
        var states = new ScreenStates();
        states.Found(Table);
        states.Found(Wall);
        states.Wish(Table, new ScreenCommand(ScreenState.Enabled));
        states.Wish(Wall, new ScreenCommand(ScreenState.Diagnostic));

        var idle = states.GoIdle();

        Assert.Equal([Table, Wall], idle.OrderBy(screen => screen.Value, StringComparer.Ordinal));
        Assert.Null(states.Overlay(Table));
        Assert.Null(states.Overlay(Wall));
        Assert.True(states.IsInactive(Wall));
    }
}
