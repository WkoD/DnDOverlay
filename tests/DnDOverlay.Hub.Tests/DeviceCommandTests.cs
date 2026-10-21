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
    /// A device renamed while it is switched off carries the new name in the lists at once, and the
    /// device hears it once when it connects - not on every connection after.
    /// </summary>
    [Fact]
    public async Task A_rename_while_off_is_kept_for_the_next_connection_once()
    {
        var directory = Directory();

        using var session = Session(directory);

        Assert.True(await session.SendDeviceCommandAsync(Device, DeviceCommand.Rename, "Beamer", Cancellation));

        Assert.Equal("Beamer", Assert.Single(directory.Paired).Name);
        Assert.Equal("Beamer", session.TakeRename(Device));
        Assert.Null(session.TakeRename(Device));
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

    private static SessionApi Session(PairingDirectory? directory = null) =>
        new(
            new SceneStore(),
            new ScreenCatalog(),
            new DisplayConnections(),
            directory ?? Directory(),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
}
