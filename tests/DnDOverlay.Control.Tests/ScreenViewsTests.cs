using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// How the DM looks at each screen, kept in control.json beside the inventory (Part 7) - and kept
/// through every write of the inventory, which used to reset it (hand-run of M4, 25).
/// </summary>
public sealed class ScreenViewsTests
{
    private static readonly DeviceId Device = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000b1"));
    private static readonly ScreenRef Table = new(Device, new ScreenId("DISPLAY1"));
    private static readonly ScreenRef Wall = new(Device, new ScreenId("DISPLAY2"));

    [Fact]
    public void A_screen_the_file_does_not_know_is_unturned()
    {
        Assert.Equal(ViewRotation.None, ScreenViews.Of(new ControlConfiguration(), Table));
        Assert.Null(ScreenViews.Find(new ControlConfiguration(), Table));
    }

    [Fact]
    public void Turning_a_known_screen_changes_its_row_and_no_other()
    {
        var configuration = new ControlConfiguration { KnownScreens = [Row(Table), Row(Wall, ViewRotation.Half)] };

        var turned = ScreenViews.Turned(configuration, Table, view: null, ViewRotation.Quarter);

        Assert.Equal(ViewRotation.Quarter, ScreenViews.Of(turned, Table));
        Assert.Equal(ViewRotation.Half, ScreenViews.Of(turned, Wall));
        Assert.Equal(2, turned.KnownScreens.Count);
    }

    /// <summary>
    /// A screen not yet named or configured has no row: it gets one, or the rotation vanished without a
    /// word and came back at 0 after every restart (hand-run of M4, 25).
    /// </summary>
    [Fact]
    public void Turning_a_screen_the_file_never_heard_of_gives_it_a_row()
    {
        var view = new ScreenView(Table, new ScreenInfo(Table.Screen, "TISCH-PC//1", null, new PixelSize(3840, 2160), 144, true), ScreenState.Enabled);

        var turned = ScreenViews.Turned(new ControlConfiguration(), Table, view, ViewRotation.ThreeQuarters);

        var row = Assert.Single(turned.KnownScreens);

        Assert.Equal(ViewRotation.ThreeQuarters, row.View);
        Assert.Equal("TISCH-PC//1", row.Label);
        Assert.Equal(new PixelSize(3840, 2160), row.Size);
    }

    /// <summary>
    /// The inventory written back from the catalogue - whose rows have no view - keeps the views of the
    /// screens it already knew; a new screen comes in unturned, a gone one goes.
    /// </summary>
    [Fact]
    public void Writing_the_inventory_back_keeps_the_views()
    {
        var gone = new ScreenRef(Device, new ScreenId("DISPLAY3"));
        var configuration = new ControlConfiguration { KnownScreens = [Row(Table, ViewRotation.Quarter), Row(gone, ViewRotation.Half)] };

        var written = ScreenViews.KeptThrough(configuration, [Row(Table) with { Label = "Tisch" }, Row(Wall)]);

        Assert.Equal(ViewRotation.Quarter, ScreenViews.Of(written, Table));
        Assert.Equal("Tisch", written.KnownScreens.Single(row => row.ScreenId == "DISPLAY1").Label);
        Assert.Equal(ViewRotation.None, ScreenViews.Of(written, Wall));
        Assert.Null(ScreenViews.Find(written, gone));
    }

    private static KnownScreen Row(ScreenRef screen, ViewRotation view = ViewRotation.None) =>
        new(screen.Device.Value, screen.Screen.Value, "TISCH-PC//1", ScreenState.Enabled, new PixelSize(1920, 1080), 96, new ScreenSettings(), view);
}
