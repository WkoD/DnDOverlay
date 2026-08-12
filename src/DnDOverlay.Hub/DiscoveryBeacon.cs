using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub;

/// <summary>
/// Says "here I am" into the network every couple of seconds, so a display PC that was just set up
/// appears at the control without anybody typing an address.
/// <para>
/// A hosted service, so it listens before the surface stands (rule 5): a display PC on autostart
/// can be quicker than the Surface is at drawing its stage.
/// </para>
/// </summary>
public sealed class DiscoveryBeacon : BackgroundService
{
    private readonly IOptions<HubOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<DiscoveryBeacon> _logger;

    public DiscoveryBeacon(IOptions<HubOptions> options, TimeProvider time, ILogger<DiscoveryBeacon> logger)
    {
        _options = options;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var beacon = DiscoveryJson.Serialise(new Beacon(
            _options.Value.ControlId,
            Environment.MachineName,
            _options.Value.Port,
            Protocol.Version));

        HubLog.BeaconStarted(_logger, Protocol.DiscoveryPort);

        using var ticks = new PeriodicTimer(Protocol.BeaconInterval, _time);

        do
        {
            Announce(beacon);
        }
        while (await Safely(ticks, stoppingToken).ConfigureAwait(false));

        HubLog.BeaconStopped(_logger);
    }

    /// <summary>
    /// Sends to every suitable interface, and the addresses are looked up EVERY round rather than
    /// once at the start. That is not thrift with sockets - it is the dock: the Surface changes
    /// its addresses when it is plugged in, and a beacon bound to what was there at startup would
    /// keep shouting into a network that no longer exists (Part 4).
    /// </summary>
    private void Announce(byte[] beacon)
    {
        var reached = 0;

        foreach (var target in Targets())
        {
            try
            {
                using var socket = new UdpClient(new IPEndPoint(target.From, 0)) { EnableBroadcast = true };

                socket.Send(beacon, beacon.Length, new IPEndPoint(target.To, Protocol.DiscoveryPort));
                reached++;
            }
            catch (SocketException exception)
            {
                // One interface that will not carry us is not an outage: the others still do, and
                // this is the ordinary state of a machine with VPN or Hyper-V adapters.
                HubLog.BeaconInterfaceFailed(_logger, exception, target.From);
            }
        }

        if (reached == 0)
        {
            HubLog.BeaconReachedNobody(_logger);
        }
    }

    /// <summary>
    /// Every IPv4 address that is up, with the broadcast address of its own subnet.
    /// <para>
    /// <b>The loopback device is explicitly among them</b>, because control and display on one
    /// machine are a regular setup and not a development mode (Part 2). Without it the display
    /// would need <c>--host</c> on the very machine where everything is closest together.
    /// </para>
    /// <para>
    /// <b>Virtual adapters are NOT sorted out</b>, and that is a correction to the plan. Telling a
    /// Hyper-V or VPN adapter apart from a real one is guesswork - they present themselves as
    /// Ethernet - and the two mistakes do not cost the same: a beacon into a virtual subnet
    /// reaches nobody and costs one datagram, while wrongly skipping a real interface costs a
    /// device that never finds its control and a DM who searches the network. What the plan is
    /// really about is not sending to only the FIRST interface, and that is what this does.
    /// </para>
    /// </summary>
    private static IEnumerable<(IPAddress From, IPAddress To)> Targets()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(address.Address))
                {
                    yield return (address.Address, IPAddress.Loopback);
                    continue;
                }

                if (Broadcast(address) is { } broadcast)
                {
                    yield return (address.Address, broadcast);
                }
            }
        }
    }

    /// <summary>
    /// The broadcast address of this address's own subnet - never 255.255.255.255. A limited
    /// broadcast leaves through whichever interface the routing table happens to prefer, and on a
    /// machine with a docking station and Hyper-V that is exactly the wrong one.
    /// </summary>
    private static IPAddress? Broadcast(UnicastIPAddressInformation address)
    {
        if (address.IPv4Mask is null)
        {
            return null;
        }

        var host = address.Address.GetAddressBytes();
        var mask = address.IPv4Mask.GetAddressBytes();

        if (host.Length != mask.Length)
        {
            return null;
        }

        for (var i = 0; i < host.Length; i++)
        {
            host[i] = (byte)(host[i] | ~mask[i]);
        }

        return new IPAddress(host);
    }

    private static async Task<bool> Safely(PeriodicTimer ticks, CancellationToken stoppingToken)
    {
        try
        {
            return await ticks.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
