using System.Text;
using System.Text.Json;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// The stock of one campaign (Part 11). The subject here is identity, deduplication, atomic
/// writing and the inventory - everything the store owes regardless of what an encoder does, which
/// is why it runs against a fake codec.
/// </summary>
public sealed class AssetStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-store-" + Guid.NewGuid().ToString("N"));

    private readonly FakeImageCodec _codec = new();
    private readonly FakeTimeProvider _time = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The identity hashes the SOURCE, not the result - and this is the case a real encoder cannot
    /// stage: the fake's output deliberately differs between runs, exactly as an encoder update
    /// would. The AssetId has to stay put across that, and the ContentHash has to move (Part 5).
    /// </summary>
    [Fact]
    public async Task TheSameSourceKeepsItsIdentityWhileTheDeliveredBytesMayChange()
    {
        var first = await Taken(Open(), "map.png", "Dorfkarte");

        // A second campaign folder with a DIFFERENT codec instance - the stand-in for the encoder
        // update that Part 5 names as the reason for two hashes.
        var elsewhere = _directory + "-after-the-update";
        var updated = AssetStore.Open(elsewhere, new FakeImageCodec(), _time);

        try
        {
            var other = await Taken(updated, "map.png", "Dorfkarte");

            Assert.Equal(first.Asset.AssetId, other.Asset.AssetId);
            Assert.NotEqual(first.Asset.Meta.ContentHash, other.Asset.Meta.ContentHash);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// The same bytes twice are one entry, nothing is written the second time, and - the part the
    /// DM notices - the name they gave it earlier stays (Part 7).
    /// </summary>
    [Fact]
    public async Task TheSameImageTwiceIsOneEntryAndKeepsItsName()
    {
        var store = Open();

        var first = await Taken(store, "orc.png", "Ork-Häuptling");
        var again = await Taken(store, "orc.png", "ganz anderer Vorschlag");

        Assert.Equal(first.Asset.AssetId, again.Asset.AssetId);
        Assert.True(again.AlreadyPresent);
        Assert.Equal("Ork-Häuptling", again.Asset.Name);
        Assert.Equal(1, store.Count);
        Assert.Equal(1, _codec.Normalisations);
    }

    /// <summary>
    /// Two different images proposing the same name: the second is numbered, and no question
    /// interrupts - an import of two hundred files must not stop to ask (Part 3).
    /// </summary>
    [Fact]
    public async Task ADifferentImageOnATakenNameIsNumbered()
    {
        var store = Open();

        await Taken(store, "one.png", "Ork");
        var second = await Taken(store, "two.png", "Ork");

        Assert.Equal("Ork (2)", second.Asset.Name);
    }

    /// <summary>
    /// The limits bite on the HEADER, before anything is decoded. The forged 60000x60000 is the
    /// case they exist for, and the counter-check matters as much: the codec is never asked to
    /// normalise it (Part 5).
    /// </summary>
    [Fact]
    public async Task AForgedHeaderIsRefusedBeforeDecoding()
    {
        var store = Open();
        _codec.Claims = new ImageProbe("png", 60_000, 60_000, 1);

        var refused = Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(Bytes("bomb.png"), "Bombe", TestContext.Current.CancellationToken));

        Assert.Equal(ImageRejection.TooLarge, refused.Reason);
        Assert.Equal(0, _codec.Normalisations);
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    /// A GIF with thousands of frames sits under every size limit and decodes like thousands of
    /// images, so the frames are counted too (Part 5).
    /// </summary>
    [Fact]
    public async Task TooManyFramesAreRefused()
    {
        var store = Open();
        _codec.Claims = new ImageProbe("gif", 64, 64, 2_000);

        var refused = Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(Bytes("many.gif"), "Flackern", TestContext.Current.CancellationToken));

        Assert.Equal(ImageRejection.TooLarge, refused.Reason);
    }

    /// <summary>
    /// The counter-check to the limits, and it counts just as much: an ordinary large scan must
    /// come through. A limit that fires in normal operation costs more than what it guards
    /// against (Part 4).
    /// </summary>
    [Fact]
    public async Task AnOrdinaryLargeScanIsNotRefused()
    {
        var store = Open();
        _codec.Claims = new ImageProbe("jpeg", 6_000, 4_000, 1);

        await Taken(store, "scan.jpg", "Kartenscan");
    }

    /// <summary>A refusal states its reason and leaves nothing behind (Part 5).</summary>
    [Fact]
    public async Task ARefusedFormatIsReportedAndStoresNothing()
    {
        var store = Open();
        _codec.RefuseWith = ImageRejection.NotPermitted;

        var refused = Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(Bytes("photo.heic"), "Urlaubsbild", TestContext.Current.CancellationToken));

        Assert.Equal(ImageRejection.NotPermitted, refused.Reason);
        Assert.Equal(0, store.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "assets")));
    }

    /// <summary>
    /// A tolerated format is taken in like any other and travels as tolerated, so the collected
    /// message can say "worked, is not assured" (Part 5).
    /// </summary>
    [Fact]
    public async Task AToleratedFormatIsTakenInAndReportedAsSuch()
    {
        var store = Open();
        _codec.Standing = FormatStanding.Tolerated;

        var taken = await Taken(store, "picture.jxl", "Wirtshaus");

        Assert.Equal(FormatStanding.Tolerated, taken.Standing);
        Assert.Equal(1, store.Count);
    }

    /// <summary>
    /// Two ingests of the SAME bytes at the same time: one entry, one file, and no scratch file
    /// left behind. The last is the one worth naming - a leftover <c>.tmp</c> under a valid hash
    /// would be a half-written image that every later check believes (Part 11).
    /// </summary>
    [Fact]
    public async Task SimultaneousIngestsOfTheSameBytesLeaveOneEntryAndNoScratchFiles()
    {
        var store = Open();
        var bytes = Bytes("crowd.png");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.IngestAsync(bytes, "Menge", TestContext.Current.CancellationToken))));

        var taken = results.Select(Assert.IsType<IngestResult.Taken>).ToList();

        Assert.Single(taken.Select(result => result.Asset.AssetId.Value).Distinct(StringComparer.Ordinal));
        Assert.Equal(1, store.Count);
        Assert.Single(taken, result => !result.AlreadyPresent);

        Assert.Empty(Directory.GetFiles(_directory, "*" + AtomicFile.TemporarySuffix, SearchOption.AllDirectories));
    }

    /// <summary>
    /// A picture whose thumbnail cannot be made is <b>refused</b>, and the reason it reaches the DM
    /// is that making the thumbnail is the only place the picture is decoded at all.
    /// <para>
    /// <b>This assertion used to say the opposite</b>, and the ground under it moved rather than
    /// the opinion. It read "a failed thumbnail is a blank tile, not a lost image", which held
    /// while <c>Normalise</c> decoded and re-encoded every picture: a thumbnail failing was then a
    /// SECOND failure of something already proved to work, and refusing over a preview would have
    /// thrown away a good picture. Since M2b both JPEG and PNG hand their bytes through untouched -
    /// the PNG re-encode cost 11.6 s on a real file - so nothing else on this side unfolds the
    /// picture, and a failure here is the first and only news that it is broken.
    /// </para>
    /// <para>
    /// Refusing costs one picture at the control. Not refusing costs it at the table, on every
    /// device at once, and it was knowable here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task APictureThatCannotBeDecodedIsRefusedRatherThanStored()
    {
        var store = Open();
        _codec.ThumbnailFails = true;

        var result = await store.IngestAsync(
            Bytes("portrait.png"), "Fürst Aldric", TestContext.Current.CancellationToken);

        var refused = Assert.IsType<IngestResult.Refused>(result);
        Assert.False(string.IsNullOrWhiteSpace(refused.Detail), "the refusal carries no reason");

        // Nothing was left behind: not in the inventory, and not on disk either. The refusal
        // happens BEFORE the picture is written, which is what keeps the folder free of files no
        // entry points at.
        Assert.Equal(0, store.Count);
        Assert.Empty(Directory.GetFiles(_directory, "*.png", SearchOption.AllDirectories));
    }

    /// <summary>
    /// The hub takes this identifier from a paired device. Without the check,
    /// <c>GET /assets/..%5C..%5Cwindows%5C…</c> reads arbitrary files off the DM's machine
    /// (Part 4, Part 5).
    /// </summary>
    [Theory]
    [InlineData("../../windows/win.ini")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    [InlineData("short")]
    [InlineData("")]
    public void AMalformedIdentifierResolvesToNothing(string value)
    {
        var store = Open();

        Assert.False(store.TryOpen(new AssetId(value), out var data, out _));
        data.Dispose();
    }

    /// <summary>
    /// The stock is not saved, it simply is there - so a reopened folder has to hold everything,
    /// names and times included (Part 3).
    /// </summary>
    [Fact]
    public async Task TheStockSurvivesBeingReopened()
    {
        var first = Open();
        var taken = await Taken(first, "keep.png", "Dorfkarte");

        var reopened = Open();

        Assert.Equal(1, reopened.Count);
        Assert.True(reopened.TryOpen(taken.Asset.AssetId, out var data, out var contentType));
        data.Dispose();
        Assert.Equal("image/png", contentType);
        Assert.Equal("Dorfkarte", reopened.Entries[0].Name);
    }

    /// <summary>
    /// Written timestamps, never the file system's. The campaign folder is the exchange format,
    /// and copying it resets file times - on every entry at once (Part 3).
    /// </summary>
    [Fact]
    public async Task TimesComeFromTheDocumentRatherThanTheFileSystem()
    {
        var moment = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
        _time.Now = moment;

        var store = Open();
        await Taken(store, "clock.png", "Turmuhr");

        // Every file in the folder gets a different time, as a copy would give them.
        foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(3));
        }

        var reopened = Open();

        Assert.Equal(moment, reopened.Entries[0].AddedAt);
        Assert.Equal(moment, reopened.CreatedAt);
    }

    /// <summary>
    /// A newer schema is refused with a reason rather than replaced. This is the opposite of the
    /// configuration cluster, and deliberately so: there a display PC must never have the one
    /// outcome of failing to start, here the folder holds work (Part 3, Part 6, Part 11).
    /// </summary>
    [Fact]
    public async Task ANewerSchemaIsRefusedAndNothingIsTouched()
    {
        var store = Open();
        await Taken(store, "old.png", "Alt");

        var path = Path.Combine(_directory, "inventory.json");
        var document = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        var raised = document.GetRawText().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal);
        File.WriteAllText(path, raised);

        var before = File.ReadAllBytes(path);

        Assert.Throws<CampaignSchemaException>(() => Open());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private AssetStore Open() => AssetStore.Open(_directory, _codec, _time);

    private static ReadOnlyMemory<byte> Bytes(string seed) => Encoding.UTF8.GetBytes("source:" + seed);

    private static async Task<IngestResult.Taken> Taken(AssetStore store, string seed, string name) =>
        Assert.IsType<IngestResult.Taken>(await store.IngestAsync(Bytes(seed), name, TestContext.Current.CancellationToken));
}

/// <summary>A clock that stands still until a test moves it (rule 10).</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
