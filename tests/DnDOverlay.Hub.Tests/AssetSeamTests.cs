using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The real client against the real hub - the seam, and it is here because leaving it out cost
/// something.
/// <para>
/// The endpoint gained a token requirement in M2, <see cref="AssetClient"/> knew nothing about it,
/// and both test suites stayed green: the hub's tests fetch with a hand-built
/// <see cref="HttpClient"/>, and the client had no tests at all. Each side brought its own
/// counterpart, so neither ever met the other - and the display fetched nothing for a whole commit,
/// looking exactly like an ordinary failed download.
/// </para>
/// <para>
/// What is checked here is therefore not "does the client send a header" and not "does the hub ask
/// for one", but that the two AGREE - scheme, header name, and which role opens the stock.
/// </para>
/// </summary>
public sealed class AssetSeamTests : IAsyncLifetime
{
    private const string DisplayToken = "the-display-token";
    private const string ControlToken = "the-control-token";

    private static readonly AssetId Known = new(new string('c', AssetId.Length));

    private static readonly DeviceId Display = new(Guid.Parse("cccccccc-0000-0000-0000-000000000001"));
    private static readonly DeviceId Control = new(Guid.Parse("cccccccc-0000-0000-0000-000000000002"));

    private readonly HttpClient _http = new();
    private WebApplication _app = null!;
    private Uri _hub = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDnDOverlayHub(hub => hub.KnownDevices =
        [
            new PairedDevice(Display, "TISCH-PC", PairingRole.Display, DisplayToken),
            new PairedDevice(Control, "SURFACE", PairingRole.Control, ControlToken),
        ]);
        builder.Services.AddSingleton<IAssetSource>(new Stock());

        _app = builder.Build();
        _app.MapDnDOverlayHub();

        await _app.StartAsync();

        _hub = new Uri(_app.Urls.First());
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>The picture, fetched the way the display fetches it.</summary>
    [Fact]
    public async Task TheClientFetchesAPictureFromTheHub()
    {
        var client = new AssetClient(_http);

        var bytes = await client.GetAsync(
            _hub, Protocol.AssetPath, Known, DisplayToken, TestContext.Current.CancellationToken);

        Assert.Equal(Stock.Picture, bytes);
    }

    /// <summary>
    /// The answer says how big it is. Without that the progress ring cannot fill: the reading it
    /// draws is bytes-so-far over the total, and an answer without a length has no total.
    /// <para>
    /// Found at the M2c table, where the ring appeared and vanished without ever showing anything -
    /// which looked like a fault in the display and was a missing header in the hub. It is a seam
    /// test rather than a hub test on purpose: neither side is wrong on its own, and only the two
    /// together say whether a picture can be watched arriving (Part 7).
    /// </para>
    /// </summary>
    [Fact]
    public async Task ThePictureSaysHowBigItIs()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(_hub, $"{Protocol.AssetPath}/{Known.Value}"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", DisplayToken);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(Stock.Picture.Length, response.Content.Headers.ContentLength);
    }

    /// <summary>
    /// A download can be WATCHED: every reading carries the real total, and there is more than one
    /// of them.
    /// <para>
    /// This is the M2c finding taken apart. The ring appeared and vanished without ever filling, and
    /// the cause was two links away from where it looked: the hub declared no length, so the client
    /// reported a total of zero, so <c>AssetProgressTracker.Received</c> left the fraction where it
    /// was - deliberately, because a ring that guesses is worse than a ring that waits. Everything
    /// did exactly what it was told; nobody had told the hub to say how big the picture is.
    /// </para>
    /// <para>
    /// Both halves are asserted, and the second is the one that keeps this honest: with a picture
    /// small enough to arrive in one read the totals would be right and there would still be nothing
    /// to see.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADownloadCanBeWatchedArriving()
    {
        var readings = new List<(long Received, long Total)>();

        var bytes = await new AssetClient(_http).GetReportingAsync(
            _hub, Protocol.AssetPath, Known, DisplayToken,
            (received, total) => readings.Add((received, total)),
            TestContext.Current.CancellationToken);

        Assert.Equal(Stock.Picture.Length, bytes.Length);
        Assert.All(readings, reading => Assert.Equal(Stock.Picture.Length, reading.Total));
        Assert.True(readings.Count > 1, $"the picture arrived in {readings.Count} reading(s)");

        // And they climb, which is what a ring draws.
        Assert.Equal(readings.OrderBy(reading => reading.Received), readings);
    }

    /// <summary>And the thumbnail, which is what makes a picture stand within a second.</summary>
    [Fact]
    public async Task TheClientFetchesAThumbnailFromTheHub()
    {
        var client = new AssetClient(_http);

        var bytes = await client.GetThumbnailAsync(
            _hub, Protocol.AssetPath, Known, 256, DisplayToken, TestContext.Current.CancellationToken);

        Assert.Equal(Stock.Thumbnail, bytes);
    }

    /// <summary>
    /// The counter-check that makes the two above a measurement rather than a coincidence: with a
    /// token the hub does not accept, the same call fails. Without it, a hub that asked for
    /// nothing would pass just as well.
    /// </summary>
    [Theory]
    [InlineData("wrong-token")]
    [InlineData(ControlToken)]
    public async Task ATokenTheHubDoesNotAcceptFails(string token)
    {
        var client = new AssetClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(
                _hub, Protocol.AssetPath, Known, token, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The token a device is handed AT PAIRING opens the stock - not only one that stood in
    /// <c>control.json</c> when the hub was built.
    /// <para>
    /// Everything above starts from <see cref="HubOptions.KnownDevices"/>, which is the second
    /// night and every one after it. The first night runs the other way round: the device is
    /// unknown, the DM allows it, and the token reaches it in the <c>Welcome</c> - a path that
    /// touches <see cref="PairingDirectory.Approve"/> rather than the constructor, and that no test
    /// followed as far as an asset. It is the exact shape of the M2c hand-run, so it is measured
    /// here rather than assumed.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task ATokenIssuedAtPairingOpensTheStock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stranger = new DeviceId(Guid.Parse("cccccccc-0000-0000-0000-000000000003"));
        var issued = DeviceTokens.Create();

        var hello = new HelloMessage(
            stranger,
            "KADACHI",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(new ScreenId(@"\\?\DISPLAY#TEST#1"), "KADACHI//DISPLAY1", null, new PixelSize(2736, 1824), 144, true)],
            Token: null,
            "7547");

        // Knocking, as a Hello over the socket does it - the state the DM's list shows.
        Assert.IsType<Admission.Waiting>(
            _app.Services.GetRequiredService<PairingDirectory>().Consider(hello, "192.168.178.20"));

        // Allowed, exactly the way the pairing desk allows one.
        await _app.Services.GetRequiredService<ISessionApi>()
            .ApprovePairingAsync(stranger, issued, PairingRole.Display, cancellationToken);

        var bytes = await new AssetClient(_http)
            .GetAsync(_hub, Protocol.AssetPath, Known, issued, cancellationToken);

        Assert.Equal(Stock.Picture, bytes);
    }

    /// <summary>
    /// The whole load path against the real hub: <see cref="AssetLoader"/> with a real
    /// <see cref="AssetClient"/>, a real <see cref="AssetCache"/> and a real
    /// <see cref="AssetProgressTracker"/> - thumbnail first, original after, checked against the
    /// hash the item carries, and stored.
    /// <para>
    /// It is here rather than beside the loader's own tests for the reason this seam exists at all:
    /// the tests next door use a stand-in counterpart, and a stand-in agrees with whoever wrote it.
    /// What is proved here is that the loader and the hub agree on paths, on the header and on what
    /// the bytes are.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheWholeLoadPathWorksAgainstTheRealHub()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dndoverlay-seam-" + Guid.NewGuid().ToString("N"));

        try
        {
            var cache = new AssetCache(directory);
            var progress = new AssetProgressTracker();
            var loader = new AssetLoader(new AssetClient(_http), cache, progress);

            var meta = new AssetMeta(
                8, 8, "png", Stock.Picture.Length, IsAnimated: false,
                ContentHash: Convert.ToHexStringLower(SHA256.HashData(Stock.Picture)));

            var arrivals = Channel.CreateUnbounded<AssetArrived>();

            await loader.LoadAsync(
                _hub,
                Protocol.AssetPath,
                [new AssetWanted(Known, meta)],
                DisplayToken,
                arrivals.Writer,
                TestContext.Current.CancellationToken);

            arrivals.Writer.Complete();

            var arrived = await arrivals.Reader
                .ReadAllAsync(TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal([Stock.Thumbnail, Stock.Picture], arrived.Select(item => item.Bytes));
            Assert.Equal([true, false], arrived.Select(item => item.IsThumbnail));

            // And it is in the store afterwards, which is what makes the second time free.
            Assert.True(cache.TryGet(Known, out var held));
            Assert.Equal(Stock.Picture, held);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A stock of exactly one picture and its thumbnail.</summary>
    private sealed class Stock : IAssetSource
    {
        /// <summary>
        /// Half a megabyte rather than eight bytes, so that a download HAPPENS: the client reads in
        /// 80 kB steps, and a picture that fits in one step reports once and can never be watched
        /// arriving. The progress test below needs several readings to mean anything.
        /// </summary>
        internal static byte[] Picture { get; } = Filled(512 * 1024);

        internal static byte[] Thumbnail { get; } = [9, 9, 9];

        public bool TryOpen(AssetId id, out Stream data, out string contentType) =>
            Serve(id, Picture, out data, out contentType);

        public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType) =>
            Serve(id, Thumbnail, out data, out contentType);

        /// <summary>Deterministic, and not compressible into nothing - the bytes are the point.</summary>
        private static byte[] Filled(int count)
        {
            var bytes = new byte[count];

            for (var i = 0; i < count; i++)
            {
                bytes[i] = (byte)(i * 31 % 251);
            }

            return bytes;
        }

        private static bool Serve(AssetId id, byte[] bytes, out Stream data, out string contentType)
        {
            if (id != Known)
            {
                data = Stream.Null;
                contentType = string.Empty;
                return false;
            }

            data = new MemoryStream(bytes, writable: false);
            contentType = "image/png";

            return true;
        }
    }
}
