using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The order of the tiles as the DM arranged it (Part 7): new screens at the end, an unplugged screen
/// keeping its place, a dragged tile taking the place of the one it was dropped on.
/// </summary>
public sealed class TileArrangementTests
{
    private static readonly DeviceId Device = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1"));
    private static readonly DeviceId Second = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2"));
    private static readonly ScreenRef A = new(Device, new ScreenId("A"));
    private static readonly ScreenRef B = new(Device, new ScreenId("B"));
    private static readonly ScreenRef C = new(Device, new ScreenId("C"));
    private static readonly ScreenRef D = new(Device, new ScreenId("D"));

    [Fact]
    public void Without_a_saved_order_the_screens_stand_as_they_came() =>
        Assert.Equal([A, B, C], Screens(TileArrangement.Arranged(Views(A, B, C), [])));

    /// <summary>The saved order rules; what it does not know is new and goes to the end, in the order it came.</summary>
    [Fact]
    public void A_new_screen_hangs_itself_on_the_end()
    {
        var arranged = TileArrangement.Arranged(Views(D, A, B, C), [Key(C), Key(A)]);

        Assert.Equal([C, A, D, B], Screens(arranged));
    }

    /// <summary>A saved place with no screen behind it is skipped - the unplugged screen does not leave a hole.</summary>
    [Fact]
    public void A_saved_place_without_a_screen_is_skipped()
    {
        var arranged = TileArrangement.Arranged(Views(A, B), [Key(B), Key(C), Key(A)]);

        Assert.Equal([B, A], Screens(arranged));
    }

    /// <summary>Dragged forward onto a tile, a tile takes that tile's place and pushes it back.</summary>
    [Fact]
    public void A_tile_dragged_forward_takes_the_targets_place() =>
        Assert.Equal([C, A, B], TileArrangement.Moved([A, B, C], C, A));

    /// <summary>Dragged backward, it lands behind the target - the others make way toward the front.</summary>
    [Fact]
    public void A_tile_dragged_backward_lands_where_the_target_stood() =>
        Assert.Equal([B, C, A], TileArrangement.Moved([A, B, C], A, C));

    /// <summary>A tile that left the stage in mid-drag moves nothing.</summary>
    [Fact]
    public void A_tile_not_on_the_stage_moves_nothing()
    {
        Assert.Null(TileArrangement.Moved([A, B], D, A));
        Assert.Null(TileArrangement.Moved([A, B], A, D));
    }

    /// <summary>
    /// What is written keeps the place of a screen unplugged during the rearrangement (Part 7): the
    /// screens that are here, then the saved places of the ones that are not.
    /// </summary>
    [Fact]
    public void An_unplugged_screen_keeps_its_saved_place_through_a_rearrangement()
    {
        var kept = TileArrangement.Kept([B, A], [Key(A), Key(D), Key(B)]);

        Assert.Equal([Key(B), Key(A), Key(D)], kept);
    }

    /// <summary>"Reset the arrangement" (Part 7): by device name first, then by screen name within the device.</summary>
    [Fact]
    public void A_reset_orders_by_device_then_by_screen_name()
    {
        var table = new ScreenRef(Second, new ScreenId("T1"));
        var wall = new ScreenRef(Second, new ScreenId("T2"));

        var order = TileArrangement.Reset(
            [Named(C, "Zelt"), Named(wall, "Beamer"), Named(A, "Aula"), Named(table, "Tisch")],
            [Key(C), Key(wall), Key(A), Key(table)],
            [],
            [Known(Device, "WOHNZIMMER"), Known(Second, "Arbeitszimmer")]);

        // "Arbeitszimmer" before "WOHNZIMMER", whatever the screens are called.
        Assert.Equal([Key(wall), Key(table), Key(A), Key(C)], order);
    }

    /// <summary>Case does not decide - the device list sorts the same way (Part 3).</summary>
    [Fact]
    public void A_reset_ignores_case()
    {
        var order = TileArrangement.Reset(
            [Named(A, "beamer"), Named(B, "Tisch"), Named(C, "Aula")],
            [],
            [],
            [Known(Device, "TISCH-PC")]);

        Assert.Equal([Key(C), Key(A), Key(B)], order);
    }

    /// <summary>
    /// An unplugged screen takes its place by the name control.json keeps, and stands there when it comes
    /// back; a saved place with no name anywhere goes to the end, as it was.
    /// </summary>
    [Fact]
    public void A_reset_places_an_unplugged_screen_by_its_kept_name()
    {
        var order = TileArrangement.Reset(
            [Named(A, "Tisch"), Named(C, "Aula")],
            [Key(D), Key(A), Key(B), Key(C)],
            [KnownAt(B, "Beamer")],
            [Known(Device, "TISCH-PC")]);

        Assert.Equal([Key(C), Key(B), Key(A), Key(D)], order);

        // Plugged in again, B stands where the reset put it.
        Assert.Equal([C, B, A], Screens(TileArrangement.Arranged(Views(A, B, C), order)));
    }

    /// <summary>A screen new on the stage with no saved place is ordered in by name like the rest.</summary>
    [Fact]
    public void A_reset_takes_in_a_screen_that_had_no_place_yet()
    {
        var order = TileArrangement.Reset(
            [Named(B, "Tisch"), Named(A, "Beamer")],
            [Key(B)],
            [],
            [Known(Device, "TISCH-PC")]);

        Assert.Equal([Key(A), Key(B)], order);
    }

    private static ScreenView Named(ScreenRef screen, string label) =>
        new(screen, new ScreenInfo(screen.Screen, label, null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled);

    private static KnownDevice Known(DeviceId device, string name) =>
        new(device.Value, name, PairingRole.Display, "token", DateTimeOffset.UnixEpoch);

    private static KnownScreen KnownAt(ScreenRef screen, string label) =>
        new(screen.Device.Value, screen.Screen.Value, label, ScreenState.Enabled, new PixelSize(1920, 1080), 96, new ScreenSettings());

    private static ScreenKey Key(ScreenRef screen) => new(screen.Device.Value, screen.Screen.Value);

    private static List<ScreenView> Views(params ScreenRef[] screens) =>
        [.. screens.Select(screen => new ScreenView(screen, new ScreenInfo(screen.Screen, screen.Screen.Value, null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled))];

    private static List<ScreenRef> Screens(IReadOnlyList<ScreenView> views) => [.. views.Select(view => view.Screen)];
}
