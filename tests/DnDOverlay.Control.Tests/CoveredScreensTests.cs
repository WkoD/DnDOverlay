using System.Net;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The control's own window keeps its own overlay off the stage (Part 2): which screens it holds back,
/// and that only its own finding is ever undone (Part 3).
/// </summary>
public sealed class CoveredScreensTests
{
    private static readonly DeviceId Here = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f1"));
    private static readonly DeviceId Clone = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f2"));
    private static readonly ScreenId Monitor = new(@"\\?\DISPLAY#DEL1234#1");
    private static readonly ScreenId Other = new(@"\\?\DISPLAY#DEL1234#2");
    private static readonly IReadOnlyList<LocalAddress> Own = [new("Ethernet", IPAddress.Parse("192.168.178.20"), HasGateway: true)];

    /// <summary>A display on this machine, under the control's window, showing an overlay: held back.</summary>
    [Fact]
    public void A_screen_of_this_machine_under_the_window_is_held_back()
    {
        var wanted = CoveredScreens.Wanted([Device(Here, "192.168.178.20", Monitor, Other)], Own, Covering(Monitor), Overlay);

        Assert.Equal([new ScreenRef(Here, Monitor)], wanted);
    }

    /// <summary>
    /// A cloned display PC reports the very same monitor path (Part 3): on another machine it is a
    /// stranger's table, and the control's window lies on none of its screens.
    /// </summary>
    [Fact]
    public void The_same_monitor_path_on_another_machine_is_not_held_back()
    {
        var wanted = CoveredScreens.Wanted([Device(Clone, "192.168.178.31", Monitor)], Own, Covering(Monitor), Overlay);

        Assert.Empty(wanted);
    }

    /// <summary>The display on this machine reaches the hub at the LAN address, not at loopback - both are this machine.</summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:192.168.178.20")]
    public void Loopback_and_a_mapped_own_address_are_this_machine(string address)
    {
        var wanted = CoveredScreens.Wanted([Device(Here, address, Monitor)], Own, Covering(Monitor), Overlay);

        Assert.Single(wanted);
    }

    /// <summary>A device that has left holds nothing back - there is no overlay to keep off the stage.</summary>
    [Fact]
    public void A_device_that_is_not_connected_holds_nothing_back()
    {
        var gone = Device(Here, "192.168.178.20", Monitor) with { Connected = false };

        Assert.Empty(CoveredScreens.Wanted([gone], Own, Covering(Monitor), Overlay));
    }

    /// <summary>A screen shown as a window slips behind the stage like any window: not held back (Part 6).</summary>
    [Fact]
    public void A_screen_shown_as_a_window_is_not_held_back()
    {
        var wanted = CoveredScreens.Wanted([Device(Here, "192.168.178.20", Monitor)], Own, Covering(Monitor), _ => ScreenPresentation.Window);

        Assert.Empty(wanted);
    }

    /// <summary>The window moved from one monitor to the other: the first is released, the second suppressed.</summary>
    [Fact]
    public void Moving_the_window_releases_one_screen_and_holds_the_next()
    {
        var first = new ScreenRef(Here, Monitor);
        var second = new ScreenRef(Here, Other);

        var (suppress, release) = CoveredScreens.Changes(new HashSet<ScreenRef> { first }, new HashSet<ScreenRef> { second });

        Assert.Equal([second], suppress);
        Assert.Equal([first], release);
    }

    /// <summary>A settled window sends nothing: what is held and wanted is neither suppressed again nor released.</summary>
    [Fact]
    public void A_settled_window_changes_nothing()
    {
        var held = new HashSet<ScreenRef> { new(Here, Monitor) };

        var (suppress, release) = CoveredScreens.Changes(held, new HashSet<ScreenRef>(held));

        Assert.Empty(suppress);
        Assert.Empty(release);
    }

    private static DeviceView Device(DeviceId device, string address, params ScreenId[] screens) =>
        new(
            device,
            "TISCH-PC",
            Connected: true,
            [.. screens.Select(screen => new ScreenView(new ScreenRef(device, screen), new ScreenInfo(screen, "TISCH-PC//1", null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled))],
            Address: address);

    private static HashSet<ScreenId> Covering(params ScreenId[] screens) => [.. screens];

    private static ScreenPresentation Overlay(ScreenRef screen) => ScreenPresentation.Overlay;
}
