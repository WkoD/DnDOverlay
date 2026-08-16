using System.Net;
using System.Net.Sockets;

namespace DnDOverlay.Transport;

/// <summary>
/// Which addresses a URL import may reach, in the four groups Part 5 names: loopback, link-local,
/// private and multicast are refused, everything else is allowed.
/// <para>
/// <b>Why this exists at all:</b> what gets fetched is whatever stood in the clipboard, and the DM's
/// machine stands INSIDE the home network. A URL on <c>127.0.0.1</c>, <c>192.168.x.x</c> or
/// <c>169.254.169.254</c> would reach router pages, a NAS or our own hub, and the answer would land
/// on the table as a picture. That needs no attacker - a redirect copied out of Discord is enough.
/// </para>
/// <para>
/// <b>Each group is refused in every dress it comes in</b>, which is why this list is longer than
/// the four names. An IPv4 address wrapped as <c>::ffff:127.0.0.1</c> is loopback; unique-local
/// <c>fc00::/7</c> is what "private" is called in IPv6; the shared range <c>100.64/10</c> is a
/// carrier's inside rather than the public internet. Refusing them costs nothing - nobody serves
/// pictures from there - and each one left out would be the whole check's way around itself.
/// </para>
/// </summary>
public static class AddressPolicy
{
    /// <summary>
    /// Why <paramref name="address"/> may not be fetched from, or <c>null</c> when it may.
    /// <para>
    /// A reason rather than a bare "no", because the DM has to be told which of their pastes was
    /// turned away and why - "points into your own network" is an answer, "refused" is not.
    /// </para>
    /// </summary>
    public static string? Refuses(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Unwrapped FIRST, and it is the single most important line here: ::ffff:127.0.0.1 is
        // loopback wearing an IPv6 hat, and every check below would say "public" about it.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return "the address is loopback - it points at the machine itself";
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return "the address is unspecified";
        }

        if (address.IsIPv6Multicast)
        {
            return "the address is multicast";
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
        {
            return "the address is inside a local IPv6 range";
        }

        return address.AddressFamily is AddressFamily.InterNetwork ? RefusesIPv4(address) : null;
    }

    /// <summary>
    /// The IPv4 groups, read off the first bytes. Written out rather than expressed as masks: the
    /// ranges are fixed, and a table anyone can check against RFC 1918 beats arithmetic nobody
    /// rereads.
    /// </summary>
    private static string? RefusesIPv4(IPAddress address)
    {
        var octets = address.GetAddressBytes();

        return octets switch
        {
            [0, ..] => "the address is in 0.0.0.0/8, which is not a destination",
            [10, ..] => "the address is private (10.0.0.0/8)",
            [100, >= 64 and <= 127, ..] => "the address is in the shared range 100.64.0.0/10",
            [127, ..] => "the address is loopback - it points at the machine itself",
            [169, 254, ..] => "the address is link-local (169.254.0.0/16)",
            [172, >= 16 and <= 31, ..] => "the address is private (172.16.0.0/12)",
            [192, 168, ..] => "the address is private (192.168.0.0/16)",
            [>= 224 and <= 239, ..] => "the address is multicast",
            [>= 240, ..] => "the address is reserved",
            _ => null,
        };
    }
}
