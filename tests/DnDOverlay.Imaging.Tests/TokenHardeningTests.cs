using System.IO.Compression;
using System.Text;
using DnDOverlay.Core;
using DnDOverlay.TestData;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// Two promises of the unpacker that were built and never proved: the archive limit, and that the
/// extension is READ rather than searched for.
/// </summary>
public sealed class TokenHardeningTests(TestDataFixture fixture)
{
    private const string Root = "net.rptools.maptool.model.Token";
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly TestAssetSet _assets = fixture.Assets;
    private readonly TokenContainer _container = new();

    /// <summary>
    /// An archive of thousands of entries is refused before anything is read out of it. No token
    /// has that many, and the number is the cheapest thing about such a file to check.
    /// </summary>
    [Fact]
    public void AnArchiveWithImplausiblyManyEntriesIsRefused()
    {
        var rejected = Assert.Throws<ImageRejectedException>(() => _container.Read(Crowded(200)));

        Assert.Contains("entries", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counter-check, and it is what makes the limit a limit rather than a wall: an ordinary
    /// token has a handful of entries and comes straight through.
    /// </summary>
    [Fact]
    public void AnOrdinaryTokenIsWellUnderTheEntryLimit()
    {
        var content = _container.Read(File.ReadAllBytes(_assets.Tokens.WithPortrait));

        Assert.Equal("Testfigur", content.Name);
    }

    /// <summary>
    /// "The extension is not guessed - the note names it, which is more reliable than a search for
    /// <c>assets/&lt;md5&gt;.*</c> and the reason that file exists" (Part 5).
    /// <para>
    /// Until now that was checked by looking at the GENERATED container, which proves the note
    /// carries an extension and nothing about what the reader does with it. Here the archive holds
    /// TWO candidates and the note names the second: a search would find the decoy, which sorts
    /// first and is the one an enumeration hands over.
    /// </para>
    /// </summary>
    [Fact]
    public void TheExtensionComesFromTheNoteRatherThanFromASearch()
    {
        var wanted = File.ReadAllBytes(Path.Combine(_assets.Directory, "token-portrait.png"));
        var decoy = File.ReadAllBytes(Path.Combine(_assets.Directory, "token-map.png"));

        var content = _container.Read(WithDecoy(wanted, decoy));

        Assert.Equal(wanted, content.Image);
        Assert.NotEqual(decoy, content.Image);
    }

    /// <summary>An archive with more entries than any token has.</summary>
    private static byte[] Crowded(int entries)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Text(archive, "content.xml", $"<{Root}><name>Viele</name></{Root}>");

            for (var n = 0; n < entries; n++)
            {
                Text(archive, $"filler/{n}.txt", "x");
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A token whose note names <c>png</c> while an <c>.bmp</c> of the same key sits in front of
    /// it - so picking by name order picks the wrong one.
    /// </summary>
    private static byte[] WithDecoy(byte[] wanted, byte[] decoy)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Text(archive, "content.xml",
                $"<{Root}><name>Testfigur</name><portraitImage><id>{Key}</id></portraitImage></{Root}>");

            Bytes(archive, $"assets/{Key}.bmp", decoy);
            Bytes(archive, $"assets/{Key}.png", wanted);

            Text(archive, $"assets/{Key}",
                "<net.rptools.maptool.model.Asset>"
                + $"<id><id>{Key}</id></id><name>Testfigur</name>"
                + "<extension>png</extension><type>IMAGE</type>"
                + "</net.rptools.maptool.model.Asset>");
        }

        return buffer.ToArray();
    }

    private static void Text(ZipArchive archive, string name, string content) =>
        Bytes(archive, name, Encoding.UTF8.GetBytes(content));

    private static void Bytes(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        stream.Write(content);
    }
}
