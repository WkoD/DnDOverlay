using System.Net;
using System.Net.Sockets;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport;

/// <summary>Where a control was heard, and what it said about itself.</summary>
public sealed record Sighting(Beacon Beacon, IPAddress Address)
{
    /// <summary>
    /// The address comes from the DATAGRAM, never from the beacon's contents. A control that
    /// announced its own idea of its address would be announcing the wrong one on every machine
    /// with more than one interface - and it is exactly the machine with a dock and a WLAN that
    /// this has to work on (Part 4).
    /// </summary>
    public string Host => Address.ToString();
}

/// <summary>
/// Listens for controls announcing themselves, so a display PC finds one without anybody typing
/// an address at a machine that has no keyboard.
/// <para>
/// It stays running even when a host is configured: the stored address is a PREFERRED one, not an
/// exclusive one, because it changes when the Surface moves between Wi-Fi and its dock (Part 4).
/// </para>
/// </summary>
public sealed class DiscoveryListener
{
    private readonly ILogger<DiscoveryListener> _logger;

    public DiscoveryListener(ILogger<DiscoveryListener> logger) => _logger = logger;

    /// <summary>
    /// Waits for a control this device may talk to.
    /// </summary>
    /// <param name="boundTo">
    /// The control this display is paired with, or <see langword="null"/> while it is unpaired. A
    /// paired display discards foreign beacons: the address is no good for telling controls apart
    /// - it changes - and a second control in the same network is no invention (Part 4).
    /// </param>
    public async Task<Sighting?> ListenAsync(Guid? boundTo, CancellationToken cancellationToken)
    {
        using var socket = new UdpClient();

        try
        {
            // Two displays on one machine are a regular setup, and both want to hear the same
            // datagrams - so the port is shared rather than owned.
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort));

            TransportLog.ListeningForControls(_logger, Protocol.DiscoveryPort);

            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var beacon = DiscoveryJson.Parse(datagram.Buffer);

                if (beacon is null)
                {
                    // Somebody else's broadcast, or noise. Not worth a line at any level that is
                    // on by default - this port belongs to the open network.
                    continue;
                }

                if (boundTo is { } control && beacon.ControlId != control)
                {
                    TransportLog.ForeignControlIgnored(_logger, beacon.ControlId, beacon.Name);
                    continue;
                }

                TransportLog.ControlHeard(_logger, beacon.Name, datagram.RemoteEndPoint.Address, beacon.Port);

                return new Sighting(beacon, datagram.RemoteEndPoint.Address);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or a configured host answered first.
        }
        catch (SocketException exception)
        {
            // Somebody else holds the port in a way that will not share, or there is no network
            // at all. Neither is fatal: the host by hand is the documented way through (Part 4).
            TransportLog.ListeningFailed(_logger, exception, Protocol.DiscoveryPort);
        }

        return null;
    }
}
