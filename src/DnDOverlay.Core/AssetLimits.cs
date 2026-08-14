namespace DnDOverlay.Core;

/// <summary>
/// The limits that bite BEFORE decoding (Part 5). Handed in rather than hard wired, for the same
/// reason the clock and the storage paths are (rule 10): the checks that matter here are the ones
/// where a limit has to be set deliberately small, and a constant cannot be.
/// <para>
/// <b>Generous on purpose.</b> A limit that fires in normal operation costs more than what it
/// guards against - a 6000x4000 scan is an ordinary map, and refusing it would be a bug the DM
/// experiences as "the program is broken". The counter-check belongs with them: a realistic import
/// must trip none of these (Part 4, Part 11).
/// </para>
/// </summary>
/// <param name="MaxSourceBytes">
/// Part 5's proposal of 100 MB. It is a STARTING POINT and stands here as one - the plan settles
/// numbers by measuring, not by choosing (Part 10).
/// </param>
/// <param name="MaxEdge">
/// The longest side. Its job is the forged header: 60000x60000 is refused here, and it is refused
/// from the header alone, at 69 bytes read.
/// </param>
/// <param name="MaxTotalPixels">
/// Across all frames, because that is what a decode has to unfold. 120 megapixels is roughly a
/// 12000x10000 image - far above any map a DM scans, far below what takes a machine down.
/// </param>
/// <param name="MaxFrames">
/// A GIF with 2000 frames sits under every size limit and decodes like 2000 images (Part 5).
/// </param>
public sealed record AssetLimits(
    long MaxSourceBytes = 100L * 1024 * 1024,
    int MaxEdge = 20_000,
    long MaxTotalPixels = 120_000_000,
    int MaxFrames = 500)
{
    /// <summary>The values above, as the ordinary case.</summary>
    public static AssetLimits Default { get; } = new();

    /// <summary>
    /// Refuses <paramref name="probe"/> if it is beyond any of the limits, naming which one.
    /// Called with the HEADER, before a pixel is unfolded - that ordering is the whole point.
    /// </summary>
    /// <exception cref="ImageRejectedException">A limit was exceeded.</exception>
    public void ThrowIfExceeded(ImageProbe probe, long sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (sourceBytes > MaxSourceBytes)
        {
            throw new ImageRejectedException(
                ImageRejection.TooLarge,
                $"The file is {sourceBytes / (1024 * 1024)} MB, the limit is {MaxSourceBytes / (1024 * 1024)} MB.");
        }

        if (probe.PixelWidth > MaxEdge || probe.PixelHeight > MaxEdge)
        {
            throw new ImageRejectedException(
                ImageRejection.TooLarge,
                $"The image is {probe.PixelWidth}x{probe.PixelHeight}, the limit per side is {MaxEdge}.");
        }

        if (probe.TotalPixels > MaxTotalPixels)
        {
            throw new ImageRejectedException(
                ImageRejection.TooLarge,
                $"The image holds {probe.TotalPixels} pixels across {probe.Frames} frame(s), "
                + $"the limit is {MaxTotalPixels}.");
        }

        if (probe.Frames > MaxFrames)
        {
            throw new ImageRejectedException(
                ImageRejection.TooLarge,
                $"The image has {probe.Frames} frames, the limit is {MaxFrames}.");
        }
    }
}
