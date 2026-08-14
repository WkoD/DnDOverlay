using DnDOverlay.Core;
using DnDOverlay.Imaging;
using DnDOverlay.TestData;
using ImageMagick;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// The thumbnail path, end to end and against the REAL codec.
/// <para>
/// It had no test at all: what existed proved that a FAILED thumbnail does not lose the picture,
/// and nothing said one is ever made. Store, interface and route were all built, and the promise
/// they serve - a picture STANDS at its place within a second, out of the thumbnail if need be
/// (Part 10) - rested on nobody having looked.
/// </para>
/// </summary>
public sealed class ThumbnailTests(TestDataFixture fixture) : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-thumb-" + Guid.NewGuid().ToString("N"));

    private readonly TestAssetSet _assets = fixture.Assets;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// A real picture in, a real PNG thumbnail out - smaller than the original, readable as an
    /// image, and served by the same identifier.
    /// </summary>
    [Fact]
    public async Task AnIngestedPictureGetsARealThumbnail()
    {
        var store = Open();
        var source = await File.ReadAllBytesAsync(
            Path.Combine(_assets.Directory, "token-portrait.png"), TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(source, "Fürst Aldric", TestContext.Current.CancellationToken));

        Assert.True(File.Exists(store.ThumbnailPath(taken.Asset.AssetId)), "no thumbnail was written");

        Assert.True(store.TryOpenThumb(taken.Asset.AssetId, 256, out var data, out var contentType));

        using (data)
        {
            Assert.Equal("image/png", contentType);

            var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, TestContext.Current.CancellationToken);

            var thumbnail = new MagickImageInfo(buffer.ToArray());

            Assert.Equal(MagickFormat.Png, thumbnail.Format);
            Assert.Equal(256u, thumbnail.Width);

            // 400x600 in, so the height follows the aspect ratio rather than being squashed.
            Assert.Equal(384u, thumbnail.Height);
        }
    }

    /// <summary>
    /// Made once and kept. Regenerating them when a campaign opens would be the most noticeable
    /// mistake in this area at a thousand entries (Part 3), so the second ingest of the same
    /// picture must not touch the file.
    /// </summary>
    [Fact]
    public async Task AThumbnailIsMadeOnceAndKept()
    {
        var store = Open();
        var source = await File.ReadAllBytesAsync(
            Path.Combine(_assets.Directory, "alpha.png"), TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(source, "Alpha", TestContext.Current.CancellationToken));

        var path = store.ThumbnailPath(taken.Asset.AssetId);
        var written = File.GetLastWriteTimeUtc(path);

        File.SetLastWriteTimeUtc(path, written.AddDays(-1));

        await store.IngestAsync(source, "noch einmal", TestContext.Current.CancellationToken);

        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// A thumbnail of a moving picture is its first frame. A preview that plays would be a second
    /// animation budget nobody asked for (Part 6, Part 7).
    /// </summary>
    [Fact]
    public async Task TheThumbnailOfAnAnimationIsAStill()
    {
        var store = Open();
        var source = await File.ReadAllBytesAsync(
            Path.Combine(_assets.Directory, "animated.gif"), TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(source, "Fackel", TestContext.Current.CancellationToken));

        var thumbnail = await File.ReadAllBytesAsync(
            store.ThumbnailPath(taken.Asset.AssetId), TestContext.Current.CancellationToken);

        using var frames = new MagickImageCollection(thumbnail);

        Assert.Single(frames);
    }

    private AssetStore Open() => AssetStore.Open(_directory, new MagickCodec(), TimeProvider.System);
}
