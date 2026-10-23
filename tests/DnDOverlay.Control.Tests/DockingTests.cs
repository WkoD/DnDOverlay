using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control.Tests;

/// <summary>Where the two panels dock and how wide they may be - the stage must not disappear (Part 7).</summary>
public sealed class DockingTests
{
    /// <summary>The log keeps its edge - unless the campaign panel lies there; then it goes across.</summary>
    [Theory]
    [InlineData(PanelSide.Bottom, PanelSide.Right, PanelSide.Bottom)]
    [InlineData(PanelSide.Right, PanelSide.Right, PanelSide.Left)]
    [InlineData(PanelSide.Top, PanelSide.Top, PanelSide.Bottom)]
    [InlineData(null, PanelSide.Left, PanelSide.Right)]
    [InlineData(null, PanelSide.Bottom, PanelSide.Top)]
    public void The_log_never_docks_at_the_campaign_panels_edge(PanelSide? kept, PanelSide panel, PanelSide side) =>
        Assert.Equal(side, Docking.LogSide(kept, panel));

    /// <summary>The campaign panel: at most half the window, at least a row of tiles - and never squeezed under 200 as a limit.</summary>
    [Fact]
    public void The_campaign_panel_takes_at_most_half_the_window()
    {
        Assert.Equal(960, Docking.Length(1200, Docking.PanelLimit(1920)));
        Assert.Equal(380, Docking.Length(380, Docking.PanelLimit(1920)));
        Assert.Equal(Docking.Narrowest, Docking.Length(40, Docking.PanelLimit(1920)));
        Assert.Equal(200, Docking.PanelLimit(300));
    }

    /// <summary>
    /// The log shown last gives way: it opens narrower rather than press the stage under its minimum
    /// beside a pinned campaign panel on the same axis - and is never laid under the narrowest.
    /// </summary>
    [Fact]
    public void The_log_gives_way_to_the_stage()
    {
        Assert.Equal(1920 - 380 - Docking.StageMinimum, Docking.Length(2000, Docking.LogLimit(1920, 380)));
        Assert.Equal(480, Docking.Length(480, Docking.LogLimit(1920, 380)));
        Assert.Equal(Docking.Narrowest, Docking.LogLimit(600, 380));
    }

    [Fact]
    public void Each_edge_has_its_opposite()
    {
        Assert.Equal(PanelSide.Right, Docking.Opposite(PanelSide.Left));
        Assert.Equal(PanelSide.Left, Docking.Opposite(PanelSide.Right));
        Assert.Equal(PanelSide.Bottom, Docking.Opposite(PanelSide.Top));
        Assert.Equal(PanelSide.Top, Docking.Opposite(PanelSide.Bottom));
    }
}
