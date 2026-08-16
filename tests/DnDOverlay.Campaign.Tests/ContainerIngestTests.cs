using DnDOverlay.Core;
using DnDOverlay.Imaging;
using DnDOverlay.TestData;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// A container arriving at the stock the way it arrives from an entrance: as bytes, with a file
/// name, and nothing that says "this is a token".
/// <para>
/// <b>The seam this closes had no test and no builder.</b> <c>TokenContainer</c> was built and
/// proved in M2a - and nothing called it. Store, unpacker and interface all existed, and a dropped
/// <c>.rptok</c> would have landed in the stock as a ZIP. It is the same shape this project has
/// already paid for once, when a client and a hub were each proved against their own stand-in and
/// neither ever sent a token.
/// </para>
/// </summary>
public sealed class ContainerIngestTests(TestDataFixture fixture) : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-container-" + Guid.NewGuid().ToString("N"));

    private readonly TestAssetSet _assets = fixture.Assets;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The whole promise of this route in one sentence: what the DM dropped was a file called
    /// <c>token-with-portrait.rptok</c>, and what stands in the stock is "Testfigur" at 400x600 -
    /// the PORTRAIT, not the 100x100 map token, and not the archive.
    /// <para>
    /// The proposed name here is the file name, which is what an entrance would derive (stage 2).
    /// That it loses is stage 1 doing its work at the only place that can see it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADroppedTokenBecomesItsPortraitUnderTheNameItCarried()
    {
        var store = Open();
        var bytes = await File.ReadAllBytesAsync(
            _assets.Tokens.WithPortrait, TestContext.Current.CancellationToken);

        var result = await store.IngestAsync(bytes, "token-with-portrait", TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(result);

        Assert.Equal("Testfigur", taken.Asset.Name);
        Assert.Equal(400, taken.Asset.Meta.PixelWidth);
        Assert.Equal(600, taken.Asset.Meta.PixelHeight);
    }

    /// <summary>
    /// The identity is the extracted picture's, and this is the test that says so out loud: the
    /// same portrait once as a token and once as a plain file is ONE entry (Part 5). Hashing the
    /// container instead would look right in every other test and quietly double the stock.
    /// </summary>
    [Fact]
    public async Task TheSamePictureAsATokenAndAsAFileIsOneEntry()
    {
        var store = Open();

        var token = await File.ReadAllBytesAsync(
            _assets.Tokens.WithPortrait, TestContext.Current.CancellationToken);

        var fromToken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(token, "token", TestContext.Current.CancellationToken));

        // The portrait as it lies inside the container, taken in as an ordinary file would be.
        var portrait = Portrait(token);

        var fromFile = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(portrait, "Portrait", TestContext.Current.CancellationToken));

        Assert.Equal(fromToken.Asset.AssetId, fromFile.Asset.AssetId);
        Assert.True(fromFile.AlreadyPresent, "the second ingest wrote a second entry");
        Assert.Equal("Testfigur", fromFile.Asset.Name);
        Assert.Equal(1, store.Count);
    }

    /// <summary>
    /// Without a portrait the map token stands in - a top-down symbol is rarely pretty as an NPC
    /// picture, but the DM sees what they got and can delete it. Told apart by measurements, never
    /// by a hash or a MapTool version (Part 5).
    /// </summary>
    [Fact]
    public async Task WithoutAPortraitTheMapTokenStandsIn()
    {
        var store = Open();
        var bytes = await File.ReadAllBytesAsync(
            _assets.Tokens.WithoutPortrait, TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(bytes, "token", TestContext.Current.CancellationToken));

        Assert.Equal(100, taken.Asset.Meta.PixelWidth);
        Assert.Equal(100, taken.Asset.Meta.PixelHeight);
    }

    /// <summary>
    /// A container holding nothing usable is refused with a reason and reaches the collected report
    /// as a refusal - not as an exception out of the middle of a two hundred file import.
    /// </summary>
    [Fact]
    public async Task ATokenWithoutAPictureIsRefusedWithAReason()
    {
        var store = Open();
        var bytes = await File.ReadAllBytesAsync(
            _assets.Tokens.WithoutImage, TestContext.Current.CancellationToken);

        var refused = Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(bytes, "token", TestContext.Current.CancellationToken));

        Assert.NotEmpty(refused.Detail);
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    /// Recognised on content, so a token under a <c>.zip</c> name comes in - and the counter-check
    /// matters as much: an ordinary archive under a <c>.rptok</c> name does not.
    /// </summary>
    [Fact]
    public async Task TheExtensionDecidesNothingInEitherDirection()
    {
        var store = Open();

        var renamed = await File.ReadAllBytesAsync(
            _assets.Tokens.Renamed, TestContext.Current.CancellationToken);
        var foreign = await File.ReadAllBytesAsync(
            _assets.Tokens.Foreign, TestContext.Current.CancellationToken);

        Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(renamed, "irgendwas.zip", TestContext.Current.CancellationToken));

        Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(foreign, "not-a-token.rptok", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// And the counter-check to the whole route: an ordinary picture is untouched by any of this.
    /// A container check that says yes too readily would be a second decoder in front of the first.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryPictureKeepsTheNameTheEntranceProposed()
    {
        var store = Open();
        var bytes = await File.ReadAllBytesAsync(
            _assets.Promised["PNG"], TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(bytes, "Dorfkarte", TestContext.Current.CancellationToken));

        Assert.Equal("Dorfkarte", taken.Asset.Name);
    }

    /// <summary>The real codec and the real unpacker - the point of this class is that they meet.</summary>
    private AssetStore Open() =>
        AssetStore.Open(
            _directory,
            new MagickCodec(),
            TimeProvider.System,
            limits: null,
            containers: new TokenContainer());

    private static byte[] Portrait(byte[] token) => new TokenContainer().Read(token).Image;
}
