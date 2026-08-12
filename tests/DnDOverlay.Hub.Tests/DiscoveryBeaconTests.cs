using System.Net;
using System.Net.Sockets;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The beacon really goes out - over the loopback device, which is the case itself rather than a
/// stand-in: control and display on one machine are a regular setup (Part 2).
/// <para>
/// What this cannot show is the machine with a dock, a WLAN and a Hyper-V adapter, where sending
/// to only one interface is the mistake that hides for months. That stays a step by hand in M1c.
/// </para>
/// </summary>
public sealed class DiscoveryBeaconTests
{
    [Fact(Timeout = 30_000)]
    public async Task A_control_announces_itself_with_four_things()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var control = Guid.NewGuid();

        using var ear = new UdpClient();
        ear.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        ear.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort));

        var beacon = new DiscoveryBeacon(
            Options.Create(new HubOptions { ControlId = control, Port = 47811 }),
            TimeProvider.System,
            NullLogger<DiscoveryBeacon>.Instance);

        await beacon.StartAsync(cancellationToken);

        try
        {
            // Whatever else is on this network, only ours counts - which is the same filter a
            // paired display applies (Part 4).
            while (true)
            {
                var datagram = await ear.ReceiveAsync(cancellationToken);
                var heard = DiscoveryJson.Parse(datagram.Buffer);

                if (heard?.ControlId != control)
                {
                    continue;
                }

                Assert.Equal(47811, heard.Port);
                Assert.Equal(Protocol.Version, heard.ProtocolVersion);
                Assert.False(string.IsNullOrWhiteSpace(heard.Name));

                return;
            }
        }
        finally
        {
            await beacon.StopAsync(cancellationToken);
        }
    }
}
