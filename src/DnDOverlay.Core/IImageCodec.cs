namespace DnDOverlay.Core;

/// <summary>
/// Everything the campaign needs from an image library, and the whole of what it may know about
/// one (rule 8). Campaign sees this interface; Magick.NET lives in Imaging and nowhere else, which
/// the architecture test enforces.
/// <para>
/// The order of the three is the security property, not a convenience: <see cref="Probe"/> reads
/// HEADERS and never unfolds pixels, so the limits can bite before the expensive step. A size
/// check on a finished decode is worthless - decoding IS the expensive step, and a 40 kB file
/// claiming 60000x60000 asks for 13 GB (Part 5).
/// </para>
/// <para>
/// The token container is NOT in here. A <c>.rptok</c> is a container, not an image format, so its
/// unpacking sits IN FRONT of the codec rather than inside it - it needs no image library at all,
/// only a zip reader and an XML reader (Part 5).
/// </para>
/// </summary>
public interface IImageCodec
{
    /// <summary>
    /// Reads format, measurements and frame count from the header alone. Nothing is decoded.
    /// </summary>
    /// <exception cref="ImageRejectedException">The bytes are not a readable image.</exception>
    ImageProbe Probe(ReadOnlyMemory<byte> source);

    /// <summary>
    /// Decodes and normalises to a delivery format - PNG for anything still, animated GIF for
    /// anything moving, and JPEG stays JPEG when the source was JPEG without transparency
    /// (Part 5). Metadata is stripped either way, deterministically and without timestamps, so
    /// that no GPS trail from a holiday photo travels to the table.
    /// </summary>
    /// <exception cref="ImageRejectedException">
    /// The format is refused, the file is broken, or a limit bit during decoding.
    /// </exception>
    NormalisedImage Normalise(ReadOnlyMemory<byte> source);

    /// <summary>
    /// A PNG thumbnail of the given width, made once and kept on disk. Regenerating these when a
    /// campaign opens would be the most noticeable mistake in this area at a thousand entries
    /// (Part 3).
    /// </summary>
    /// <exception cref="ImageRejectedException">The bytes cannot be decoded.</exception>
    byte[] Thumbnail(ReadOnlyMemory<byte> delivered, int width);
}

/// <summary>What the header says, before anything is decoded.</summary>
/// <param name="Format">The source format's name, for the report - not for the decision.</param>
/// <param name="Frames">
/// Counted, because a GIF with 2000 frames sits under every size limit and decodes like 2000
/// images (Part 5).
/// </param>
public sealed record ImageProbe(string Format, int PixelWidth, int PixelHeight, int Frames)
{
    /// <summary>Total pixels across all frames - what a decode actually has to unfold.</summary>
    public long TotalPixels => (long)PixelWidth * PixelHeight * Math.Max(1, Frames);
}

/// <summary>The result of normalising: the bytes that will be stored and delivered.</summary>
/// <param name="Standing">
/// Whether the SOURCE format was one we promise or one we merely tolerate. It travels with the
/// result because the collected import report names it ("1 x JPEG XL - worked, is not assured"),
/// and because nothing later can tell: what is in the stock afterwards is PNG or GIF either way
/// (Part 5).
/// </param>
public sealed record NormalisedImage(
    byte[] Bytes,
    string Format,
    int PixelWidth,
    int PixelHeight,
    bool IsAnimated,
    string SourceFormat,
    FormatStanding Standing);

/// <summary>
/// Two of the three outcomes of Part 5. The third, REJECTED, is not a value here - it never
/// produces an image, so it travels as <see cref="ImageRejectedException"/> instead.
/// </summary>
public enum FormatStanding
{
    /// <summary>On the promised list, printed in the README, checked by the format parcours.</summary>
    Promised,

    /// <summary>
    /// Neither promised nor locked out, so it was tried and worked. Reported as "not assured" and
    /// asserted nowhere - which is why two platforms tolerating different formats is the normal
    /// case rather than a finding (Part 5).
    /// </summary>
    Tolerated,
}

/// <summary>Why an image was refused. Every one of them is stated, never a silent swallow.</summary>
public enum ImageRejection
{
    /// <summary>Not an image at all, or an image this build cannot read.</summary>
    Unreadable,

    /// <summary>
    /// Refused although it may well work. Today that is HEIC/HEIF, and for legal reasons rather
    /// than technical ones - so it must be its OWN entry: without one it would come in with the
    /// tolerated formats the moment a build can read it, and the patent exclusion would be lifted
    /// by accident (Part 5).
    /// </summary>
    NotPermitted,

    /// <summary>Beyond a limit - bytes, measurements or frame count (Part 5).</summary>
    TooLarge,

    /// <summary>
    /// The decode was cut off by the resource limits. Separate from <see cref="TooLarge"/> on
    /// purpose: this is the second net, the one that catches a header which LIED.
    /// </summary>
    Aborted,

    /// <summary>
    /// There is no room on the campaign's drive (Part 5).
    /// <para>
    /// Its own entry rather than a shade of <see cref="TooLarge"/>, because it says nothing about
    /// the picture: an evening of large maps simply adds up. The DM's answer is the cleanup view,
    /// not a smaller file - and folded into "too large" that is exactly the answer they would not
    /// get.
    /// </para>
    /// </summary>
    NoSpace,
}

/// <summary>
/// A refusal with a reason, in a shape the collected report can group by.
/// <para>
/// An exception rather than a result value, and the reason is the implementation: the image
/// library throws for every one of these cases anyway, so a result type would mean catching and
/// re-wrapping at every call. The store turns it back into a value the moment the report is built
/// - that is where "one collected message rather than 200 dialogs" is owed (Part 7).
/// </para>
/// </summary>
public sealed class ImageRejectedException : Exception
{
    public ImageRejectedException(ImageRejection reason, string message)
        : base(message) => Reason = reason;

    public ImageRejectedException(ImageRejection reason, string message, Exception inner)
        : base(message, inner) => Reason = reason;

    public ImageRejectedException()
    {
    }

    public ImageRejectedException(string message)
        : base(message)
    {
    }

    public ImageRejectedException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Which of the stated reasons applies.</summary>
    public ImageRejection Reason { get; } = ImageRejection.Unreadable;
}
