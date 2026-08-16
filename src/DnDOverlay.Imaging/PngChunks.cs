using System.Buffers.Binary;

namespace DnDOverlay.Imaging;

/// <summary>
/// Cutting metadata out of a PNG without touching the picture, the same trade
/// <see cref="JpegSegments"/> makes and for a different reason.
/// <para>
/// For JPEG the reason was quality: re-encoding costs a generation. For PNG it is TIME. Measured
/// with the real files at a hand run of M2b: a 24 MB PNG at 4616×6000 spends <b>11.6 s</b> being
/// decoded and re-encoded, and the result comes out LARGER than the source (25.5 MB). What that
/// bought was the removal of profiles and timestamps - which is a byte operation costing
/// milliseconds. PNG is already one of the two output formats, so passing it through does not widen
/// what leaves here by one format (Part 5).
/// </para>
/// <para>
/// A PNG is a signature and then a chain of chunks: length, type, data, CRC. Whole chunks are
/// dropped by not copying them; the CRCs of the kept ones stay valid because they are copied
/// verbatim, and the image data is never looked at.
/// </para>
/// </summary>
internal static class PngChunks
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The ancillary chunks that are kept although they are not needed to decode. Every one of them
    /// changes how the picture LOOKS, and dropping it would be a quiet damage rather than a privacy
    /// gain - the same trap <c>APP2</c> and <c>APP14</c> are in the JPEG path.
    /// <list type="bullet">
    /// <item><c>tRNS</c> - transparency of a palette image. Without it a cut-out token gets a
    /// background.</item>
    /// <item><c>gAMA</c>, <c>cHRM</c>, <c>sRGB</c>, <c>iCCP</c>, <c>sBIT</c> - colour rendering.</item>
    /// <item><c>acTL</c>, <c>fcTL</c>, <c>fdAT</c> - the frames of an APNG. They cannot arrive here
    /// today, because more than one frame takes the moving path, but half-stripping an animation
    /// into something broken is not a failure worth leaving available.</item>
    /// </list>
    /// </summary>
    private static readonly string[] KeptAncillary =
        ["tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "acTL", "fcTL", "fdAT"];

    /// <summary>
    /// Removes the chunks that carry metadata and keeps the ones that carry meaning.
    /// <para>
    /// <b>The rule is the other way round from the JPEG path, and deliberately so.</b> There, three
    /// named segments are dropped and everything else is kept; here everything the format calls
    /// CRITICAL is kept, plus a named list of ancillary chunks, and <b>anything else is dropped</b>.
    /// PNG says which chunks are critical in the type itself - a lower-case first letter means
    /// ancillary - so a chunk nobody here has heard of can be dropped without guessing, which is
    /// what a privacy promise needs. A list of enemies would let the next metadata chunk in by
    /// simply being new.
    /// </para>
    /// <para>
    /// <b>Dropped in particular:</b> <c>eXIf</c>, where the GPS trail of a holiday photo lives;
    /// <c>tEXt</c>, <c>zTXt</c> and <c>iTXt</c>, which carry comments, the writing software and XMP;
    /// and <c>tIME</c>, the timestamp that also makes the output differ on every write.
    /// </para>
    /// </summary>
    internal static byte[] StripMetadata(ReadOnlySpan<byte> png)
    {
        if (!LooksLikePng(png))
        {
            return png.ToArray();
        }

        var output = new MemoryStream(png.Length);
        output.Write(png[..Signature.Length]);

        var position = Signature.Length;

        // 4 length + 4 type + 4 CRC is the smallest a chunk can be.
        while (position + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(position, 4));

            // The length is a promise about the rest of the file, and a file may lie about it. The
            // arithmetic is done in long so that a length near uint.MaxValue cannot wrap into a
            // small number and make the bound check pass.
            var total = 12L + length;

            if (position + total > png.Length)
            {
                // Malformed beyond this point. What was read is sound and the rest is copied as it
                // is - the codec, not this, decides whether the file is usable.
                output.Write(png[position..]);

                return output.ToArray();
            }

            var type = Type(png.Slice(position + 4, 4));

            if (Keep(type))
            {
                output.Write(png.Slice(position, (int)total));
            }

            position += (int)total;

            if (type == "IEND")
            {
                // Everything after the end marker is not part of the picture. Copying it would carry
                // over whatever a tool appended there, which is exactly the sort of thing this
                // method exists to leave behind.
                return output.ToArray();
            }
        }

        if (position < png.Length)
        {
            output.Write(png[position..]);
        }

        return output.ToArray();
    }

    /// <summary>Whether these bytes start like a PNG at all.</summary>
    internal static bool LooksLikePng(ReadOnlySpan<byte> data) =>
        data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Critical chunks are kept because the picture cannot be read without them, and PNG marks them
    /// in the type itself: bit 5 of the first byte clear - an upper-case letter - means critical.
    /// </summary>
    private static bool Keep(string type) =>
        (type.Length == 4 && char.IsUpper(type[0])) || KeptAncillary.Contains(type, StringComparer.Ordinal);

    private static string Type(ReadOnlySpan<byte> bytes)
    {
        Span<char> type = stackalloc char[4];

        for (var i = 0; i < 4; i++)
        {
            type[i] = (char)bytes[i];
        }

        return new string(type);
    }
}
