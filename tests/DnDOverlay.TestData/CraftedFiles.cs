using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DnDOverlay.TestData;

/// <summary>
/// The half of the stock that is written byte by byte, without Magick (Part 10). These files have
/// to be malformed, forged or hostile in ways no encoder would ever produce - an encoder is the
/// wrong tool for them, not merely a heavier one.
/// </summary>
internal static class CraftedFiles
{
    /// <summary>
    /// A PNG scanline carries one filter byte in front of the pixels; a solid grey image is
    /// therefore a run of zeroes, and that is what makes it a bomb: 2000x2000 unfolds to some four
    /// megabytes and deflates to a few kilobytes.
    /// </summary>
    private const uint BombSide = 2000;

    private const uint ForgedSide = 60000;

    internal static CraftedSet Write(string directory, string genuinePngPath)
    {
        var disguised = Path.Combine(directory, "disguised.png");

        // An MVG script under a PNG name. Two things have to hold at once: the decision falls on
        // the CONTENT rather than the extension, and the coder policy still bites when our own
        // format check is skipped entirely (Part 5, Part 11).
        File.WriteAllText(disguised, """
            push graphic-context
            viewbox 0 0 64 64
            fill 'red'
            rectangle 0,0 64,64
            pop graphic-context
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var svg = Path.Combine(directory, "external-reference.svg");
        File.WriteAllText(svg, """
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="64" height="64">
              <image xlink:href="http://example.invalid/pixel.png" x="0" y="0" width="64" height="64"/>
            </svg>
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // A genuine image under a name that lies. The counterpart to the file above: there the
        // content is dangerous, here it is harmless and only the name is wrong.
        var mislabelled = Path.Combine(directory, "mislabelled.jpg");
        File.Copy(genuinePngPath, mislabelled, overwrite: true);

        var truncated = Path.Combine(directory, "truncated.png");
        var whole = File.ReadAllBytes(genuinePngPath);
        File.WriteAllBytes(truncated, whole.AsSpan(0, whole.Length / 2).ToArray());

        // Declares itself HEIC through its ftyp brand and contains nothing else. The rejection has
        // to bite at the DECLARATION, before any decoding - which makes the stub the more precise
        // test rather than the cheaper one (Part 10).
        var heic = Path.Combine(directory, "photo.heic");
        File.WriteAllBytes(heic, [
            0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0,
            (byte)'h', (byte)'e', (byte)'i', (byte)'c', (byte)'m', (byte)'i', (byte)'f', (byte)'1',
        ]);

        var forged = Path.Combine(directory, "forged-header.png");
        File.WriteAllBytes(forged, ForgedHeader());

        var bomb = Path.Combine(directory, "small-bomb.png");
        File.WriteAllBytes(bomb, Bomb());

        return new CraftedSet(disguised, svg, mislabelled, truncated, heic, forged, bomb);
    }

    /// <summary>
    /// A PNG whose header claims 60000x60000 - the first net, and it tests the REAL production
    /// limits at 69 bytes.
    /// <para>
    /// The one <c>IDAT</c> chunk is not decoration. Measured against the plan's "and almost
    /// nothing after it": with header and end marker alone the PNG reader refuses as truncated
    /// BEFORE it reports a size, so the dimension gate is never reached and the test would be
    /// green without having measured anything. The chunk is what makes the forged size readable.
    /// </para>
    /// </summary>
    private static byte[] ForgedHeader()
    {
        var png = new MemoryStream();
        WriteSignature(png);
        WriteChunk(png, "IHDR"u8, Header(ForgedSide, ForgedSide, colourType: 6));
        WriteChunk(png, "IDAT"u8, Deflate(new byte[64]));
        WriteChunk(png, "IEND"u8, []);

        return png.ToArray();
    }

    /// <summary>
    /// A genuine, decodable 2000x2000 image of a few kilobytes - the second net, where the
    /// MECHANISM is the subject and not the number. The test sets the limits deliberately small,
    /// and this one then breaks them as reliably as a 60000 one breaks the real ones.
    /// <para>
    /// A full-size bomb exists nowhere in the automated stock: it would push some 3.6 GB through a
    /// deflate stream for a hundred-kilobyte file, on every single <c>dotnet test</c>. That the
    /// production numbers bite on real decoding as well is shown by hand, in step 14.
    /// </para>
    /// </summary>
    private static byte[] Bomb()
    {
        // One filter byte plus one grey byte per pixel, per row.
        var raw = new byte[BombSide * (BombSide + 1)];

        var png = new MemoryStream();
        WriteSignature(png);
        WriteChunk(png, "IHDR"u8, Header(BombSide, BombSide, colourType: 0));
        WriteChunk(png, "IDAT"u8, Deflate(raw));
        WriteChunk(png, "IEND"u8, []);

        return png.ToArray();
    }

    private static byte[] Header(uint width, uint height, byte colourType)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;           // bit depth
        header[9] = colourType;
        return header;
    }

    private static void WriteSignature(Stream png)
        => png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

    private static byte[] Deflate(byte[] data)
    {
        var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        png.Write(number);
        png.Write(type);
        png.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Of(type, data));
        png.Write(number);
    }

    /// <summary>
    /// PNG's own CRC-32, fifteen lines from its specification. Taking a package for it would be
    /// one dependency for one polynomial.
    /// </summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        internal static uint Of(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;

            foreach (var b in type)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            foreach (var b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];

            for (var n = 0u; n < table.Length; n++)
            {
                var c = n;

                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
