using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The screen inventory without a socket: wishes, findings, the two-sided configuration and the
/// three things a changed inventory can mean.
/// </summary>
public sealed class ScreenInventoryTests
{
    private static readonly DeviceId Device = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"));
    private static readonly DeviceId Twin = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));
    private static readonly ScreenId Screen = new(@"\\?\DISPLAY#IVM1234#5&1a2b");
    private static readonly ScreenId Second = new(@"\\?\DISPLAY#IVM1234#5&3c4d");

    /// <summary>A screen nobody has met becomes Enabled, like every unknown one (Part 3).</summary>
    [Fact]
    public void An_unknown_screen_is_played_on()
    {
        var catalog = new ScreenCatalog();

        var change = catalog.Report(Device, [Info(Screen)], reported: null);

        Assert.Equal(new ScreenRef(Device, Screen), Assert.Single(change.Added));
        Assert.Equal(ScreenState.Enabled, catalog.ViewOf(new ScreenRef(Device, Screen))!.State);
    }

    /// <summary>
    /// The core test of the state model. A finding must not overwrite the wish, because a finding
    /// that did would have to restore it - and that memory is exactly where such models come
    /// apart: the screen goes, somebody changes the wish meanwhile, and the wrong value wins when
    /// it comes back (Part 3).
    /// </summary>
    [Fact]
    public void A_screen_that_went_away_comes_back_with_the_wish_that_was_set_meanwhile()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Diagnostic);

        // Unplugged - the screen is no longer reported, and the device goes with it.
        catalog.Report(Device, [], reported: null);
        catalog.Departed(Device);

        Assert.Equal(SuppressReason.Unavailable, catalog.ViewOf(target)!.Suppressed);
        Assert.Equal(ScreenState.Diagnostic, catalog.ViewOf(target)!.State);

        // ... and now somebody changes the wish while it is away.
        catalog.SetState(target, ScreenState.Inactive);

        catalog.Report(Device, [Info(Screen)], reported: null);

        Assert.Null(catalog.ViewOf(target)!.Suppressed);
        Assert.Equal(ScreenState.Inactive, catalog.ViewOf(target)!.State);
    }

    /// <summary>
    /// Three findings, and only one of them is dangerous - which is why they are told apart
    /// rather than summed up (Part 3).
    /// </summary>
    [Fact]
    public void The_three_inventory_findings_are_told_apart()
    {
        var catalog = new ScreenCatalog();

        catalog.Report(Device, [Info(Screen), Info(Second)], reported: null);

        // Second is gone, Screen is now 4K, and a third one appears.
        var third = new ScreenId(@"\\?\DISPLAY#IVM1234#5&5e6f");
        var change = catalog.Report(
            Device,
            [Info(Screen, new PixelSize(3840, 2160), 144), Info(third)],
            reported: null);

        Assert.Equal(new ScreenRef(Device, third), Assert.Single(change.Added));
        Assert.Equal(new ScreenRef(Device, Second), Assert.Single(change.Missing));
        Assert.Equal(new ScreenRef(Device, Screen), Assert.Single(change.Changed));
    }

    /// <summary>A screen that is missing keeps its wish and its parameters - nothing is thrown away.</summary>
    [Fact]
    public void A_missing_screen_is_marked_not_forgotten()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Blackout);
        catalog.Change(target, new ScreenSettings(ParkEdge: ParkEdge.Top));

        catalog.Report(Device, [], reported: null);

        Assert.Contains(target, catalog.Known);
        Assert.Equal(ScreenState.Blackout, catalog.ViewOf(target)!.State);
        Assert.Equal(ParkEdge.Top, catalog.ContextFor(target).ParkEdge);
    }

    /// <summary>
    /// The clone case: two cloned display PCs can report literally the same screen identifier.
    /// The device identifier in front of it rules the collision out by construction (Part 3).
    /// </summary>
    [Fact]
    public void Two_devices_with_the_same_screen_identifier_do_not_interfere()
    {
        var catalog = new ScreenCatalog();
        var mine = new ScreenRef(Device, Screen);
        var theirs = new ScreenRef(Twin, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.Report(Twin, [Info(Screen)], reported: null);

        catalog.SetState(mine, ScreenState.Blackout);
        catalog.Change(theirs, new ScreenSettings(ParkEdge: ParkEdge.Left));

        Assert.Equal(ScreenState.Blackout, catalog.ViewOf(mine)!.State);
        Assert.Equal(ScreenState.Enabled, catalog.ViewOf(theirs)!.State);
        Assert.Equal(ParkEdge.Right, catalog.ContextFor(mine).ParkEdge);
        Assert.Equal(ParkEdge.Left, catalog.ContextFor(theirs).ParkEdge);
    }

    /// <summary>
    /// A screen is fully playable while its device is switched OFF - which is only possible
    /// because the context is restored from control.json rather than waited for in a Hello
    /// (Part 3, Part 7).
    /// </summary>
    [Fact]
    public void The_computation_context_outlives_the_device()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Restore(
        [
            new KnownScreen(
                Device.Value,
                Screen.Value,
                "TISCH-PC//DISPLAY1",
                ScreenState.Inactive,
                new PixelSize(3840, 2160),
                144,
                ScreenSettings.Of(
                    ScreenContext.Default(new PixelSize(3840, 2160), 144) with
                    {
                        Placement = PlacementMode.Cascade,
                        DefaultRotationDeg = 180,
                    },
                    "Touch table")),
        ]);

        var context = catalog.ContextFor(target);

        Assert.Equal(new PixelSize(3840, 2160), context.Size);
        Assert.Equal(PlacementMode.Cascade, context.Placement);
        Assert.Equal(180, context.DefaultRotationDeg);
        Assert.Equal(ScreenState.Inactive, catalog.ViewOf(target)!.State);

        // Never connected, so not available - and that is a finding, not the state.
        Assert.Equal(SuppressReason.Unavailable, catalog.ViewOf(target)!.Suppressed);
    }

    /// <summary>
    /// The cross case, and the reason the configuration is a delta at all: both sides change
    /// DIFFERENT keys while the device is away, and each keeps its own (Part 4).
    /// </summary>
    [Fact]
    public void Each_side_keeps_the_key_it_changed()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);

        // The control changes the park edge while the device is off.
        catalog.Departed(Device);
        catalog.Change(target, new ScreenSettings(ParkEdge: ParkEdge.Top));

        // The device comes back and reports that IT changed the placement mode.
        catalog.Report(
            Device,
            [Info(Screen)],
            new ConfigUpdate([new ScreenConfigUpdate(Screen, new ScreenSettings(Placement: PlacementMode.Cascade))]));

        var context = catalog.ContextFor(target);

        Assert.Equal(PlacementMode.Cascade, context.Placement);
        Assert.Equal(ParkEdge.Top, context.ParkEdge);

        // And what the control changed goes out with the next drain, not before.
        var update = catalog.Drain(Device);
        var screen = Assert.Single(update.Screens);

        Assert.Equal(ParkEdge.Top, screen.Settings!.ParkEdge);
        Assert.Null(screen.Settings.Placement);
    }

    /// <summary>Where BOTH changed the same key, the control wins - and that is all it wins.</summary>
    [Fact]
    public void On_the_same_key_the_control_wins()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.Departed(Device);
        catalog.Change(target, new ScreenSettings(ParkEdge: ParkEdge.Top));

        catalog.Report(
            Device,
            [Info(Screen)],
            new ConfigUpdate([new ScreenConfigUpdate(Screen, new ScreenSettings(ParkEdge: ParkEdge.Bottom))]));

        Assert.Equal(ParkEdge.Top, catalog.ContextFor(target).ParkEdge);
    }

    /// <summary>
    /// A device never says how it STANDS, only how it is SET. One that sends a state anyway is
    /// passed over, and saying so is the difference between a rule and a silence (Part 3).
    /// </summary>
    [Fact]
    public void A_state_sent_by_a_device_is_passed_over()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Inactive);

        var refused = catalog.Apply(
            Device,
            new ConfigUpdate(
            [
                new ScreenConfigUpdate(
                    Screen,
                    new ScreenSettings(MaxScale: 5),
                    new ScreenCommand(ScreenState.Enabled)),
            ]));

        Assert.True(refused);
        Assert.Equal(ScreenState.Inactive, catalog.ViewOf(target)!.State);

        // The settings half of the very same message is applied - only the state is refused.
        Assert.Equal(5, catalog.ContextFor(target).MaxScale);
    }

    /// <summary>
    /// What is drained is what a freshly connected device is told, and it is gone afterwards -
    /// otherwise every reconnect would replay every change ever made.
    /// </summary>
    [Fact]
    public void What_has_gone_out_is_not_sent_again()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.Change(target, new ScreenSettings(MaxScale: 7));

        Assert.Equal(7, Assert.Single(catalog.Drain(Device).Screens).Settings!.MaxScale);
        Assert.Null(Assert.Single(catalog.Drain(Device).Screens).Settings);
    }

    /// <summary>
    /// The command half always travels, because it has one writer and therefore nothing to
    /// overwrite - which is also how a finding is CLEARED.
    /// </summary>
    [Fact]
    public void The_wish_and_the_finding_always_travel_complete()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Disabled);
        catalog.SetSuppress(target, SuppressReason.ControlWindow);

        var command = Assert.Single(catalog.Drain(Device).Screens).Command;

        Assert.Equal(ScreenState.Disabled, command!.State);
        Assert.Equal(SuppressReason.ControlWindow, command.Suppress);

        catalog.SetSuppress(target, null);

        Assert.Null(Assert.Single(catalog.Drain(Device).Screens).Command!.Suppress);
    }

    /// <summary>Findings are never written down - control.json carries the wish and nothing else.</summary>
    [Fact]
    public void A_finding_is_never_persisted()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Diagnostic);
        catalog.SetSuppress(target, SuppressReason.ControlWindow);

        var stored = Assert.Single(catalog.Snapshot());

        Assert.Equal(ScreenState.Diagnostic, stored.State);

        var again = new ScreenCatalog();
        again.Restore([stored]);

        Assert.Equal(ScreenState.Diagnostic, again.ViewOf(target)!.State);

        // Unavailable, because nothing has connected - not the finding that was set before.
        Assert.Equal(SuppressReason.Unavailable, again.ViewOf(target)!.Suppressed);
    }

    /// <summary>
    /// The catalogue says when it changed, so the control can write. Polling would be quietly
    /// broken: the configuration file debounces, so a save on every tick would push its own
    /// deadline out for ever and write nothing at all.
    /// </summary>
    [Fact]
    public void A_change_worth_keeping_announces_itself()
    {
        var catalog = new ScreenCatalog();
        var target = new ScreenRef(Device, Screen);
        var announced = 0;

        catalog.Changed += () => announced++;

        catalog.Report(Device, [Info(Screen)], reported: null);
        catalog.SetState(target, ScreenState.Inactive);
        catalog.Change(target, new ScreenSettings(MaxScale: 3));

        Assert.Equal(3, announced);

        // Setting the same wish again is not a change.
        catalog.SetState(target, ScreenState.Inactive);
        Assert.Equal(3, announced);
    }

    private static ScreenInfo Info(ScreenId screen) => Info(screen, new PixelSize(1920, 1080), 96);

    private static ScreenInfo Info(ScreenId screen, PixelSize size, double dpi) =>
        new(screen, "TISCH-PC//DISPLAY1", null, size, dpi, IsPrimary: true);
}
