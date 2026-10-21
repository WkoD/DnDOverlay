using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The window lists of one device on their way to the surfaces (Part 4): ten lists a second, two
/// hundred windows a list - and the wish for them kept across a reconnect like the diagnostics wish.
/// </summary>
public sealed class WindowsRelayTests
{
    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000003"));
    private static readonly ScreenId Screen = new("DISPLAY-TABLE");

    [Fact]
    public async Task What_is_over_the_rate_is_refused()
    {
        var time = new ManualTime();
        var events = new SessionEvents(time);
        var relay = new WindowsRelay(events, time, NullLogger.Instance, Device, "TISCH-PC");
        var watching = events.Open(() => Nothing);

        for (var i = 0; i < 50; i++)
        {
            relay.Take(new WindowListMessage([Window(i)]));
        }

        // The tenth got through and the eleventh did not.
        Assert.Equal(WindowsRelay.Allowed - 1, (await NewestAsync(watching)).Single().Handle);
    }

    /// <summary>A list beyond two hundred windows is cut there - a dozen visible ones is many.</summary>
    [Fact]
    public async Task A_list_is_cut_at_two_hundred_windows()
    {
        var time = new ManualTime();
        var events = new SessionEvents(time);
        var relay = new WindowsRelay(events, time, NullLogger.Instance, Device, "TISCH-PC");
        var watching = events.Open(() => Nothing);

        relay.Take(new WindowListMessage([.. Enumerable.Range(0, 300).Select(Window)]));

        Assert.Equal(WindowsRelay.MostWindows, (await NewestAsync(watching)).Count);
    }

    /// <summary>The wish is kept for a device that is not here, and a command for it is refused rather than lost silently.</summary>
    [Fact]
    public async Task The_wish_is_kept_and_a_command_without_the_device_is_refused()
    {
        using var session = new SessionApi(
            new SceneStore(),
            new ScreenCatalog(),
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);

        await session.RequestWindowsAsync(Device, wanted: true, TestContext.Current.CancellationToken);

        Assert.True(session.WindowsRequested(Device));
        Assert.False(await session.SendWindowCommandAsync(
            new ScreenRef(Device, Screen), 42, "MapTool", WindowAction.Maximize, null, TestContext.Current.CancellationToken));

        await session.RequestWindowsAsync(Device, wanted: false, TestContext.Current.CancellationToken);

        Assert.False(session.WindowsRequested(Device));
    }

    private static readonly SessionEvent Nothing = new SessionEvent.DevicesChanged([]);

    private static ForeignWindow Window(int handle) =>
        new(handle, Screen, "MapTool", new Rect(0, 0, 1, 1), handle, Minimized: false);

    /// <summary>The newest list that reached the stream - the slot keeps one.</summary>
    private static async Task<IReadOnlyList<ForeignWindow>> NewestAsync(SessionEvents.Subscription watching)
    {
        watching.Dispose();

        SessionEvent.WindowList? last = null;

        await foreach (var @event in watching.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            if (@event is SessionEvent.WindowList list)
            {
                Assert.Equal(Device, list.Device);
                last = list;
            }
        }

        Assert.NotNull(last);

        return last.Windows;
    }
}
