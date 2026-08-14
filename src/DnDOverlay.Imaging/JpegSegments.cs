namespace DnDOverlay.Imaging;

/// <summary>
/// Cutting metadata out of a JPEG without touching the picture. JPEG stays JPEG when the source
/// was JPEG (Part 5), so re-encoding is exactly what must not happen here - it would cost a
/// generation of quality for a privacy fix.
/// <para>
/// A JPEG is a chain of marker segments. Removing whole APPn segments is a byte operation: the
/// entropy-coded image data is never looked at, and the result differs from the source in the
/// removed segments and nowhere else.
/// </para>
/// </summary>
internal static class JpegSegments
{
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte Comment = 0xFE;

    /// <summary>
    /// Removes the segments that carry metadata and keeps the ones that carry meaning.
    /// <para>
    /// <b>Removed:</b> <c>APP1</c> - EXIF and XMP, where the GPS trail of a holiday photo lives -
    /// <c>APP13</c> (Photoshop resource blocks, which carry metadata too) and <c>COM</c>.
    /// </para>
    /// <para>
    /// <b>Kept, and each for a reason:</b> <c>APP0</c> is JFIF, which says how to read the density;
    /// <c>APP2</c> is the ICC colour profile, and dropping it changes how the picture LOOKS;
    /// <c>APP14</c> is Adobe's colour transform, without which a CMYK or YCCK JPEG comes out with
    /// inverted colours. Stripping everything blindly would be the tidier line of code and would
    /// quietly damage pictures.
    /// </para>
    /// </summary>
    internal static byte[] StripMetadata(ReadOnlySpan<byte> jpeg)
    {
        if (!LooksLikeJpeg(jpeg))
        {
            return jpeg.ToArray();
        }

        var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg[..2]);

        var position = 2;

        while (position + 4 <= jpeg.Length && jpeg[position] == Marker)
        {
            var kind = jpeg[position + 1];

            // From the scan onwards comes entropy-coded data, which has no segment structure.
            // Everything from here is copied unchanged - this is the picture.
            if (kind == StartOfScan)
            {
                output.Write(jpeg[position..]);
                return output.ToArray();
            }

            var length = (jpeg[position + 2] << 8) | jpeg[position + 3];

            if (length < 2 || position + 2 + length > jpeg.Length)
            {
                // Malformed beyond this point. What has been read is sound, the rest is copied as
                // it is - the codec, not this, decides whether the file is usable.
                output.Write(jpeg[position..]);
                return output.ToArray();
            }

            if (!IsMetadata(kind))
            {
                output.Write(jpeg.Slice(position, 2 + length));
            }

            position += 2 + length;
        }

        if (position < jpeg.Length)
        {
            output.Write(jpeg[position..]);
        }

        return output.ToArray();
    }

    /// <summary>Whether these bytes start like a JPEG at all.</summary>
    internal static bool LooksLikeJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == Marker && data[1] == StartOfImage;

    private static bool IsMetadata(byte kind) =>
        kind is 0xE1     // APP1  - EXIF, XMP
             or 0xED     // APP13 - Photoshop resource blocks
             or Comment;
}
