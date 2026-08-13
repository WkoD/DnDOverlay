using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DnDOverlay.Hub;

/// <summary>One address this machine answers on, and whether its interface leads anywhere.</summary>
/// <param name="HasGateway">
/// Whether this interface has a default gateway. It is the one signal that separates a real
/// network from a Hyper-V, WSL or VPN adapter without guessing at adapter types - and guessing is
/// what makes a control show its 172.x address while the table waits on the 192.168.x one
/// (Part 4).
/// </param>
public sealed record LocalAddress(string Interface, IPAddress Address, bool HasGateway)
{
    public override string ToString() => Address.ToString();
}

/// <summary>
/// Which address to put in front of the DM - the one he types at a display when discovery does
/// not get through.
/// <para>
/// It is a different question from the one the beacon asks. The beacon goes out on <b>every</b>
/// usable interface, because a datagram into a virtual subnet costs one datagram while skipping a
/// real interface costs a device that never finds its control. Here exactly one thing is being
/// shown to a person, so a list of six is as useless as the wrong one.
/// </para>
/// </summary>
public static class LocalAddresses
{
    /// <summary>What this machine has, as the operating system reports it.</summary>
    public static IReadOnlyList<LocalAddress> Enumerate()
    {
        var found = new List<LocalAddress>();

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = adapter.GetIPProperties();

            var hasGateway = properties.GatewayAddresses
                .Any(gateway => gateway.Address is { } address
                    && address.AddressFamily == AddressFamily.InterNetwork
                    && !address.Equals(IPAddress.Any));

            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(address.Address))
                {
                    continue;
                }

                found.Add(new LocalAddress(adapter.Name, address.Address, hasGateway));
            }
        }

        return found;
    }

    /// <summary>
    /// What to show, out of what was found. A pure function, so the choice is testable without a
    /// network card.
    /// <para>
    /// The rule from Part 4: the address of the interface with the default gateway - and where
    /// several qualify, <b>all</b> of them rather than one guessed at. Where none does, everything
    /// is shown: a machine on a switch without a gateway is a perfectly good table network, and
    /// showing nothing would be the one answer that helps nobody.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LocalAddress> Preferred(IReadOnlyList<LocalAddress> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        var routed = found.Where(address => address.HasGateway).ToList();

        return routed.Count > 0 ? routed : [.. found];
    }

    /// <summary>Both steps at once - what the reachability view shows.</summary>
    public static IReadOnlyList<LocalAddress> Preferred() => Preferred(Enumerate());

    /// <summary>
    /// Whether a connected device is running on THIS machine - the question behind "a screen the
    /// control window lies on gets no overlay" (Part 2).
    /// <para>
    /// The plan said this was answered by the loopback interface, and <b>measured it is not</b>: a
    /// display on the same machine takes whichever beacon it hears first, and the beacon goes out
    /// on every interface. A display three centimetres away therefore connects to
    /// <c>192.168.178.23</c> as readily as to <c>127.0.0.1</c>, and a loopback test finds it in
    /// neither case reliably. The sound rule is the wider one: <b>an address this machine answers
    /// on cannot belong to another machine.</b>
    /// </para>
    /// <para>
    /// A pure function over what was handed in, so the decision is testable without a network card
    /// - and it has to be tested, because the wiring around it lives in the WPF application where
    /// nothing runs.
    /// </para>
    /// </summary>
    /// <param name="address">What the hub recorded for that connection, or <see langword="null"/>.</param>
    /// <param name="own">This machine's addresses, from <see cref="Enumerate"/>.</param>
    public static bool IsThisMachine(string? address, IReadOnlyList<LocalAddress> own)
    {
        ArgumentNullException.ThrowIfNull(own);

        if (!IPAddress.TryParse(address, out var peer))
        {
            return false;
        }

        // Kestrel reports an IPv4 peer on a dual-stack socket as ::ffff:192.168.178.23, and the
        // enumeration collects plain IPv4. Without unwrapping, the comparison below silently never
        // matches - the same shape of fault as the loopback assumption it replaces.
        if (peer.IsIPv4MappedToIPv6)
        {
            peer = peer.MapToIPv4();
        }

        return IPAddress.IsLoopback(peer) || own.Any(local => local.Address.Equals(peer));
    }
}
