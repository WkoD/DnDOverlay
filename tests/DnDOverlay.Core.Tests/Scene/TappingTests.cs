using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// What counts as a tap, and what counts as the second of two. The rule moved into Core when the
/// thumbnail got the same grip: two copies of these four numbers would be two feels, and
/// Prüfschritt 22 signs the milestone off on them being one.
/// </summary>
public sealed class TappingTests
{
    [Fact]
    public void A_short_still_touch_is_a_tap() => Assert.True(Tapping.IsTap(travelDip: 3, heldMs: 90));

    /// <summary>A hand that travelled meant to move something, however briefly it was down.</summary>
    [Fact]
    public void A_touch_that_travelled_is_not_a_tap() =>
        Assert.False(Tapping.IsTap(Tapping.TravelDip + 0.1, heldMs: 90));

    /// <summary>And one that stayed is a hold - which is what opens a menu (Part 7).</summary>
    [Fact]
    public void A_touch_that_lingered_is_not_a_tap() =>
        Assert.False(Tapping.IsTap(travelDip: 1, heldMs: Tapping.HeldMs + 1));

    /// <summary>
    /// The first tap is never the second of a pair. Without this the very first tap of a session
    /// would turn a picture, because nothing had been remembered yet.
    /// </summary>
    [Fact]
    public void The_first_tap_is_not_a_double()
    {
        var tapping = new Tapping();

        Assert.False(tapping.Twice(1000, 40, 60));
    }

    [Fact]
    public void Two_quick_taps_on_the_same_spot_are_a_double()
    {
        var tapping = new Tapping();

        Assert.False(tapping.Twice(1000, 40, 60));
        Assert.True(tapping.Twice(1000 + Tapping.TwiceMs, 40 + 5, 60 - 5));
    }

    [Fact]
    public void A_second_tap_too_late_is_a_first_one_again()
    {
        var tapping = new Tapping();

        Assert.False(tapping.Twice(1000, 40, 60));
        Assert.False(tapping.Twice(1000 + Tapping.TwiceMs + 1, 40, 60));

        // And it counts as a fresh first: a third, quick one after it pairs with it.
        Assert.True(tapping.Twice(1000 + Tapping.TwiceMs + 2, 40, 60));
    }

    /// <summary>
    /// Far away is a different tap, and the two axes are asked separately - a test that only ever
    /// moved diagonally could not tell a swapped pair apart (Guide C14).
    /// </summary>
    [Fact]
    public void A_second_tap_elsewhere_is_a_first_one_again()
    {
        var across = new Tapping();
        var down = new Tapping();

        Assert.False(across.Twice(1000, 40, 60));
        Assert.False(across.Twice(1100, 40 + Tapping.NearDip + 1, 60));

        Assert.False(down.Twice(1000, 40, 60));
        Assert.False(down.Twice(1100, 40, 60 + Tapping.NearDip + 1));
    }

    /// <summary>
    /// A third tap does not turn the picture again - the pair is spent. Otherwise holding one
    /// finger down and tapping with another would spin a picture on the table.
    /// </summary>
    [Fact]
    public void A_third_tap_does_not_pair_with_the_second()
    {
        var tapping = new Tapping();

        Assert.False(tapping.Twice(1000, 40, 60));
        Assert.True(tapping.Twice(1100, 40, 60));
        Assert.False(tapping.Twice(1200, 40, 60));
    }
}
