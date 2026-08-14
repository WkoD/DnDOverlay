using System.Net.WebSockets;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// Errors travel to where the DM sits (Part 8). What only exists over a socket: that a forwarded
/// entry ends up in the control's own log with BOTH timestamps, that a device with a wrong clock
/// is said so once, and that a flood is cut off rather than swallowed.
/// </summary>
public sealed class LogForwardingOverTheWireTests
{
    private const string Token = "a-token-that-was-issued-earlier";

    private static readonly DeviceId Device = new(Guid.Parse("cccccccc-0000-0000-0000-000000000001"));

    /// <summary>
    /// The line arrives with the DEVICE's clock and ours, and the source is the device - while the
    /// entry itself is still a stable identifier plus named values, rendered here, in our language
    /// (Part 8).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_forwarded_entry_lands_in_the_controls_own_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(cancellationToken);

        var written = new DateTimeOffset(2026, 8, 12, 19, 30, 0, TimeSpan.FromHours(2));

        await display.SendAsync(
            new LogEntryMessage(
                3005,
                "AssetFailed",
                LogLevel.Warning,
                written,
                [new LogValue("AssetId", "ab12cd")],
                Screen: new ScreenId(@"\\?\DISPLAY#TEST#1")),
            cancellationToken);

        var record = await hub.WaitForAsync(entry => entry.EventId == 3005, cancellationToken);

        Assert.Equal(written, record.At);
        Assert.NotEqual(written, record.Received);
        Assert.Equal(Device, record.Source?.Device);
        Assert.Equal("TISCH-PC", record.Source?.Name);
        Assert.Equal(new ScreenId(@"\\?\DISPLAY#TEST#1"), record.Screen);
        Assert.Equal("Could not load asset ab12cd.", LogCatalog.Render(record));
    }

    /// <summary>
    /// Once per connection, and only when it is worth saying. This is the one absolute foreign
    /// clock in the protocol, and it is measured the moment it appears.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_whose_clock_is_out_is_said_so_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(cancellationToken);

        var wrong = DateTimeOffset.Now.AddHours(3);

        for (var index = 0; index < 3; index++)
        {
            await display.SendAsync(Entry(3002, "NoScreens", LogLevel.Warning, wrong), cancellationToken);
        }

        await hub.WaitForAsync(entry => entry.EventId == 1046, cancellationToken);
        await hub.WaitForAsync(entry => entry.EventId == 3002, cancellationToken);

        Assert.Single(hub.Log.Ring.Recent(200), record => record.EventId == 1046);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_clock_that_agrees_is_not_worth_a_line()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(cancellationToken);

        await display.SendAsync(Entry(3002, "NoScreens", LogLevel.Warning, DateTimeOffset.Now), cancellationToken);
        await hub.WaitForAsync(entry => entry.EventId == 3002, cancellationToken);

        Assert.DoesNotContain(hub.Log.Ring.Recent(200), record => record.EventId == 1046);
    }

    /// <summary>
    /// A paired device is trusted, not infallible: a fault or a taken-over display PC must not be
    /// able to fill the control's log. Refused and reported, never swallowed (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_flood_at_information_is_cut_off_and_reported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(cancellationToken);

        for (var index = 0; index < 80; index++)
        {
            await display.SendAsync(
                Entry(3006, "AssetDecoded", LogLevel.Information, DateTimeOffset.Now),
                cancellationToken);
        }

        await hub.WaitForAsync(entry => entry.EventId == 1047, cancellationToken);

        var arrived = hub.Log.Ring.Recent(500).Count(record => record.EventId == 3006);

        // Two windows' worth at the Information rate is the generous bound: what is being proven
        // is that the limit bites at all, not where a second boundary happened to fall.
        Assert.True(arrived <= 40, $"{arrived} entries got through, the limit is 20 a second");
    }

    /// <summary>
    /// Hardening, not tidiness (Part 4). The text was cleaned where it was written - but that was
    /// on another machine, so it is cleaned again on the way in.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_crafted_entry_cannot_write_a_line_of_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(cancellationToken);

        await display.SendAsync(
            new LogEntryMessage(
                3002,
                "NoScreens",
                LogLevel.Warning,
                DateTimeOffset.Now,
                [new LogValue("Name", "TISCH-PC\r\n# DnDOverlay.Control 9.9.9")],
                RawText: "first\nsecond"),
            cancellationToken);

        var record = await hub.WaitForAsync(entry => entry.EventId == 3002, cancellationToken);

        Assert.Equal("TISCH-PC # DnDOverlay.Control 9.9.9", record.Values.Single().Text);
        Assert.Equal("first second", record.RawText);
    }

    private static LogEntryMessage Entry(int id, string name, LogLevel level, DateTimeOffset at) =>
        new(id, name, level, at, []);

    /// <summary>One hub with its own port and its own process log, started per test.</summary>
    private sealed class Hub : IAsyncDisposable
    {
        private WebApplication _app = null!;

        internal ProcessLog Log { get; private set; } = null!;

        private Uri Address { get; set; } = null!;

        internal static async Task<Hub> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<IAssetSource>(new NoAssets());

            // In memory, with no directory: what is being proven here is the relay, and the file
            // has its own tests next door.
            var log = new ProcessLog(
                new LogIdentity("DnDOverlay.Control", "0.1.0-test", Protocol.Version),
                directory: null,
                LogFileLimits.Control,
                TimeProvider.System);

            // Registered TWICE over, and both are needed: as a provider so the hub's own lines -
            // the clock notice, the rate limit - go into it, and as a service so the relay can put
            // forwarded ones there as well. That is exactly how the control wires it up.
            builder.Logging.AddProvider(log);
            builder.Logging.SetMinimumLevel(LogLevel.Trace);

            builder.Services.AddSingleton(log);
            builder.Services.AddDnDOverlayHub(hub => hub.KnownDevices =
            [
                new PairedDevice(Device, "TISCH-PC", PairingRole.Display, Token),
            ]);

            var app = builder.Build();
            app.UseWebSockets();
            app.MapDnDOverlayHub();

            await app.StartAsync(cancellationToken);

            var address = new Uri(app.Urls.First());

            return new Hub
            {
                _app = app,
                Log = log,
                Address = new Uri($"ws://{address.Authority}{Protocol.DisplayPath}"),
            };
        }

        internal async Task<Display> ConnectAsync(CancellationToken cancellationToken) =>
            await Display.ConnectAsync(Address, cancellationToken);

        /// <summary>
        /// Waits for an entry to turn up in the log. Polled rather than awaited on an event: what
        /// is being tested is that it ARRIVES, and a test that hooked into the mechanism would be
        /// testing its own hook.
        /// </summary>
        internal async Task<LogRecord> WaitForAsync(Func<LogRecord, bool> wanted, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 250; attempt++)
            {
                if (Log.Ring.Recent(500).FirstOrDefault(wanted) is { } found)
                {
                    return found;
                }

                await Task.Delay(20, cancellationToken);
            }

            throw new TimeoutException("The entry never arrived in the control's log.");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();

            Log.Dispose();
        }
    }

    /// <summary>A paired display that says Hello and then forwards log entries.</summary>
    private sealed class Display : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();

        internal static async Task<Display> ConnectAsync(Uri address, CancellationToken cancellationToken)
        {
            var display = new Display();

            await display._socket.ConnectAsync(address, cancellationToken);
            await display.SendAsync(
                new HelloMessage(
                    Device,
                    "TISCH-PC",
                    "1.0.0",
                    Protocol.Version,
                    [new ScreenInfo(
                        new ScreenId(@"\\?\DISPLAY#TEST#1"),
                        "TISCH-PC//DISPLAY1",
                        null,
                        new PixelSize(1920, 1080),
                        96,
                        true)],
                    Token),
                cancellationToken);

            return display;
        }

        internal async Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken) =>
            await _socket.SendAsync(
                ProtocolJson.Serialise(message),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Already gone; nothing to close politely.
            }

            _socket.Dispose();
        }
    }

    private sealed class NoAssets : IAssetSource
    {
        public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType) =>
            TryOpen(id, out data, out contentType);

        public bool TryOpen(AssetId id, out Stream data, out string contentType)
        {
            data = Stream.Null;
            contentType = string.Empty;

            return false;
        }
    }
}
