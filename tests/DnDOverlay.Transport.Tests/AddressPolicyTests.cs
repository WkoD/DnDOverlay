using System.Net;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The four groups of Part 5, each in every dress it comes in. A pure function over an address, so
/// it is provable without a network - and it has to be, because the thing it guards against is
/// precisely what a test network would look like.
/// </summary>
public sealed class AddressPolicyTests
{
    /// <summary>
    /// The addresses the plan names: loopback, link-local, private, multicast. <c>169.254.169.254</c>
    /// stands there by name in Part 5 - it is the cloud metadata address, the single most fetched
    /// target of this kind of mistake.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.4")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    public void An_address_inside_the_house_is_refused_with_a_reason(string address)
    {
        var why = AddressPolicy.Refuses(IPAddress.Parse(address));

        Assert.NotNull(why);
        Assert.NotEmpty(why);
    }

    /// <summary>
    /// <b>The line that carries the whole check.</b> An IPv4 address wrapped as IPv6 is the same
    /// machine wearing a hat, and every range test would call it public. Refused unwrapped, it is
    /// loopback like any other.
    /// </summary>
    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void An_ipv4_address_wearing_an_ipv6_hat_is_refused_all_the_same(string address)
    {
        Assert.NotNull(AddressPolicy.Refuses(IPAddress.Parse(address)));
    }

    /// <summary>
    /// The counter-check, and it matters as much as the refusals: a check that refuses everything
    /// is not a check, it is an outage. These are ordinary public addresses and they go through.
    /// </summary>
    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("192.167.1.1")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void An_ordinary_public_address_goes_through(string address)
    {
        Assert.Null(AddressPolicy.Refuses(IPAddress.Parse(address)));
    }

    /// <summary>
    /// The edges of the private ranges, from both sides. Off-by-one here does not fail loudly - it
    /// either opens a hole or refuses a real address, and both look like nothing at all.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.0", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("11.0.0.0", false)]
    [InlineData("9.255.255.255", false)]
    [InlineData("172.16.0.0", true)]
    [InlineData("172.31.0.0", true)]
    [InlineData("169.253.255.255", false)]
    [InlineData("169.255.0.0", false)]
    [InlineData("223.255.255.255", false)]
    [InlineData("224.0.0.0", true)]
    [InlineData("239.255.255.255", true)]
    [InlineData("240.0.0.0", true)]
    public void The_edges_of_the_ranges_sit_where_the_rfcs_put_them(string address, bool refused)
    {
        Assert.Equal(refused, AddressPolicy.Refuses(IPAddress.Parse(address)) is not null);
    }
}
