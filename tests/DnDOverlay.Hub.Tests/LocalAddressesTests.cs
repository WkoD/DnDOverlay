using System.Net;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// Which address is put in front of the DM. The choice is a pure function over what was found, so
/// it is testable without a network card - the enumeration itself is BCL and has nothing of ours
/// in it.
/// </summary>
public sealed class LocalAddressesTests
{
    /// <summary>
    /// The failure this is written against: a Surface with Hyper-V shows its 172.x address, the DM
    /// types it at the table, and nothing connects. The default gateway is the one signal that
    /// separates a real network from a virtual adapter without guessing at adapter types (Part 4).
    /// </summary>
    [Fact]
    public void The_interface_with_a_gateway_wins()
    {
        var found = new[]
        {
            new LocalAddress("vEthernet (Default Switch)", IPAddress.Parse("172.20.16.1"), HasGateway: false),
            new LocalAddress("Wi-Fi", IPAddress.Parse("192.168.1.20"), HasGateway: true),
        };

        var shown = Assert.Single(LocalAddresses.Preferred(found));

        Assert.Equal(IPAddress.Parse("192.168.1.20"), shown.Address);
    }

    /// <summary>
    /// A Surface in its dock has WLAN and Ethernet at once, and both work. Showing one of them
    /// would be a guess; showing both is the answer (Part 4).
    /// </summary>
    [Fact]
    public void Several_routed_interfaces_are_all_shown()
    {
        var found = new[]
        {
            new LocalAddress("Wi-Fi", IPAddress.Parse("192.168.1.20"), HasGateway: true),
            new LocalAddress("Ethernet", IPAddress.Parse("10.0.0.5"), HasGateway: true),
            new LocalAddress("vEthernet (WSL)", IPAddress.Parse("172.20.16.1"), HasGateway: false),
        };

        var shown = LocalAddresses.Preferred(found);

        Assert.Equal(2, shown.Count);
        Assert.DoesNotContain(shown, address => address.Interface.StartsWith("vEthernet", StringComparison.Ordinal));
    }

    /// <summary>
    /// A machine on a switch with no gateway is a perfectly good table network. Showing nothing
    /// there would be the one answer that helps nobody.
    /// </summary>
    [Fact]
    public void Without_any_gateway_everything_is_shown()
    {
        var found = new[]
        {
            new LocalAddress("Ethernet", IPAddress.Parse("169.254.3.4"), HasGateway: false),
            new LocalAddress("Ethernet 2", IPAddress.Parse("10.1.1.1"), HasGateway: false),
        };

        Assert.Equal(2, LocalAddresses.Preferred(found).Count);
    }

    [Fact]
    public void Nothing_found_stays_nothing()
    {
        Assert.Empty(LocalAddresses.Preferred([]));
    }

    /// <summary>
    /// The enumeration runs on both platforms in CI and must not throw on either. What it finds is
    /// whatever the runner has, so that is all this asserts - loopback is out, which is the one
    /// thing that holds everywhere.
    /// </summary>
    [Fact]
    public void Enumerating_this_machine_leaves_loopback_out()
    {
        Assert.DoesNotContain(LocalAddresses.Enumerate(), address => IPAddress.IsLoopback(address.Address));
    }

    /// <summary>
    /// Whether a connected device is on THIS machine - and the case it is written against is the
    /// one that made the whole rule wrong: a display three centimetres away, on the same machine,
    /// connecting to the LAN address rather than to loopback. Measured, not imagined.
    /// <para>
    /// The plan answered this with "the connection comes over the loopback interface". It does not
    /// have to: a display takes whichever beacon it hears first, and the beacon goes out on every
    /// interface. What holds instead is wider and no weaker - an address this machine answers on
    /// cannot belong to another machine.
    /// </para>
    /// </summary>
    [Fact]
    public void A_device_on_our_own_lan_address_is_on_this_machine()
    {
        var own = new[]
        {
            new LocalAddress("Wi-Fi", IPAddress.Parse("192.168.178.23"), HasGateway: true),
            new LocalAddress("vEthernet", IPAddress.Parse("172.20.16.1"), HasGateway: false),
        };

        Assert.True(LocalAddresses.IsThisMachine("192.168.178.23", own));
        Assert.True(LocalAddresses.IsThisMachine("172.20.16.1", own));

        // Loopback stays in, of course - it is the other way the same machine can arrive.
        Assert.True(LocalAddresses.IsThisMachine("127.0.0.1", own));
        Assert.True(LocalAddresses.IsThisMachine("::1", own));
    }

    /// <summary>
    /// The half that must NOT widen with it: a real second machine on the same subnet. Getting this
    /// wrong would suppress a stranger's table from here.
    /// </summary>
    [Fact]
    public void A_device_on_another_machine_is_not()
    {
        var own = new[] { new LocalAddress("Wi-Fi", IPAddress.Parse("192.168.178.23"), HasGateway: true) };

        Assert.False(LocalAddresses.IsThisMachine("192.168.178.24", own));
        Assert.False(LocalAddresses.IsThisMachine("unknown", own));
        Assert.False(LocalAddresses.IsThisMachine(null, own));
        Assert.False(LocalAddresses.IsThisMachine("192.168.178.23", []));
    }

    /// <summary>
    /// Kestrel reports an IPv4 peer on a dual-stack socket in its mapped form. Without unwrapping,
    /// the comparison never matches - and it never matches SILENTLY, which is the same shape of
    /// fault as the loopback assumption this replaces.
    /// </summary>
    [Fact]
    public void An_ipv4_peer_in_its_mapped_form_is_still_recognised()
    {
        var own = new[] { new LocalAddress("Wi-Fi", IPAddress.Parse("192.168.178.23"), HasGateway: true) };

        Assert.True(LocalAddresses.IsThisMachine("::ffff:192.168.178.23", own));
        Assert.False(LocalAddresses.IsThisMachine("::ffff:192.168.178.24", own));
    }
}
