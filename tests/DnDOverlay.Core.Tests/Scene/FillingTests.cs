using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// How much of one run of arrivals goes onto the screen. The rule exists because a single intake
/// put 714 pictures on one table, of which six were visible - the rest lay in six piles under them,
/// the display held 2.9 GB, and the control's tile stopped answering (Part 6,
/// <see cref="Filling"/>).
/// </summary>
public sealed class FillingTests
{
    /// <summary>
    /// The ordinary case, and the one that must not change: everything a DM drops in normal use
    /// goes up whole, whatever already lies on the table. The bound is on the run, so a table that
    /// is already full does not refuse the next picture.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(29)]
    [InlineData(30)]
    public void A_run_that_fits_goes_up_whole(int arriving) =>
        Assert.Equal(arriving, Filling.OntoTheScreen(arriving));

    /// <summary>
    /// The case that broke the table: a screenful goes up, and the remainder is what the caller has
    /// to account for. Without that sentence "714 taken in" stands beside a table showing six.
    /// </summary>
    [Fact]
    public void A_run_larger_than_a_screenful_leaves_the_rest_in_the_stock()
    {
        var onto = Filling.OntoTheScreen(714);

        Assert.Equal(AnimationBudget.ItemsPerScreen, onto);
        Assert.Equal(684, 714 - onto);
    }

    /// <summary>
    /// The bound is Part 6's own number rather than one chosen here, so raising the budget raises
    /// this with it and there is no second answer to the same question (Guide <c>G24</c>).
    /// </summary>
    [Fact]
    public void The_bound_is_the_number_Part_6_reckons_a_screen_carries() =>
        Assert.Equal(AnimationBudget.ItemsPerScreen, Filling.OntoTheScreen(int.MaxValue));

    /// <summary>A run of nothing is not a negative run.</summary>
    [Fact]
    public void A_nonsense_count_never_comes_back_negative() =>
        Assert.Equal(0, Filling.OntoTheScreen(-3));
}
