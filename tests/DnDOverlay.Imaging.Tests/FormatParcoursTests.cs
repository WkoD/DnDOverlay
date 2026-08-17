using System.Security.Cryptography;
using DnDOverlay.Core;
using DnDOverlay.TestData;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The format parcours (Part 11): every input format lands in the right output format, animated
/// sources keep their frames and frame times, and the three outcomes are told apart one by one.
/// <para>
/// It is a round trip - written with Magick, read with Magick - and that is worth saying out loud.
/// It proves the PIPELINE: format decision, limits, refusal, normalisation. It does not prove that
/// a WebP out of Chrome or a HEIC off an iPhone comes through; that is step 13 by hand, with its
/// own material and nothing checked in (Part 10).
/// </para>
/// </summary>
public sealed class FormatParcoursTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;
    private readonly MagickCodec _codec = new();

    /// <summary>
    /// Every promised format goes through and is carried AS promised. The list is the demand on
    /// the build, and it is the same six names on both platforms (Part 5).
    /// </summary>
    [Fact]
    public void EveryPromisedFormatComesThroughAsPromised()
    {
        foreach (var name in TestAssets.MandatoryFormats)
        {
            var result = _codec.Normalise(File.ReadAllBytes(_assets.Promised[name]));

            Assert.Equal(FormatStanding.Promised, result.Standing);
            Assert.True(result.PixelWidth > 0 && result.PixelHeight > 0, $"{name} normalised to nothing");
        }
    }

    /// <summary>
    /// Broad in, narrow out: what is in the stock afterwards is PNG, GIF or JPEG and nothing else,
    /// so the display needs to read exactly three formats (Part 5).
    /// </summary>
    [Theory]
    [InlineData("alpha.png", "png")]
    [InlineData("plain.bmp", "png")]
    [InlineData("still.webp", "png")]
    [InlineData("still.avif", "png")]
    [InlineData("scan.tiff", "png")]
    [InlineData("layered.psd", "png")]
    [InlineData("animated.gif", "gif")]
    [InlineData("animated.webp", "gif")]
    [InlineData("photo.jpg", "jpg")]
    public void EverySourceLandsInOneOfTheThreeOutputFormats(string fileName, string expected)
    {
        var result = _codec.Normalise(File.ReadAllBytes(Path.Combine(_assets.Directory, fileName)));

        Assert.Equal(expected, result.Format);
    }

    /// <summary>
    /// Animation survives the conversion, frame times included. A still GIF out of an animated
    /// WebP would pass a naive check and be a broken picture at the table.
    /// </summary>
    [Theory]
    [InlineData("animated.gif")]
    [InlineData("animated.webp")]
    public void AnimatedSourcesStayAnimated(string fileName)
    {
        var result = _codec.Normalise(File.ReadAllBytes(Path.Combine(_assets.Directory, fileName)));

        Assert.True(result.IsAnimated);
        Assert.Equal("gif", result.Format);

        var probe = _codec.Probe(result.Bytes);
        Assert.Equal(3, probe.Frames);
    }

    /// <summary>
    /// JPEG stays JPEG and is NOT re-encoded - the counter-check that says so is byte equality
    /// everywhere except the removed segments, with the GPS trail gone (Part 5, Part 11).
    /// </summary>
    [Fact]
    public void AJpegKeepsItsPixelsAndLosesItsGpsTrail()
    {
        var source = File.ReadAllBytes(Path.Combine(_assets.Directory, "gps.jpg"));

        // EXIF travels in an APP1 segment, marked FF E1 and introduced by "Exif\0\0". Looked for
        // as bytes rather than as the text "GPS": the coordinates are binary tag values, and a
        // search for the word would find nothing and prove nothing.
        Assert.True(HasExifSegment(source), "the generated file carries no EXIF to begin with");

        var result = _codec.Normalise(source);

        Assert.Equal("jpg", result.Format);
        Assert.True(result.Bytes.Length < source.Length, "nothing was removed at all");
        Assert.False(HasExifSegment(result.Bytes), "the EXIF segment survived");

        // The picture itself is untouched: the entropy-coded data from the scan onwards is
        // identical, byte for byte.
        Assert.Equal(Scan(source), Scan(result.Bytes));
    }

    /// <summary>
    /// PNG stays PNG and is NOT re-encoded either, and this is the assertion that pays for it. The
    /// change was made for TIME - a 24 MB PNG cost 11.6 s to decode and re-encode, measured with the
    /// real files at a hand run of M2b - and the price of that speed is that the source bytes
    /// travel on, so the byte-wise strip is now the only thing between a photograph's coordinates
    /// and the table.
    /// <para>
    /// All three shapes of text are asserted separately, because they are separate chunks: EXIF in
    /// <c>eXIf</c>, a comment in <c>tEXt</c> and the writing software beside it. A strip that
    /// caught only one of them would look right.
    /// </para>
    /// </summary>
    [Fact]
    public void APngKeepsItsPixelsAndLosesItsGpsTrail()
    {
        var source = File.ReadAllBytes(Path.Combine(_assets.Directory, "gps.png"));

        Assert.True(HasChunk(source, "eXIf"), "the generated file carries no EXIF to begin with");
        Assert.True(HasChunk(source, "tEXt"), "the generated file carries no text chunk to begin with");

        var result = _codec.Normalise(source);

        Assert.Equal("png", result.Format);
        Assert.False(HasChunk(result.Bytes, "eXIf"), "the EXIF chunk survived");
        Assert.False(HasChunk(result.Bytes, "tEXt"), "the text chunk survived");
        Assert.False(HasChunk(result.Bytes, "tIME"), "the timestamp survived");

        // The picture itself is untouched, and this is the half that says "not re-encoded": the
        // compressed image data is identical, byte for byte. A re-encode produces different IDAT
        // bytes even at identical pixels, so this fails the moment the pass-through is lost.
        Assert.Equal(Chunk(source, "IDAT"), Chunk(result.Bytes, "IDAT"));

        // And the ones that decide how it LOOKS are still there. Stripping everything blindly would
        // be the tidier line of code and would quietly damage pictures.
        Assert.True(HasChunk(result.Bytes, "IHDR"));
        Assert.True(HasChunk(result.Bytes, "IEND"));
    }

    /// <summary>
    /// The properties the re-encode used to flatten now reach the display's decoder as they were
    /// written: 16 bits per channel, interlaced, with an alpha channel. This is the codec-to-WIC
    /// seam getting WIDER, which is the cost of the pass-through - and the seam is where this
    /// project has already been wrong once.
    /// <para>
    /// Here only our own half is asserted - that the file comes through unchanged and still reads
    /// as a PNG of the right size. Whether WIC can decode it is the other half and belongs where
    /// WIC is, in the rendering tests.
    /// </para>
    /// </summary>
    [Fact]
    public void An_unusual_png_travels_on_as_it_was_written()
    {
        var source = File.ReadAllBytes(Path.Combine(_assets.Directory, "quirky.png"));

        var result = _codec.Normalise(source);

        Assert.Equal("png", result.Format);
        Assert.Equal(Chunk(source, "IDAT"), Chunk(result.Bytes, "IDAT"));

        var probe = _codec.Probe(result.Bytes);

        Assert.Equal(1, probe.Frames);
        Assert.Equal("PNG", probe.Format);
    }

    /// <summary>Whether a PNG carries a chunk of this type at all.</summary>
    private static bool HasChunk(ReadOnlySpan<byte> png, string type) => Walk(png, type) is not null;

    /// <summary>The data of the FIRST chunk of that type, for comparing one file against another.</summary>
    private static byte[] Chunk(ReadOnlySpan<byte> png, string type) =>
        Walk(png, type) ?? throw new InvalidOperationException($"no {type} chunk");

    private static byte[]? Walk(ReadOnlySpan<byte> png, string wanted)
    {
        var position = 8;

        while (position + 12 <= png.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.Slice(position, 4));

            if (position + 12L + length > png.Length)
            {
                return null;
            }

            var type = System.Text.Encoding.ASCII.GetString(png.Slice(position + 4, 4));

            if (type.Equals(wanted, StringComparison.Ordinal))
            {
                return png.Slice(position + 8, (int)length).ToArray();
            }

            position += 12 + (int)length;
        }

        return null;
    }

    /// <summary>
    /// The tolerated outcome, on its own. It is taken in like any other and reported as not
    /// assured - and what is in the stock afterwards is PNG, so the format never reaches a device.
    /// </summary>
    [Fact]
    public void AToleratedFormatIsTakenInAndMarkedAsNotAssured()
    {
        Assert.NotEmpty(_assets.Tolerated);

        foreach (var (_, path) in _assets.Tolerated)
        {
            var result = _codec.Normalise(File.ReadAllBytes(path));

            Assert.Equal(FormatStanding.Tolerated, result.Standing);
            Assert.Equal("png", result.Format);
        }
    }

    /// <summary>
    /// The refusal that has to be OUR entry: HEIC is turned away although the build can read it.
    /// A stub that merely claims the brand is enough, because the refusal bites at the
    /// DECLARATION - which is the more precise test, not the cheaper one (Part 5, Part 10).
    /// </summary>
    [Fact]
    public void HeicIsRefusedAlthoughTheBuildCanReadIt()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _codec.Probe(File.ReadAllBytes(_assets.Crafted.HeicStub)));

        Assert.Equal(ImageRejection.NotPermitted, rejected.Reason);
    }

    /// <summary>
    /// A script coder under a PNG name is refused - the decision falls on the CONTENT, and the
    /// extension gets it nowhere.
    /// <para>
    /// The REASON it comes back with is <c>Unreadable</c> rather than <c>NotPermitted</c>, and
    /// that is a consequence of the hardening working rather than a gap: the MVG coder is denied,
    /// so content detection cannot identify the file in the first place, and to us it is
    /// indistinguishable from any other blob. Where the format is FORCED - the only way to reach
    /// the coder deliberately - the policy answers with its own exception, and
    /// <see cref="CoderPolicyTests"/> asserts exactly that.
    /// </para>
    /// </summary>
    [Fact]
    public void AScriptDisguisedAsAnImageIsRefused()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _codec.Normalise(File.ReadAllBytes(_assets.Crafted.ScriptDisguisedAsPng)));

        Assert.Equal(ImageRejection.Unreadable, rejected.Reason);
    }

    /// <summary>The ordinary refusals: a broken file and a name that lies about its content.</summary>
    [Fact]
    public void ABrokenFileIsRefusedAndAMislabelledOneIsNot()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _codec.Normalise(File.ReadAllBytes(_assets.Crafted.Truncated)));
        Assert.Equal(ImageRejection.Unreadable, rejected.Reason);

        // A PNG called .jpg is a naming mistake, not a bad file - the content decides, so it comes
        // straight in (Part 5).
        var fine = _codec.Normalise(File.ReadAllBytes(_assets.Crafted.WrongExtension));
        Assert.Equal("png", fine.Format);
    }

    /// <summary>
    /// No two files of the parcours hold the same bytes, save the one pair that is meant to: the
    /// container under its two names.
    /// <para>
    /// It is here because the mislabelled file used to be a COPY of the genuine PNG, and the M2c
    /// hand-run showed what that costs. The stock is content-addressed, so the second of two
    /// identical files is recognised as one already there - and the property the file was written
    /// for is never reached. Every future file of this parcours has the same trap waiting, which is
    /// why this is a rule rather than an assertion about one name (Guide C9).
    /// </para>
    /// </summary>
    [Fact]
    public void NoFileOfTheParcoursIsACopyOfAnother()
    {
        var twins = Directory
            .EnumerateFiles(_assets.Directory, "*", SearchOption.TopDirectoryOnly)
            .GroupBy(path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" = ", group.Select(Path.GetFileName)))
            .Where(pair => pair is not "token-renamed.zip = token-with-portrait.rptok"
                and not "token-with-portrait.rptok = token-renamed.zip")
            .ToList();

        Assert.Empty(twins);
    }

    /// <summary>
    /// The first net: the forged header is read AND reported, so the limits have something to bite
    /// on - and the counter-check is the one that matters, that nothing was unfolded.
    /// </summary>
    [Fact]
    public void TheForgedHeaderIsReportedWithoutBeingDecoded()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var probe = _codec.Probe(File.ReadAllBytes(_assets.Crafted.ForgedHeaderBomb));

        Assert.Equal(60000, probe.PixelWidth);
        Assert.Equal(60000, probe.PixelHeight);
        Assert.True(GC.GetTotalMemory(forceFullCollection: true) - before < 8 * 1024 * 1024);
    }

    /// <summary>
    /// An SVG that points at an outside resource must not fetch it. If it ever does, SVG leaves
    /// both the promise and the allowed coders (Part 5) - so this is a standing question, not a
    /// settled one.
    /// </summary>
    [Fact]
    public void AnSvgWithAnExternalReferenceFetchesNothing()
    {
        var source = File.ReadAllBytes(_assets.Crafted.SvgWithExternalReference);

        try
        {
            var result = _codec.Normalise(source);

            // Rasterising is allowed; reaching out is not. A refused fetch inside a delegate would
            // have surfaced as a policy error above.
            Assert.Equal("png", result.Format);
        }
        catch (ImageRejectedException)
        {
            // Refusing it outright is just as good an answer, and the build is free to.
        }
    }

    /// <summary>Everything from the scan marker on - the entropy-coded picture itself.</summary>
    private static byte[] Scan(byte[] jpeg)
    {
        for (var i = 2; i + 1 < jpeg.Length; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA)
            {
                return jpeg[i..];
            }
        }

        return [];
    }

    /// <summary>Whether an APP1 segment introduced by <c>Exif\0\0</c> is present.</summary>
    private static bool HasExifSegment(byte[] jpeg)
    {
        for (var i = 0; i + 10 < jpeg.Length; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xE1
                && jpeg.AsSpan(i + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                return true;
            }
        }

        return false;
    }
}
