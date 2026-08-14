using DnDOverlay.Core;
using DnDOverlay.TestData;
using ImageMagick;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The two nets of Part 5, checked separately - which is the point, because together they look
/// like one and neither would be proved.
/// <para>
/// <b>The first net</b> reads the header and owns the message: a file claiming 60000x60000 is
/// turned away at 69 bytes read, with a sentence that says what is wrong.
/// <b>The second net</b> is for the header that LIED, and there the MECHANISM is the subject
/// rather than the number - so the limits are set deliberately small and a 2000x2000 image then
/// breaks them as reliably as a 60000 one breaks the real ones.
/// </para>
/// </summary>
public sealed class DecodeLimitTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;

    /// <summary>
    /// The first net, with the REAL production limits: the forged header is refused, it is
    /// refused with our own message, and nothing is unfolded.
    /// </summary>
    [Fact]
    public void TheForgedHeaderIsRefusedByOurOwnLimitWithOurOwnMessage()
    {
        var codec = new MagickCodec();
        var limits = AssetLimits.Default;

        var probe = codec.Probe(File.ReadAllBytes(_assets.Crafted.ForgedHeaderBomb));

        var rejected = Assert.Throws<ImageRejectedException>(
            () => limits.ThrowIfExceeded(probe, sourceBytes: 69));

        Assert.Equal(ImageRejection.TooLarge, rejected.Reason);

        // The message names the size and the limit. That is the whole difference between the two
        // nets: this one can say what is wrong, the library's can only say that something is.
        Assert.Contains("60000x60000", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("20000", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second net. The limits go deliberately small, and the decode of a genuine 2000x2000
    /// image is then cut off in a controlled way rather than taking the process with it - which is
    /// what "aborted" has to be told apart from "too large" for.
    /// <para>
    /// The disk cap is not decoration, and leaving it out was the mistake this test caught:
    /// measured, with memory or area capped ALONE the bomb decodes without complaint, because
    /// ImageMagick spills into its disk cache. Part 5 names disk usage among the limits for
    /// exactly this reason.
    /// </para>
    /// </summary>
    [Fact]
    public void SmallLimitsStopADecodeInsteadOfTakingTheProcessDown()
    {
        var (area, memory, disk) = (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk);

        try
        {
            // Built first, because the constructor installs the wide backstop - the small values
            // are the subject here, so they are set afterwards.
            var codec = new MagickCodec();

            ResourceLimits.Area = 64 * 64;
            ResourceLimits.Memory = 1024 * 1024;
            ResourceLimits.Disk = 0;

            var rejected = Assert.Throws<ImageRejectedException>(
                () => codec.Normalise(File.ReadAllBytes(_assets.Crafted.SmallBomb)));

            Assert.Equal(ImageRejection.Aborted, rejected.Reason);
        }
        finally
        {
            (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk) = (area, memory, disk);
        }
    }

    /// <summary>
    /// The counter-check to the counter-check, and the one that turns the test above into a
    /// measurement: with the disk left uncapped the very same bomb goes THROUGH. Without this,
    /// "the limits stopped it" would be a claim about which limit did the work, and the wrong one
    /// would look like it was doing it.
    /// </summary>
    [Fact]
    public void CappingMemoryAloneDoesNotStopIt()
    {
        var (area, memory, disk) = (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk);

        try
        {
            var codec = new MagickCodec();

            ResourceLimits.Area = 64 * 64;
            ResourceLimits.Memory = 1024 * 1024;

            var result = codec.Normalise(File.ReadAllBytes(_assets.Crafted.SmallBomb));

            Assert.Equal(2000, result.PixelWidth);
        }
        finally
        {
            (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk) = (area, memory, disk);
        }
    }

    /// <summary>
    /// The counter-check, and it counts as much as the two above: with the real limits in force an
    /// ordinary picture goes through untouched. A limit that fires in normal operation costs more
    /// than what it guards against (Part 4).
    /// </summary>
    [Fact]
    public void TheRealLimitsLetAnOrdinaryPictureThrough()
    {
        var codec = new MagickCodec();

        var result = codec.Normalise(File.ReadAllBytes(_assets.Promised["PNG"]));

        Assert.Equal("png", result.Format);
        Assert.Equal(64, result.PixelWidth);
    }
}
