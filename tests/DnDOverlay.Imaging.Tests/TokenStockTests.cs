using System.IO.Compression;
using DnDOverlay.TestData;
using ImageMagick;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The token containers, checked against the two real templates they were copied from. The
/// unpacker does not exist yet - what these tests hold in place is the STOCK, and above all the
/// finding that made a second generation necessary (14.08.2026).
/// <para>
/// Without them the correction would live in a commit message: someone tidying the generator later
/// would drop the odd-looking legacy shape, every test would stay green, and an unpacker built to
/// the tidy version would answer "not found" for a fifteen-year-old token holding a perfectly good
/// image.
/// </para>
/// </summary>
public sealed class TokenStockTests(TestDataFixture fixture)
{
    private readonly TokenSet _tokens = fixture.Assets.Tokens;

    /// <summary>
    /// A 2024 token keeps image data in its own entry and names the extension in the note beside
    /// it. That is the shape Part 5 describes - and it describes only this one.
    /// </summary>
    [Fact]
    public void TheModernShapeCarriesTheExtensionInItsNote()
    {
        using var token = ZipFile.OpenRead(_tokens.WithPortrait);

        var images = Names(token).Where(name => name.StartsWith("assets/", StringComparison.Ordinal)
                                                && name.Contains('.', StringComparison.Ordinal)).ToList();
        Assert.Equal(2, images.Count);

        var note = Entry(token, images[0][..images[0].LastIndexOf('.')]);
        Assert.Contains("<extension>png</extension>", note, StringComparison.Ordinal);
        Assert.Contains("<type>IMAGE</type>", note, StringComparison.Ordinal);

        Assert.Contains("thumbnail_large", Names(token), StringComparer.Ordinal);
    }

    /// <summary>
    /// The correction itself. A 2009 token has NO entry with an extension: the note IS the image,
    /// base64 inside an <c>image</c> element, and there is no <c>extension</c> anywhere to read.
    /// An unpacker that insists on one finds nothing here.
    /// </summary>
    [Fact]
    public void TheLegacyShapeHoldsTheImageInsideItsNoteAndNamesNoExtension()
    {
        using var token = ZipFile.OpenRead(_tokens.Legacy);

        var assets = Names(token).Where(name => name.StartsWith("assets/", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, assets.Count);
        Assert.DoesNotContain(assets, name => name.Contains('.', StringComparison.Ordinal));

        var note = Entry(token, assets[0]);
        Assert.Contains("<image>", note, StringComparison.Ordinal);
        Assert.DoesNotContain("<extension>", note, StringComparison.Ordinal);

        // Its provenance travels with it, as it does in a real token - and it is provenance, not a
        // dependency: nothing we read hangs on a version (Part 5).
        var properties = Entry(token, "properties.xml");
        Assert.Contains("1.3.b55", properties, StringComparison.Ordinal);
        Assert.DoesNotContain("herolab", properties, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the finding, and the good one: across fifteen years the three paths that
    /// are actually read did not move. That is exactly where the plan bet on stability.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothGenerationsCarryTheSameThreePaths(bool legacy)
    {
        using var token = ZipFile.OpenRead(legacy ? _tokens.Legacy : _tokens.WithPortrait);
        var content = Entry(token, "content.xml");

        Assert.Contains("<net.rptools.maptool.model.Token>", content, StringComparison.Ordinal);
        Assert.Contains("<name>Testfigur</name>", content, StringComparison.Ordinal);
        Assert.Contains("<portraitImage>", content, StringComparison.Ordinal);
        Assert.Contains("<net.rptools.lib.MD5Key>", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The portrait is told apart from the map token by its MEASUREMENTS - never by a hash and
    /// never by a MapTool version. That is what lets the token be rebuilt at any time without the
    /// assertion turning false (Part 5, Part 11).
    /// </summary>
    [Fact]
    public void PortraitAndMapTokenAreToldApartByTheirMeasurements()
    {
        var directory = Path.GetDirectoryName(_tokens.WithPortrait)!;

        var portrait = new MagickImageInfo(Path.Combine(directory, "token-portrait.png"));
        Assert.Equal(400u, portrait.Width);
        Assert.Equal(600u, portrait.Height);

        var map = new MagickImageInfo(Path.Combine(directory, "token-map.png"));
        Assert.Equal(100u, map.Width);
        Assert.Equal(100u, map.Height);
    }

    /// <summary>
    /// The fallback case has one image, the empty one has none - and the second must end in a
    /// stated rejection rather than in a wrong picture, which is only testable if it really is
    /// empty.
    /// </summary>
    [Fact]
    public void TheFallbackCasesCarryWhatTheirNamesSay()
    {
        using (var fallback = ZipFile.OpenRead(_tokens.WithoutPortrait))
        {
            Assert.Single(Names(fallback), IsImageEntry);
            Assert.DoesNotContain("<portraitImage>", Entry(fallback, "content.xml"), StringComparison.Ordinal);
        }

        using var empty = ZipFile.OpenRead(_tokens.WithoutImage);
        Assert.DoesNotContain(Names(empty), IsImageEntry);
    }

    /// <summary>
    /// A 10 GB <c>content.xml</c> in a 40 kB archive is trivial to build, which is why the XML
    /// needs a limit of its own - the image limits never see it.
    /// </summary>
    [Fact]
    public void TheZipBombIsSmallOnDiskAndHugeUnpacked()
    {
        using var token = ZipFile.OpenRead(_tokens.ZipBomb);
        var content = token.GetEntry("content.xml")!;

        Assert.True(content.Length > 32 * 1024 * 1024, $"unpacks to only {content.Length} bytes");
        Assert.True(content.CompressedLength < 128 * 1024,
            $"compresses to {content.CompressedLength} bytes and is no longer a bomb");
    }

    /// <summary>
    /// The file has to declare a DTD, or a reader that permits them has nothing to fall for and
    /// the test would be green without touching the defence (Part 5).
    /// </summary>
    [Fact]
    public void TheExternalEntityTokenReallyDeclaresOne()
    {
        using var token = ZipFile.OpenRead(_tokens.ExternalEntity);
        var content = Entry(token, "content.xml");

        Assert.Contains("<!DOCTYPE", content, StringComparison.Ordinal);
        Assert.Contains("SYSTEM \"file:///", content, StringComparison.Ordinal);
        Assert.Contains("SYSTEM \"http://", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The extension can lie in both directions, so the stock carries both lies: a genuine token
    /// under a foreign name, and a foreign archive under the token name. Recognised by content -
    /// ZIP signature plus a content.xml with the expected root element.
    /// </summary>
    [Fact]
    public void TheExtensionLiesInBothDirections()
    {
        using (var renamed = ZipFile.OpenRead(_tokens.Renamed))
        {
            Assert.Contains("<net.rptools.maptool.model.Token>", Entry(renamed, "content.xml"),
                StringComparison.Ordinal);
        }

        using var foreign = ZipFile.OpenRead(_tokens.Foreign);
        Assert.DoesNotContain("content.xml", Names(foreign), StringComparer.Ordinal);
    }

    private static bool IsImageEntry(string name)
        => name.StartsWith("assets/", StringComparison.Ordinal)
           && name.Contains('.', StringComparison.Ordinal);

    private static List<string> Names(ZipArchive archive)
        => [.. archive.Entries.Select(entry => entry.FullName)];

    private static string Entry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
