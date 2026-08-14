using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DnDOverlay.TestData;

/// <summary>
/// The MapTool token containers, all of them generated (Part 10). No handwork and no committed
/// binary, and all variants come into being the same way rather than two by hand and three derived
/// from them.
/// <para>
/// The structure is COPIED FROM existing tokens rather than invented - and copied is all it is:
/// not a byte, not an image, not a name is taken over. The difference carries the whole
/// construction; a ZIP assembled from memory would test our understanding of the format instead of
/// MapTool's actual output.
/// </para>
/// <para>
/// Looking at two generations rather than one is the correction the copying produced (14.08.2026).
/// Part 5 described the container from a single, modern sample: image data under
/// <c>assets/&lt;md5&gt;.&lt;ext&gt;</c>, a note beside it naming the extension. A token from 2009
/// has NO entry with an extension - <c>assets/&lt;md5&gt;</c> is the note WITH THE IMAGE INSIDE
/// it, base64 in an <c>&lt;image&gt;</c> element, and there is no <c>&lt;extension&gt;</c>
/// anywhere. An unpacker built to the description alone answers "not found" for a file holding a
/// perfectly good JPEG - and the promise that a DM's years of collected tokens come in is the
/// whole reason this path exists.
/// </para>
/// <para>
/// What did NOT move in those fifteen years are the three paths read out of <c>content.xml</c>.
/// That is exactly where the plan bet on stability, and there it wins.
/// </para>
/// </summary>
internal static class TokenFiles
{
    /// <summary>
    /// The version of the OLDER template, and it is provenance rather than a dependency: nothing
    /// we read hangs on a MapTool version (Part 5). It says where the rebuilt structure comes from
    /// and how old it is - which is why the version travels into the generated
    /// <c>properties.xml</c> as well, where a real token carries it too.
    /// </summary>
    internal const string LegacyTemplateVersion = "1.3.b55";

    /// <summary>The version of the newer template, and the one installed at the time of writing.</summary>
    internal const string ModernTemplateVersion = "1.11.5";

    /// <summary>
    /// The token's name, and at the same time the expected caption of the stock entry (Part 11).
    /// Asserted through the image MEASUREMENTS, never through a hash and never through a version.
    /// </summary>
    internal const string TokenName = "Testfigur";

    /// <summary>
    /// Uncompressed size of the inflated <c>content.xml</c>. Far above any sane limit for that
    /// file and still written in a moment - a 10 GB one in a 40 kB archive is trivial to build,
    /// and that is the point of the entry, not its exact size.
    /// </summary>
    private const int InflatedXmlBytes = 32 * 1024 * 1024;

    internal static TokenSet Write(string directory, string portraitPath, string mapTokenPath)
    {
        var portrait = File.ReadAllBytes(portraitPath);
        var mapToken = File.ReadAllBytes(mapTokenPath);

        var withPortrait = Path.Combine(directory, "token-with-portrait.rptok");
        Modern(withPortrait, portrait, mapToken);

        var legacy = Path.Combine(directory, "token-legacy.rptok");
        Legacy(legacy, portrait, mapToken);

        // No portrait: the fallback to the map token, which many tokens need. A top-down symbol is
        // rarely pretty as an NPC picture, but it beats a rejection - the DM sees what they got.
        var withoutPortrait = Path.Combine(directory, "token-without-portrait.rptok");
        Modern(withoutPortrait, portrait: null, mapToken);

        // Neither: content.xml names identifiers no entry answers to. Has to end in a rejection
        // with a reason, never in a wrong image and never in a crash.
        var withoutImage = Path.Combine(directory, "token-without-image.rptok");
        Modern(withoutImage, portrait: null, mapToken: null);

        var zipBomb = Path.Combine(directory, "token-zip-bomb.rptok");
        ZipBomb(zipBomb);

        var externalEntity = Path.Combine(directory, "token-external-entity.rptok");
        ExternalEntity(externalEntity);

        // The extension can lie in both directions, and the content decides either way: a genuine
        // token under a foreign name, and a foreign archive under the token name.
        var renamed = Path.Combine(directory, "token-renamed.zip");
        Modern(renamed, portrait, mapToken);

        var foreign = Path.Combine(directory, "not-a-token.rptok");
        Foreign(foreign);

        return new TokenSet(
            withPortrait, legacy, withoutPortrait, withoutImage, zipBomb, externalEntity, renamed, foreign);
    }

    /// <summary>
    /// The shape of a token from 2024: image data in its own entry, and a note beside it naming
    /// name, extension and type.
    /// </summary>
    private static void Modern(string path, byte[]? portrait, byte[]? mapToken)
    {
        var portraitKey = portrait is null ? NewKey() : KeyOf(portrait);
        var mapKey = mapToken is null ? NewKey() : KeyOf(mapToken);

        using var archive = Create(path);

        // A token without a portrait does not carry an empty element - the path is simply absent,
        // and "not found" is what sends the unpacker to the map token (Part 5).
        Text(archive, "content.xml", Content(portraitKey, mapKey, withPortrait: portrait is not null));
        Text(archive, "properties.xml", Properties(ModernTemplateVersion, withHeroLab: true));

        if (portrait is not null)
        {
            Bytes(archive, $"assets/{portraitKey}.png", portrait);
            Text(archive, $"assets/{portraitKey}", ModernNote(portraitKey, "png"));
        }

        if (mapToken is not null)
        {
            Bytes(archive, $"assets/{mapKey}.png", mapToken);
            Text(archive, $"assets/{mapKey}", ModernNote(mapKey, "png"));
        }

        // MapTool's own previews. We ignore them and make our own, so they are here only so that
        // ignoring them is something the tests actually exercise.
        Bytes(archive, "thumbnail", mapToken ?? [1, 2, 3]);
        Bytes(archive, "thumbnail_large", portrait ?? [1, 2, 3]);
    }

    /// <summary>
    /// The shape of a token from 2009: NO entry with an extension, and the image base64-encoded
    /// inside the note. There is no <c>&lt;extension&gt;</c> to read, so an unpacker that insists
    /// on one finds nothing here.
    /// </summary>
    private static void Legacy(string path, byte[] portrait, byte[] mapToken)
    {
        var portraitKey = KeyOf(portrait);
        var mapKey = KeyOf(mapToken);

        using var archive = Create(path);

        Text(archive, "content.xml", Content(portraitKey, mapKey, withPortrait: true));
        Text(archive, "properties.xml", Properties(LegacyTemplateVersion, withHeroLab: false));
        Text(archive, $"assets/{portraitKey}", LegacyNote(portraitKey, portrait));
        Text(archive, $"assets/{mapKey}", LegacyNote(mapKey, mapToken));
        Bytes(archive, "thumbnail", mapToken);
    }

    private static void ZipBomb(string path)
    {
        using var archive = Create(path);

        // One long run of the same characters: a few kilobytes on disk, 32 MB unpacked. The image
        // limits do not help here - the XML needs its own.
        var entry = archive.CreateEntry("content.xml", CompressionLevel.SmallestSize);
        using (var stream = entry.Open())
        {
            var chunk = Encoding.UTF8.GetBytes(new string(' ', 64 * 1024));

            stream.Write("<net.rptools.maptool.model.Token>"u8);

            for (var written = 0; written < InflatedXmlBytes; written += chunk.Length)
            {
                stream.Write(chunk);
            }

            stream.Write("</net.rptools.maptool.model.Token>"u8);
        }

        Text(archive, "properties.xml", Properties(ModernTemplateVersion, withHeroLab: true));
    }

    private static void ExternalEntity(string path)
    {
        using var archive = Create(path);

        // Reads a local file if the reader lets it, and reaches the network if it resolves. Both
        // are shut by DtdProcessing.Prohibit and a null resolver - which is what this file exists
        // to prove rather than to assume (Part 5).
        Text(archive, "content.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE net.rptools.maptool.model.Token [
              <!ENTITY stolen SYSTEM "file:///c:/windows/win.ini">
              <!ENTITY fetched SYSTEM "http://example.invalid/collect">
            ]>
            <net.rptools.maptool.model.Token>
              <name>&stolen;</name>
              <portraitImage>
                <id>&fetched;</id>
              </portraitImage>
            </net.rptools.maptool.model.Token>
            """);

        Text(archive, "properties.xml", Properties(ModernTemplateVersion, withHeroLab: true));
    }

    /// <summary>An ordinary archive wearing the token extension. Recognised by content, not by name.</summary>
    private static void Foreign(string path)
    {
        using var archive = Create(path);

        Text(archive, "readme.txt", "Just an archive.");
        Text(archive, "notes/list.txt", "Nothing to see.");
    }

    /// <summary>
    /// The three paths that are read, and the only three. No object is deserialised by a foreign
    /// type name - a missing path is simply "not found", which is what makes a shifted or renamed
    /// structure end in a fallback or a stated rejection rather than in a wrong image (Part 5).
    /// </summary>
    private static string Content(string portraitKey, string mapKey, bool withPortrait)
    {
        var portrait = withPortrait
            ? $"""
                 <portraitImage>
                   <id>{portraitKey}</id>
                 </portraitImage>
               """
            : string.Empty;

        return $"""
            <net.rptools.maptool.model.Token>
              <id>
                <baGUID>wKgCaO6iRMx0AwAAwAACaA==</baGUID>
              </id>
              <beingImpersonated>false</beingImpersonated>
              <imageAssetMap>
                <entry>
                  <null/>
                  <net.rptools.lib.MD5Key>
                    <id>{mapKey}</id>
                  </net.rptools.lib.MD5Key>
                </entry>
              </imageAssetMap>
              <name>{TokenName}</name>
              <ownerType>0</ownerType>
              <tokenShape>TOP_DOWN</tokenShape>
              <tokenType>NPC</tokenType>
              <layer>TOKEN</layer>
              <propertyType>Basic</propertyType>
            {portrait}
              <sightType>Normal</sightType>
              <hasSight>false</hasSight>
            </net.rptools.maptool.model.Token>
            """;
    }

    private static string Properties(string version, bool withHeroLab)
    {
        var heroLab = withHeroLab
            ? """
                <entry>
                  <string>herolab</string>
                  <boolean>false</boolean>
                </entry>
              """
            : string.Empty;

        return $"""
            <map>
              <entry>
                <string>version</string>
                <string>{version}</string>
              </entry>
            {heroLab}
            </map>
            """;
    }

    private static string ModernNote(string key, string extension)
        => $"""
            <net.rptools.maptool.model.Asset>
              <id>
                <id>{key}</id>
              </id>
              <name>{TokenName}</name>
              <extension>{extension}</extension>
              <type>IMAGE</type>
            </net.rptools.maptool.model.Asset>
            """;

    private static string LegacyNote(string key, byte[] image)
        => $"""
            <net.rptools.maptool.model.Asset>
              <id>
                <id>{key}</id>
              </id>
              <image>{Convert.ToBase64String(image)}</image>
              <name>{TokenName}</name>
            </net.rptools.maptool.model.Asset>
            """;

    /// <summary>
    /// The container's own key format is an MD5 of the image bytes, so a copied structure carries
    /// one. It is a LOOKUP KEY inside the archive and nothing else - our identity for an asset is
    /// the SHA-256 of the extracted image (Part 5), and no assertion anywhere may hang on this
    /// value.
    /// </summary>
    [SuppressMessage(
        "Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "Not a security use: MapTool's archives key their entries by MD5, and this "
            + "reproduces that structure. Asset identity is SHA-256 elsewhere (Part 5).")]
    private static string KeyOf(byte[] image) => Convert.ToHexStringLower(MD5.HashData(image));

    /// <summary>A key that answers to no entry - for the token whose images are missing.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static ZipArchive Create(string path)
    {
        File.Delete(path);
        return ZipFile.Open(path, ZipArchiveMode.Create);
    }

    private static void Text(ZipArchive archive, string name, string content)
        => Bytes(archive, name, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

    private static void Bytes(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        stream.Write(content);
    }
}
