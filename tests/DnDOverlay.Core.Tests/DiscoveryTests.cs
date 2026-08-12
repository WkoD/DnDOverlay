using System.Text;
using DnDOverlay.Core.Protocol;

// The test namespace has a Protocol folder of its own, so the constant needs saying which one.
using Wire = DnDOverlay.Core.Protocol.Protocol;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// The beacon: four things, and a reader that never falls over.
/// <para>
/// This one is fed by the open network - anybody may send anything to that port - so the parser
/// is the last place that should be lenient, and the first that must not throw (Part 4).
/// </para>
/// </summary>
public sealed class DiscoveryTests
{
    [Fact]
    public void A_beacon_survives_the_wire()
    {
        var beacon = new Beacon(Guid.NewGuid(), "DM-SURFACE", 47800, Wire.Version);

        var read = DiscoveryJson.Parse(DiscoveryJson.Serialise(beacon));

        Assert.Equal(beacon, read);
    }

    /// <summary>
    /// It is unauthenticated and readable by everyone on the network, so what is NOT in it matters
    /// as much as what is: no device list, no versions of paired machines, no screen names
    /// (Part 4).
    /// </summary>
    [Fact]
    public void A_beacon_says_four_things_and_no_more()
    {
        var json = Encoding.UTF8.GetString(
            DiscoveryJson.Serialise(new Beacon(Guid.NewGuid(), "DM-SURFACE", 47800, Wire.Version)));

        Assert.Equal(4, typeof(Beacon).GetProperties().Length);
        Assert.Contains("controlId", json, StringComparison.Ordinal);
        Assert.Contains("name", json, StringComparison.Ordinal);
        Assert.Contains("port", json, StringComparison.Ordinal);
        Assert.Contains("protocolVersion", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Anything that is not one of ours is <see langword="null"/>, never an exception: a stray
    /// broadcast from a different program is the normal state of a home network, and one throw
    /// per stray packet would make the listener a denial-of-service target against itself.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"controlId\":\"not-a-guid\"}")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void Anything_that_is_not_a_beacon_is_simply_not_one(string datagram)
    {
        Assert.Null(DiscoveryJson.Parse(Encoding.UTF8.GetBytes(datagram)));
    }

    [Fact]
    public void An_oversized_datagram_is_refused_before_it_is_read()
    {
        Assert.Null(DiscoveryJson.Parse(new byte[DiscoveryJson.MaxBytes + 1]));
    }

    /// <summary>
    /// A port out of range would be turned into a connection attempt, so it is refused here
    /// rather than at the socket.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void A_beacon_with_an_impossible_port_is_refused(int port)
    {
        var json = $$"""{"controlId":"{{Guid.Empty}}","name":"X","port":{{port}},"protocolVersion":1}""";

        Assert.Null(DiscoveryJson.Parse(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// A beacon from a newer build carries fields this one does not know. Ignoring them is what
    /// lets an older display find a newer control at all (rule 7).
    /// </summary>
    [Fact]
    public void A_beacon_from_a_newer_build_is_still_a_beacon()
    {
        var control = Guid.NewGuid();
        var json = $$"""
            {"controlId":"{{control}}","name":"DM-SURFACE","port":47800,"protocolVersion":9,"somethingNew":true}
            """;

        var beacon = DiscoveryJson.Parse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(beacon);
        Assert.Equal(control, beacon.ControlId);
        Assert.Equal(9, beacon.ProtocolVersion);
    }
}
