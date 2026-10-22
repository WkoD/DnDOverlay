using System.Net.WebSockets;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>The coarse device commands (Part 7): a rename works without the device, clearing and restarting do not.</summary>
public sealed class DeviceCommandTests
{
    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000009"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A device renamed while it is switched off carries the new name in the lists at once. The
    /// device hears it when it connects, from the comparison every connection makes (over the wire,
    /// next door).
    /// </summary>
    [Fact]
    public async Task A_rename_while_off_changes_the_name_in_the_lists_at_once()
    {
        var directory = Directory();

        using var session = Session(directory);

        Assert.True(await session.SendDeviceCommandAsync(Device, DeviceCommand.Rename, "Beamer", Cancellation));

        Assert.Equal("Beamer", Assert.Single(directory.Paired).Name);
    }

    /// <summary>
    /// A connection on its way out still stands in the list for a moment and takes nothing. A command
    /// sent into it is not reported as sent - before, "restart" said done, and nobody repeated it.
    /// </summary>
    [Fact]
    public async Task A_command_into_a_connection_that_takes_nothing_is_not_reported_sent()
    {
        var connections = new DisplayConnections();
        var options = new HubOptions();

        using var socket = WebSocket.CreateFromStream(Stream.Null, new WebSocketCreationOptions { IsServer = true });
        using var outgoing = new SendQueues(
            socket, options.SendLimits, new SocketReport("127.0.0.1", NullLogger.Instance), TimeProvider.System);

        connections.Add(new DisplayConnection(
            Device,
            [new ScreenId("DISPLAY1")],
            new HelloMessage(Device, "WAND-PC", "1.0.0", Protocol.Version, [], "a-token", null, null, null),
            "127.0.0.1",
            outgoing,
            new Liveness(TimeProvider.System, options, outgoing.TrySend)));

        outgoing.Dispose();

        using var session = Session(connections: connections);

        Assert.False(await session.SendDeviceCommandAsync(Device, DeviceCommand.Restart, cancellationToken: Cancellation));
        Assert.False(await session.SendWindowCommandAsync(
            new ScreenRef(Device, new ScreenId("DISPLAY1")), 42, "notepad", WindowAction.Minimize, null, Cancellation));
    }

    /// <summary>
    /// The device tree is built from the paired devices, so a rename of a device that is switched off
    /// has to reach it although no connection and no screen moved. Before, only the pairing desk was
    /// published, and the Devices window redrew the band under the old name.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_rename_while_off_reaches_the_device_tree_at_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var session = Session();

        await using var stream = session.Subscribe(cancellationToken).GetAsyncEnumerator(cancellationToken);

        Assert.True(await stream.MoveNextAsync());
        Assert.IsType<SessionEvent.Opening>(stream.Current);

        Assert.True(await session.SendDeviceCommandAsync(Device, DeviceCommand.Rename, "Beamer", cancellationToken));

        SessionEvent.DevicesChanged? devices = null;

        while (devices is null && await stream.MoveNextAsync())
        {
            devices = stream.Current as SessionEvent.DevicesChanged;
        }

        Assert.Equal("Beamer", Assert.Single(devices!.Devices).Name);
    }

    /// <summary>Clearing the store and restarting reach a connected device only - and say so, rather than vanishing.</summary>
    [Fact]
    public async Task Clearing_and_restarting_need_the_device_connected()
    {
        using var session = Session();

        Assert.False(await session.SendDeviceCommandAsync(Device, DeviceCommand.ClearCache, cancellationToken: Cancellation));
        Assert.False(await session.SendDeviceCommandAsync(Device, DeviceCommand.Restart, cancellationToken: Cancellation));
    }

    /// <summary>A device that is not paired cannot be renamed.</summary>
    [Fact]
    public async Task An_unknown_device_is_not_renamed()
    {
        using var session = Session();

        Assert.False(await session.SendDeviceCommandAsync(new DeviceId(Guid.NewGuid()), DeviceCommand.Rename, "Tisch", Cancellation));
    }

    private static PairingDirectory Directory() =>
        new(
            Options.Create(new HubOptions { KnownDevices = [new PairedDevice(Device, "WAND-PC", PairingRole.Display, "a-token")] }),
            TimeProvider.System);

    private static SessionApi Session(PairingDirectory? directory = null, DisplayConnections? connections = null) =>
        new(
            new SceneStore(),
            new ScreenCatalog(),
            connections ?? new DisplayConnections(),
            directory ?? Directory(),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
}
