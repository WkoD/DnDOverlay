using DnDOverlay.Core;
using DnDOverlay.TestData;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The token unpacker (Part 5, Part 11). Told apart by the picture's MEASUREMENTS - 400x600 for
/// the portrait against 100x100 for the map token - never by a hash and never by a MapTool
/// version, so the generated tokens may be rebuilt at any time without an assertion turning false.
/// </summary>
public sealed class TokenContainerTests(TestDataFixture fixture)
{
    private readonly TokenSet _tokens = fixture.Assets.Tokens;
    private readonly TokenContainer _container = new();
    private readonly MagickCodec _codec = new();

    /// <summary>
    /// Both generations yield the portrait, and this is the whole point of reading two shapes: a
    /// 2009 token holds its picture base64-encoded INSIDE the note, with no entry carrying an
    /// extension. A reader built to Part 5's table alone answers "not found" here.
    /// </summary>
    [Fact]
    public void BothGenerationsYieldThePortrait()
    {
        foreach (var path in new[] { _tokens.WithPortrait, _tokens.Legacy })
        {
            var content = _container.Read(File.ReadAllBytes(path));

            Assert.Equal("Testfigur", content.Name);
            Assert.Equal(TokenPicture.Portrait, content.Picture);

            var probe = _codec.Probe(content.Image);
            Assert.Equal(400, probe.PixelWidth);
            Assert.Equal(600, probe.PixelHeight);
        }
    }

    /// <summary>
    /// No portrait is not a refusal. A top-down symbol is rarely pretty as an NPC picture, but the
    /// DM sees what they got and can delete the entry - which beats being told no (Part 5).
    /// </summary>
    [Fact]
    public void WithoutAPortraitTheMapTokenIsTaken()
    {
        var content = _container.Read(File.ReadAllBytes(_tokens.WithoutPortrait));

        Assert.Equal(TokenPicture.MapToken, content.Picture);

        var probe = _codec.Probe(content.Image);
        Assert.Equal(100, probe.PixelWidth);
        Assert.Equal(100, probe.PixelHeight);
    }

    /// <summary>
    /// The same picture out of two different containers is ONE entry, and so is the same picture
    /// once as a token and once as a PNG. That holds because what is hashed is the EXTRACTED image
    /// rather than the container (Part 5).
    /// </summary>
    [Fact]
    public void TheSamePortraitOutOfTwoContainersIsOnePicture()
    {
        var fromModern = _container.Read(File.ReadAllBytes(_tokens.WithPortrait)).Image;
        var fromLegacy = _container.Read(File.ReadAllBytes(_tokens.Legacy)).Image;
        var asPng = File.ReadAllBytes(Path.Combine(StockDirectory, "token-portrait.png"));

        Assert.Equal(asPng, fromModern);
        Assert.Equal(asPng, fromLegacy);
    }

    /// <summary>
    /// Recognised by CONTENT, and the extension lies in both directions: a genuine token under a
    /// <c>.zip</c> name is one, an ordinary archive under the token name is not.
    /// </summary>
    [Fact]
    public void TheExtensionDecidesNothing()
    {
        Assert.True(_container.LooksLikeToken(File.ReadAllBytes(_tokens.Renamed)));
        Assert.False(_container.LooksLikeToken(File.ReadAllBytes(_tokens.Foreign)));

        // And an ordinary picture is not an archive at all.
        Assert.False(_container.LooksLikeToken(File.ReadAllBytes(fixture.Assets.Promised["PNG"])));
    }

    /// <summary>
    /// A token holding no picture ends in a stated refusal - never a wrong picture and never a
    /// crash (Part 5).
    /// </summary>
    [Fact]
    public void ATokenWithoutAnyPictureIsRefusedWithAReason()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _container.Read(File.ReadAllBytes(_tokens.WithoutImage)));

        Assert.Contains("no picture", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A foreign archive under the token name is refused, and with the reason that names what is
    /// missing rather than a stack trace.
    /// </summary>
    [Fact]
    public void AForeignArchiveIsRefused()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _container.Read(File.ReadAllBytes(_tokens.Foreign)));

        Assert.Contains("content.xml", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The zip bomb: 32 MB of XML in a 33 kB archive. The image limits never see this file, which
    /// is why the XML carries its own - and the refusal comes without a memory run, because the
    /// read stops AT the limit rather than after it.
    /// </summary>
    [Fact]
    public void TheZipBombIsRefusedWithoutAMemoryRun()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var rejected = Assert.Throws<ImageRejectedException>(
            () => _container.Read(File.ReadAllBytes(_tokens.ZipBomb)));

        Assert.Contains("unpacks to more than", rejected.Message, StringComparison.Ordinal);
        Assert.True(
            GC.GetTotalMemory(forceFullCollection: true) - before < 16 * 1024 * 1024,
            "the bomb was unpacked before it was refused");
    }

    /// <summary>
    /// The external entity reaches for a local file and for the network. Prohibiting DTDs shuts
    /// both, and the counter-check that matters is the second half: nothing is fetched and no
    /// local file is read - the refusal comes before either could happen (Part 5).
    /// </summary>
    [Fact]
    public void TheExternalEntityLoadsNothing()
    {
        var rejected = Assert.Throws<ImageRejectedException>(
            () => _container.Read(File.ReadAllBytes(_tokens.ExternalEntity)));

        // Whatever the reason reads as, what must NOT have happened is that win.ini turned into a
        // token name.
        Assert.DoesNotContain("16-bit app support", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unknown structure never ends wrong - the test to the stability assumption (Part 5). A
    /// content.xml with shifted paths and renamed elements falls back to the map token or is
    /// refused with a reason, and neither a wrong picture nor a crash is among the outcomes.
    /// </summary>
    [Theory]
    [InlineData("<other.root.entirely><name>X</name></other.root.entirely>")]
    [InlineData("<net.rptools.maptool.model.Token><somethingElse><id>abc</id></somethingElse></net.rptools.maptool.model.Token>")]
    [InlineData("<net.rptools.maptool.model.Token></net.rptools.maptool.model.Token>")]
    public void UnknownStructureEndsInARefusalRatherThanAWrongPicture(string content)
    {
        var token = Rebuilt(_tokens.WithPortrait, content);

        var rejected = Assert.Throws<ImageRejectedException>(() => _container.Read(token));
        Assert.False(string.IsNullOrWhiteSpace(rejected.Message));
    }

    /// <summary>
    /// The half of the stability assumption that must NOT fail: the paths are still there, only
    /// somewhere else in the file. The portrait still comes out.
    /// </summary>
    [Fact]
    public void APathThatMovedInsideTheDocumentIsStillFound()
    {
        var original = File.ReadAllBytes(_tokens.WithPortrait);
        var keys = Keys(original);

        var reordered =
            $"<{Root}><zzz>filler</zzz><imageAssetMap><entry><null/><net.rptools.lib.MD5Key>"
            + $"<id>{keys.Map}</id></net.rptools.lib.MD5Key></entry></imageAssetMap>"
            + $"<name>Testfigur</name><portraitImage><id>{keys.Portrait}</id></portraitImage></{Root}>";

        var content = _container.Read(Rebuilt(_tokens.WithPortrait, reordered));

        Assert.Equal(TokenPicture.Portrait, content.Picture);
        Assert.Equal(400, _codec.Probe(content.Image).PixelWidth);
    }

    private const string Root = "net.rptools.maptool.model.Token";

    private string StockDirectory => fixture.Assets.Directory;

    /// <summary>The same archive with a different content.xml, so one variable moves at a time.</summary>
    private static byte[] Rebuilt(string path, string content)
    {
        var buffer = new MemoryStream(File.ReadAllBytes(path));

        using (var archive = new System.IO.Compression.ZipArchive(
                   buffer, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("content.xml")!.Delete();

            using var stream = archive.CreateEntry("content.xml").Open();
            stream.Write(System.Text.Encoding.UTF8.GetBytes(content));
        }

        return buffer.ToArray();
    }

    private static (string Portrait, string Map) Keys(byte[] token)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(token));

        var names = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("assets/", StringComparison.Ordinal)
                           && name.Contains('.', StringComparison.Ordinal))
            .Select(name => name["assets/".Length..name.LastIndexOf('.')])
            .ToList();

        // The portrait is the larger of the two files, which is how the generator built them.
        var bySize = archive.Entries
            .Where(entry => names.Any(key => entry.FullName == "assets/" + key + ".png"))
            .OrderByDescending(entry => entry.Length)
            .ToList();

        return (
            bySize[0].FullName["assets/".Length..bySize[0].FullName.LastIndexOf('.')],
            bySize[1].FullName["assets/".Length..bySize[1].FullName.LastIndexOf('.')]);
    }
}
