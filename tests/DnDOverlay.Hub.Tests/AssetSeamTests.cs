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

    /// <summary>A stock of exactly one picture and its thumbnail.</summary>
    private sealed class Stock : IAssetSource
    {
        internal static byte[] Picture { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

        internal static byte[] Thumbnail { get; } = [9, 9, 9];

        public bool TryOpen(AssetId id, out Stream data, out string contentType) =>
            Serve(id, Picture, out data, out contentType);

        public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType) =>
            Serve(id, Thumbnail, out data, out contentType);

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
