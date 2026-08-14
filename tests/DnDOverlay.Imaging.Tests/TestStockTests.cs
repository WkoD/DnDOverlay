using DnDOverlay.TestData;
using ImageMagick;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The stock checking itself. It is the capability probe for this Magick build (Part 10), so what
/// it produced is a finding about the build - and the two crafted bombs carry measured properties
/// that a later change could quietly break.
/// </summary>
public sealed class TestStockTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;

    /// <summary>
    /// Animation belongs to the promise, and a still GIF would prove none of it: frames and frame
    /// times both have to survive the round trip.
    /// </summary>
    [Theory]
    [InlineData("animated.gif")]
    [InlineData("animated.webp")]
    public void AnimatedSourcesKeepTheirFramesAndFrameTimes(string fileName)
    {
        using var frames = new MagickImageCollection(Path.Combine(_assets.Directory, fileName));

        Assert.Equal(3, frames.Count);
        Assert.Equal([10u, 20u, 30u], frames.Select(frame => frame.AnimationDelay));
    }

    /// <summary>
    /// The first net, and the reason the forged file carries an <c>IDAT</c> chunk: the size has to
    /// be READABLE from the header. With header and end marker alone the reader refuses as
    /// truncated before it ever reports a size - the dimension gate would then never be reached,
    /// and a test asserting "rejected" would be green without having measured anything.
    /// </summary>
    [Fact]
    public void TheForgedHeaderReportsItsForgedSizeWithoutDecoding()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var info = new MagickImageInfo(_assets.Crafted.ForgedHeaderBomb);

        Assert.Equal(MagickFormat.Png, info.Format);
        Assert.Equal(60000u, info.Width);
        Assert.Equal(60000u, info.Height);

        // 60000x60000 would be some 13 GB unfolded. Nothing was unfolded.
        var after = GC.GetTotalMemory(forceFullCollection: true);
        Assert.True(after - before < 8 * 1024 * 1024, $"the header read grew the heap by {after - before} bytes");
    }

    /// <summary>
    /// The second net: a genuine, decodable image whose file is a fraction of what it unfolds to.
    /// Here the mechanism is the subject, not the number - which is why the limits are set small
    /// deliberately in the tests that use it.
    /// </summary>
    [Fact]
    public void TheSmallBombUnfoldsFarBeyondItsFileSize()
    {
        var onDisk = new FileInfo(_assets.Crafted.SmallBomb).Length;
        var info = new MagickImageInfo(_assets.Crafted.SmallBomb);

        Assert.Equal(2000u, info.Width);
        Assert.Equal(2000u, info.Height);
        Assert.True(onDisk < 64 * 1024, $"the bomb is {onDisk} bytes and no longer compresses like one");
    }

    /// <summary>
    /// The stub has to be rejected at its DECLARATION rather than because decoding failed, so the
    /// declaration has to be there to be read - and HEIC has to remain reachable for the coder
    /// policy, or our own legal rejection would never be the one that fires (Part 5).
    /// </summary>
    [Fact]
    public void TheHeicStubDeclaresItselfHeic()
    {
        var bytes = File.ReadAllBytes(_assets.Crafted.HeicStub);

        Assert.Equal("ftyp"u8, bytes.AsSpan(4, 4));
        Assert.Equal("heic"u8, bytes.AsSpan(8, 4));
    }

    /// <summary>
    /// A finding, not a requirement: which tolerated formats this build carries. Tolerated ones
    /// may differ per platform and nothing is asserted about them - so this test asserts only that
    /// the generator made a decision about each, rather than skipping the question.
    /// </summary>
    [Fact]
    public void EveryToleratedCandidateIsEitherWrittenOrReported()
        => Assert.Equal(2, _assets.Tolerated.Count + _assets.SkippedTolerated.Length);
}
