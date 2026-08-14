using DnDOverlay.Core;
using DnDOverlay.TestData;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The frame count, which is the limit no size check can see: a GIF of six hundred frames sits
/// under every byte and pixel limit and decodes like six hundred pictures (Part 5).
/// <para>
/// Against the REAL codec and the REAL production limits. Until now this was checked against a
/// fake alone, which proved that the arithmetic in <see cref="AssetLimits"/> works - not that a
/// real file is counted at all.
/// </para>
/// </summary>
public sealed class FrameLimitTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;
    private readonly MagickCodec _codec = new();

    /// <summary>
    /// Counted from the HEADER, so the refusal comes before six hundred pictures are unfolded.
    /// </summary>
    [Fact]
    public void ManyFramesAreCountedAndRefusedFromTheHeader()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var probe = _codec.Probe(File.ReadAllBytes(_assets.Crafted.ManyFrames));

        Assert.Equal(600, probe.Frames);

        var rejected = Assert.Throws<ImageRejectedException>(
            () => AssetLimits.Default.ThrowIfExceeded(probe, sourceBytes: 14_000));

        Assert.Equal(ImageRejection.TooLarge, rejected.Reason);
        Assert.Contains("600 frames", rejected.Message, StringComparison.Ordinal);
        Assert.True(GC.GetTotalMemory(forceFullCollection: true) - before < 8 * 1024 * 1024);
    }

    /// <summary>
    /// The counter-check that turns the one above into a measurement: an ordinary animation of
    /// three frames goes straight through. Without it "many frames are refused" would hold even if
    /// every GIF were refused.
    /// </summary>
    [Fact]
    public void AnOrdinaryAnimationIsNotRefused()
    {
        var probe = _codec.Probe(File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")));

        Assert.Equal(3, probe.Frames);

        AssetLimits.Default.ThrowIfExceeded(probe, sourceBytes: 321);
    }

    /// <summary>
    /// The size limit of Part 5, which had no test of its own: the number that turns away a file
    /// nobody wants on the table, whatever its measurements say.
    /// </summary>
    [Fact]
    public void AFileBeyondTheByteLimitIsRefused()
    {
        var limits = new AssetLimits(MaxSourceBytes: 1024);
        var probe = _codec.Probe(File.ReadAllBytes(_assets.Promised["PNG"]));

        var rejected = Assert.Throws<ImageRejectedException>(
            () => limits.ThrowIfExceeded(probe, sourceBytes: 2048));

        Assert.Equal(ImageRejection.TooLarge, rejected.Reason);

        // And just under it goes through - or the test above would hold for any number at all.
        limits.ThrowIfExceeded(probe, sourceBytes: 1024);
    }
}
