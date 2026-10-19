using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// Scrolling against pulling out (Part 7, step 40g): the direction decides, and the panel's side says
/// which direction is which.
/// </summary>
public sealed class PanelGestureTests
{
    [Fact]
    public void A_twitch_decides_nothing()
    {
        Assert.Equal(PanelStroke.Undecided, PanelGesture.Decide(6, 6, PanelSide.Left));
    }

    [Theory]
    [InlineData(PanelSide.Left, 0, 30)]
    [InlineData(PanelSide.Right, 2, -30)]
    [InlineData(PanelSide.Top, 30, 0)]
    [InlineData(PanelSide.Bottom, -30, 4)]
    public void Along_the_edge_the_list_scrolls(PanelSide side, double dx, double dy)
    {
        Assert.Equal(PanelStroke.Scroll, PanelGesture.Decide(dx, dy, side));
    }

    [Theory]
    [InlineData(PanelSide.Left, 30, 0)]
    [InlineData(PanelSide.Right, -30, 0)]
    [InlineData(PanelSide.Top, 0, 30)]
    [InlineData(PanelSide.Bottom, 0, -30)]
    public void Toward_the_stage_the_entry_comes_out(PanelSide side, double dx, double dy)
    {
        Assert.Equal(PanelStroke.Out, PanelGesture.Decide(dx, dy, side));
    }

    [Theory]
    [InlineData(PanelSide.Left, -30, 0)]
    [InlineData(PanelSide.Right, 30, 0)]
    [InlineData(PanelSide.Top, 0, -30)]
    [InlineData(PanelSide.Bottom, 0, 30)]
    public void Toward_the_edge_is_the_swipe_that_closes(PanelSide side, double dx, double dy)
    {
        Assert.Equal(PanelStroke.In, PanelGesture.Decide(dx, dy, side));
    }

    /// <summary>
    /// The case 40g names, "with a slanted start": steeper than the diagonal toward the stage pulls
    /// out, flatter scrolls, and exactly on it the list wins.
    /// </summary>
    [Fact]
    public void A_slanted_start_goes_by_the_steeper_axis()
    {
        Assert.Equal(PanelStroke.Out, PanelGesture.Decide(-20, 12, PanelSide.Right));
        Assert.Equal(PanelStroke.Scroll, PanelGesture.Decide(-12, 20, PanelSide.Right));
        Assert.Equal(PanelStroke.Scroll, PanelGesture.Decide(-15, 15, PanelSide.Right));
    }
}
