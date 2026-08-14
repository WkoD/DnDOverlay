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

    /// <summary>
    /// What the last round went out to, so a change can be said out loud and a repetition cannot.
    /// <para>
    /// Null until the first round, which is what makes the first one a change: the list is then
    /// written once at startup and after that only when something actually moves.
    /// </para>
    /// </summary>
    private string? _lastTargets;

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
        var targets = Targets().ToList();

        Note(targets);

        foreach (var target in targets)
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
    /// Writes down where the beacon goes - but only when that changes.
    /// <para>
    /// "Every suitable interface, not the first" is the one promise in discovery that could not be
    /// read anywhere: a start line says the beacon runs, a failure line says one interface would
    /// not carry it, and silence says at least one did. None of them answers *where it went* - and
    /// the failure this guards against is the quietest one the system has, because a display PC
    /// that never hears anything simply stays still (Part 4).
    /// </para>
    /// <para>
    /// Only on a change, because the beacon goes out every two seconds and a line per round would
    /// bury the file it is meant to explain. A change is exactly the interesting moment anyway:
    /// docking, Wi-Fi going on or off, a VPN coming up. The first round counts as one, so the list
    /// stands once at startup.
    /// </para>
    /// <para>
    /// That filter is also what makes the line <b>Information</b> rather than Debug: what would
    /// have justified Debug is a repetition that cannot happen any more (Part 8).
    /// </para>
    /// </summary>
    private void Note(List<(IPAddress From, IPAddress To)> targets)
    {
        // Formatted before the comparison rather than after: the text IS the identity here, so
        // there is no second notion of "the same set" that could drift from what gets printed.
        var written = string.Join(", ", targets.Select(target => $"{target.From} -> {target.To}"));

        if (string.Equals(written, _lastTargets, StringComparison.Ordinal))
        {
            return;
        }

        _lastTargets = written;

        HubLog.BeaconTargetsChanged(_logger, targets.Count, written);
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
