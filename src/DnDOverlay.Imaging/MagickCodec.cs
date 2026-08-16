using System.Collections.Frozen;
using DnDOverlay.Core;
using ImageMagick;

namespace DnDOverlay.Imaging;

/// <summary>
/// <see cref="IImageCodec"/> on Magick.NET - the only place in the program that knows an image
/// library exists (rule 8). Broad in, narrow out (Part 5).
/// </summary>
public sealed class MagickCodec : IImageCodec
{
    /// <summary>
    /// The promise, and the whole of it. It is printed in the README and checked by the format
    /// parcours against these same six names on both platforms - it is a demand on the BUILD, not
    /// an observation of one (Part 5).
    /// </summary>
    public static readonly FrozenSet<string> PromisedFormats =
        new[] { "PNG", "JPEG", "GIF", "BMP", "WEBP", "AVIF" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly AssetLimits _limits;

    /// <summary>
    /// How much wider the second net is than the first. It has to be strictly wider, and finding
    /// out why cost a test run: set to the SAME numbers, ImageMagick's limits fire first - even on
    /// a header read - and the two nets collapse into one. What the DM then sees is
    /// <c>Invalid IHDR data</c> instead of "the image is 60000x60000, the limit per side is
    /// 20000", and our own message becomes unreachable code.
    /// </summary>
    private const int BackstopFactor = 4;

    /// <summary>
    /// Builds the codec and installs the decode limits - the SECOND net, for the header that LIED
    /// (Part 5). The first net is <see cref="AssetLimits"/>, read off the header, and it owns the
    /// message.
    /// <para>
    /// Width and height are deliberately NOT set here. They would act on the DECLARED size and
    /// thereby do the first net's job with a worse message; what belongs in the backstop is what
    /// only shows during decoding - area, frame count, memory.
    /// </para>
    /// <para>
    /// These limits are process-wide state in ImageMagick, like the coder policy. One process runs
    /// one control application, so a single owner is the honest arrangement; a test that wants them
    /// small sets them itself and puts them back.
    /// </para>
    /// </summary>
    public MagickCodec(AssetLimits? limits = null)
    {
        CoderPolicy.EnsureApplied();

        _limits = limits ?? AssetLimits.Default;

        // ImageMagick counts these in BYTES, not pixels. Four bytes per pixel is the Q8 RGBA
        // worst case - the unit confusion is worth naming, because the numbers look plausible
        // either way and only one of them is a limit.
        var budget = (ulong)_limits.MaxTotalPixels * 4 * BackstopFactor;

        ResourceLimits.Area = budget;
        ResourceLimits.Memory = budget;

        // The one that makes the others bite, and leaving it out silently disables the whole net.
        // Measured: with area or memory capped ALONE, a 2000x2000 bomb decodes without complaint -
        // ImageMagick simply spills into its disk cache. Only with the disk capped as well does it
        // stop. Part 5 lists disk usage among the limits to set; skipping it turned the second net
        // into a slow decode rather than a refusal.
        ResourceLimits.Disk = budget * 2;

        ResourceLimits.ListLength = (ulong)(_limits.MaxFrames * BackstopFactor);
    }

    /// <inheritdoc />
    public ImageProbe Probe(ReadOnlyMemory<byte> source)
    {
        CoderPolicy.EnsureApplied();
        RefuseNotPermitted(source.Span);

        try
        {
            // Headers only. Nothing is unfolded, which is the entire reason the limits can bite
            // before the expensive step (Part 5).
            var frames = MagickImageInfo.ReadCollection(source.Span).ToList();

            if (frames.Count == 0)
            {
                throw new ImageRejectedException(ImageRejection.Unreadable, "The file holds no image.");
            }

            var first = frames[0];

            return new ImageProbe(
                first.Format.ToString().ToUpperInvariant(),
                (int)Math.Min(first.Width, int.MaxValue),
                (int)Math.Min(first.Height, int.MaxValue),
                frames.Count);
        }
        catch (MagickException ex)
        {
            throw Rejected(ex);
        }
    }

    /// <inheritdoc />
    public NormalisedImage Normalise(ReadOnlyMemory<byte> source)
    {
        CoderPolicy.EnsureApplied();
        RefuseNotPermitted(source.Span);

        var probe = Probe(source);
        var standing = PromisedFormats.Contains(probe.Format)
            ? FormatStanding.Promised
            : FormatStanding.Tolerated;

        try
        {
            // JPEG stays JPEG, and it is NOT re-encoded - the metadata is cut out byte-wise
            // instead. Anything else would cost a generation of quality for a privacy fix
            // (Part 5).
            if (IsJpegWithoutTransparency(source.Span, probe))
            {
                var stripped = JpegSegments.StripMetadata(source.Span);

                return new NormalisedImage(
                    stripped, "jpg", probe.PixelWidth, probe.PixelHeight, false, probe.Format, standing);
            }

            // PNG stays PNG the same way, and the reason is TIME rather than quality. Measured with
            // the real files (hand-run of M2b): a 24 MB PNG at 4616×6000 cost 11.6 s to decode and
            // re-encode, and came out LARGER than the source. The DM waited those seconds before
            // the picture existed at all.
            //
            // Nothing about the output contract moves: PNG is already one of the two formats that
            // leave here, so this changes whether we re-encode and not what we produce (Part 5).
            // What it does change is that odd-but-valid PNGs - 16 bit, interlaced, palette with
            // transparency - now reach the display's decoder as they were written instead of
            // flattened, which is why the codec-to-WIC seam test carries them.
            if (IsStillPng(source.Span, probe))
            {
                var stripped = PngChunks.StripMetadata(source.Span);

                return new NormalisedImage(
                    stripped, "png", probe.PixelWidth, probe.PixelHeight, false, probe.Format, standing);
            }

            return IsMoving(probe)
                ? Moving(source, probe, standing)
                : Still(source, probe, standing);
        }
        catch (MagickException ex)
        {
            throw Rejected(ex);
        }
    }

    /// <inheritdoc />
    public byte[] Thumbnail(ReadOnlyMemory<byte> delivered, int width)
    {
        CoderPolicy.EnsureApplied();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        try
        {
            using var image = new MagickImage(delivered.Span);

            // A thumbnail of a moving image is its first frame - a preview that plays would be
            // a second animation budget nobody asked for (Part 7).
            image.Resize(new MagickGeometry((uint)width, 0) { Greater = true });
            Deterministic(image);

            return image.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException ex)
        {
            throw Rejected(ex);
        }
    }

    /// <summary>Anything still becomes PNG - lossless, with the alpha channel intact.</summary>
    private static NormalisedImage Still(ReadOnlyMemory<byte> source, ImageProbe probe, FormatStanding standing)
    {
        using var image = new MagickImage(source.Span);
        Deterministic(image);

        return new NormalisedImage(
            image.ToByteArray(MagickFormat.Png),
            "png",
            (int)image.Width,
            (int)image.Height,
            false,
            probe.Format,
            standing);
    }

    /// <summary>
    /// Anything moving becomes animated GIF. The price is named and accepted: 256 colours per
    /// frame, in exchange for something that plays everywhere (Part 5).
    /// </summary>
    private static NormalisedImage Moving(ReadOnlyMemory<byte> source, ImageProbe probe, FormatStanding standing)
    {
        using var frames = new MagickImageCollection(source.Span);

        // Coalesce first: the frames of a WebP or GIF may be partial rectangles that only mean
        // anything on top of their predecessor. Converting them one by one would produce the
        // torn-looking animation this is a classic mistake for.
        frames.Coalesce();

        foreach (var frame in frames)
        {
            Deterministic(frame);
        }

        var bytes = frames.ToByteArray(MagickFormat.Gif);

        return new NormalisedImage(
            bytes,
            "gif",
            (int)frames[0].Width,
            (int)frames[0].Height,
            true,
            probe.Format,
            standing);
    }

    /// <summary>
    /// Strips profiles, comments and the timestamps ImageMagick writes by default. Deterministic
    /// output is not what identity rests on - the <see cref="AssetId"/> hashes the SOURCE for
    /// exactly that reason (Part 5) - but a file that differs on every write for no reason would
    /// defeat every later comparison a human makes.
    /// </summary>
    private static void Deterministic(IMagickImage<byte> image)
    {
        image.Strip();
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time,tEXt");
    }

    /// <summary>
    /// HEIC and HEIF, refused at the DECLARATION and before any decoding. It is the one place a
    /// format is turned away although it works perfectly - the reason is the HEVC patent situation
    /// and not a technical one, so it has to be its own entry with its own message. Without one it
    /// would come in with the tolerated formats the moment a build can read it, and the exclusion
    /// would be lifted by accident (Part 5).
    /// <para>
    /// Read from the <c>ftyp</c> brand rather than by asking the decoder, which also makes the
    /// refusal cheap: a stub that merely CLAIMS to be HEIC is turned away just as surely.
    /// </para>
    /// </summary>
    private static void RefuseNotPermitted(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12 || !source.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return;
        }

        var brand = source.Slice(8, 4);

        if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8)
            || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8)
            || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
        {
            throw new ImageRejectedException(
                ImageRejection.NotPermitted,
                "HEIC/HEIF is not accepted. The reason is the HEVC patent situation rather than a "
                + "technical one - save the picture as JPEG or PNG and it comes straight in. AVIF "
                + "is unaffected and is supported.");
        }
    }

    /// <summary>
    /// The formats where more than one frame means MOVEMENT. The distinction is not pedantry, it
    /// was measured: a layered PSD reports its layers as frames and came out as an animated GIF -
    /// a flat map turned into a flickering one. TIFF pages, ICO sizes and PDF pages are the same
    /// shape of mistake.
    /// <para>
    /// Multi-PAGE sources take the still path, where the library hands back the merged image -
    /// which is what somebody dropping a layered file onto the table means by it.
    /// </para>
    /// </summary>
    private static readonly FrozenSet<string> AnimationFormats =
        new[] { "GIF", "GIF87", "WEBP", "APNG", "MNG", "AVIF" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsMoving(ImageProbe probe) =>
        probe.Frames > 1 && AnimationFormats.Contains(probe.Format);

    /// <summary>
    /// A PNG that does not move, and therefore one whose bytes can travel on unchanged.
    /// <para>
    /// The single-frame condition is what keeps an APNG out. More than one frame takes the moving
    /// path and becomes GIF; should a build ever report an animated PNG as plain <c>PNG</c> with
    /// several frames, it falls to the still path below and is FLATTENED, which is what happens
    /// today. Passing it through half-stripped would be the worse of the two.
    /// </para>
    /// </summary>
    private static bool IsStillPng(ReadOnlySpan<byte> source, ImageProbe probe) =>
        probe.Frames == 1
        && probe.Format.Equals("PNG", StringComparison.OrdinalIgnoreCase)
        && PngChunks.LooksLikePng(source);

    private static bool IsJpegWithoutTransparency(ReadOnlySpan<byte> source, ImageProbe probe) =>
        probe.Frames == 1
        && probe.Format.Equals("JPEG", StringComparison.OrdinalIgnoreCase)
        && JpegSegments.LooksLikeJpeg(source);

    /// <summary>
    /// Turns the library's exception into one of our stated reasons. The distinction that matters
    /// is the last one: a policy refusal is the hardening working, and it must not read as "broken
    /// file" to the DM.
    /// </summary>
    private static ImageRejectedException Rejected(MagickException ex) => ex switch
    {
        MagickPolicyErrorException => new ImageRejectedException(
            ImageRejection.NotPermitted,
            "This kind of file is not accepted - it is not an image format but a script or a "
            + "fetching coder.",
            ex),

        // Both belong to the second net. The cache one is the case that actually occurs, and it
        // took measuring to learn that: a decode stopped by the limits comes back as a CACHE
        // error, because what fails is the pixel cache failing to grow rather than a limit being
        // consulted up front.
        MagickResourceLimitErrorException or MagickCacheErrorException => new ImageRejectedException(
            ImageRejection.Aborted,
            "Decoding was stopped: the image needs more memory than is allowed. Its header may "
            + "have understated its size.",
            ex),

        MagickMissingDelegateErrorException => new ImageRejectedException(
            ImageRejection.Unreadable, "This build cannot read that format.", ex),

        _ => new ImageRejectedException(
            ImageRejection.Unreadable, "The file could not be read as an image: " + ex.Message, ex),
    };
}
