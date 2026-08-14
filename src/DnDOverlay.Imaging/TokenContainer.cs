using System.IO.Compression;
using System.Text;
using System.Xml;
using DnDOverlay.Core;

namespace DnDOverlay.Imaging;

/// <summary>
/// Reads a MapTool <c>.rptok</c> and hands out the picture inside it. The DM has had their monsters
/// as tokens for years; pulling them in is the shortest way to a full stock (Part 5).
/// <para>
/// A container is not an image format, so this sits IN FRONT of the codec rather than inside it
/// (rule 8) - and it needs no image library at all, only a zip reader and an XML reader. What comes
/// out goes through the ordinary pipeline: same format decision, same limits, same normalisation.
/// </para>
/// <para>
/// <b>Two generations, and that is the correction measured on 14.08.2026.</b> Part 5 described the
/// container from one modern sample: image data under <c>assets/&lt;md5&gt;.&lt;ext&gt;</c> and a
/// note beside it naming the extension. A token from 2009 has no entry with an extension at all -
/// there <c>assets/&lt;md5&gt;</c> IS the note, with the image base64-encoded inside it. A reader
/// built to the table alone answers "not found" for a file holding a perfectly good JPEG, and the
/// promise that a DM's years of collected tokens come in is the whole reason this path exists.
/// </para>
/// <para>
/// What did NOT move in those fifteen years are the three paths read out of <c>content.xml</c>.
/// That is where the plan bet on stability, and there it wins - which is also why nothing is
/// deserialised by a foreign type name: three element values are read, and a path that is missing
/// or has moved is simply "not found", never a wrong picture and never a crash.
/// </para>
/// </summary>
public sealed class TokenContainer(TokenLimits? limits = null)
{
    private const string RootElement = "net.rptools.maptool.model.Token";
    private const string ContentEntry = "content.xml";
    private const string AssetPrefix = "assets/";

    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    private readonly TokenLimits _limits = limits ?? TokenLimits.Default;

    /// <summary>
    /// Whether these bytes are a token, decided on CONTENT: a zip holding a
    /// <c>content.xml</c> whose root element is the token type. The extension can lie in both
    /// directions, and the format parcours checks exactly that (Part 5).
    /// </summary>
    public bool LooksLikeToken(ReadOnlyMemory<byte> data)
    {
        if (!data.Span.StartsWith(ZipSignature))
        {
            return false;
        }

        try
        {
            using var archive = Open(data);
            var content = archive.GetEntry(ContentEntry);

            return content is not null && RootElement.Equals(RootOf(content), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or ImageRejectedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the token and returns the picture the DM wants to show, with the name to file it
    /// under.
    /// </summary>
    /// <exception cref="ImageRejectedException">
    /// Not a token, or a token holding no usable picture, or one that exceeds a limit. Stated with
    /// a reason - never a wrong picture, never a crash.
    /// </exception>
    public TokenContent Read(ReadOnlyMemory<byte> data)
    {
        using var archive = Open(data);

        var content = archive.GetEntry(ContentEntry)
            ?? throw Refuse("This file is not a MapTool token: it has no content.xml.");

        var described = ReadContent(content);

        if (!RootElement.Equals(described.Root, StringComparison.Ordinal))
        {
            throw Refuse("This file is not a MapTool token: its content.xml describes something else.");
        }

        // The portrait is what the DM wants to show. Many tokens have none, and a top-down symbol
        // is rarely pretty as an NPC picture - but it beats a refusal: the DM sees what they got
        // and can delete the entry (Part 5).
        var portrait = Picture(archive, described.PortraitKey);
        var kind = TokenPicture.Portrait;

        if (portrait is null)
        {
            portrait = Picture(archive, described.MapKey);
            kind = TokenPicture.MapToken;
        }

        if (portrait is null)
        {
            throw Refuse(
                "This token holds no picture that could be read - neither a portrait nor a map token.");
        }

        var name = string.IsNullOrWhiteSpace(described.Name) ? "Token" : described.Name;

        return new TokenContent(name, portrait, kind);
    }

    private ZipArchive Open(ReadOnlyMemory<byte> data)
    {
        ZipArchive archive;

        try
        {
            archive = new ZipArchive(new MemoryStream(data.ToArray(), writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new ImageRejectedException(
                ImageRejection.Unreadable, "This file is not a readable archive.", ex);
        }

        if (archive.Entries.Count > _limits.MaxEntries)
        {
            archive.Dispose();

            throw Refuse(
                $"This archive holds {archive.Entries.Count} entries, which is beyond anything a token "
                + $"has (limit {_limits.MaxEntries}).");
        }

        return archive;
    }

    /// <summary>
    /// The picture behind one key, in whichever generation the token happens to be - or
    /// <see langword="null"/> when this key answers to nothing.
    /// </summary>
    private byte[]? Picture(ZipArchive archive, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var note = archive.GetEntry(AssetPrefix + key);

        if (note is null)
        {
            return null;
        }

        var described = ReadNote(note);

        // The 2009 shape: the note IS the picture. There is no entry with an extension to look
        // for, and no <extension> element to read either.
        if (described.Image is { Length: > 0 })
        {
            return described.Image;
        }

        // The modern shape. The extension is NOT guessed - the note names it, which is more
        // reliable than a search for assets/<md5>.* and the reason that file exists (Part 5).
        if (string.IsNullOrEmpty(described.Extension))
        {
            return null;
        }

        var image = archive.GetEntry(AssetPrefix + key + "." + described.Extension);

        return image is null ? null : ReadEntry(image, _limits.MaxImageBytes);
    }

    /// <summary>
    /// The three values, and only these three. XStream is not used and no object is built from a
    /// type name in the file - the same stance as the JSON source generator takes (Part 4, Part 5).
    /// </summary>
    private Described ReadContent(ZipArchiveEntry entry)
    {
        try
        {
            return ParseContent(Text(entry, _limits.MaxXmlBytes));
        }
        catch (XmlException ex)
        {
            // A prohibited DTD lands here, and so does anything simply malformed. Both have to
            // come out as a STATED refusal: an XmlException escaping this class would be, to the
            // caller, exactly the crash Part 5 rules out - and the DM would see a stack trace
            // where a sentence belongs.
            throw new ImageRejectedException(
                ImageRejection.Unreadable,
                "The description inside this token could not be read. It is either damaged or it "
                + "declares a document type, which is not accepted.",
                ex);
        }
    }

    private static Described ParseContent(string text)
    {

        string? root = null;
        string? name = null;
        string? portrait = null;
        string? map = null;

        var path = new Stack<string>();

        using var reader = XmlReader.Create(new StringReader(text), Settings());

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    root ??= reader.Name;

                    if (!reader.IsEmptyElement)
                    {
                        path.Push(reader.Name);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (path.Count > 0)
                    {
                        path.Pop();
                    }

                    break;

                case XmlNodeType.Text:
                    var here = path.ToArray();

                    if (Matches(here, "name", RootElement))
                    {
                        name ??= reader.Value;
                    }
                    else if (Matches(here, "id", "portraitImage", RootElement))
                    {
                        portrait ??= reader.Value;
                    }
                    else if (Matches(here, "id", "net.rptools.lib.MD5Key", "entry", "imageAssetMap", RootElement))
                    {
                        map ??= reader.Value;
                    }

                    break;

                default:
                    break;
            }
        }

        return new Described(root, name, portrait, map, null, null);
    }

    /// <summary>The note beside a key: either the extension, or - in the older shape - the image.</summary>
    private Described ReadNote(ZipArchiveEntry entry)
    {
        try
        {
            return ParseNote(Text(entry, _limits.MaxNoteBytes));
        }
        catch (XmlException)
        {
            // A note we cannot read is a key that answers to nothing - which sends the caller to
            // the map token, or to a stated refusal. Never a wrong picture.
            return new Described(null, null, null, null, null, null);
        }
    }

    private static Described ParseNote(string text)
    {

        string? extension = null;
        byte[]? image = null;

        using var reader = XmlReader.Create(new StringReader(text), Settings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "extension":
                    extension ??= reader.ReadElementContentAsString();
                    break;

                case "image":
                    var encoded = reader.ReadElementContentAsString();
                    image ??= Convert.TryFromBase64String(encoded, new byte[encoded.Length], out var written)
                        ? Convert.FromBase64String(encoded)[..written]
                        : null;
                    break;

                default:
                    break;
            }
        }

        return new Described(null, null, null, null, extension, image);
    }

    private static bool Matches(string[] path, params string[] expected) =>
        path.Length >= expected.Length && path.Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal);

    private string? RootOf(ZipArchiveEntry entry)
    {
        using var reader = XmlReader.Create(new StringReader(Text(entry, _limits.MaxXmlBytes)), Settings());

        return reader.MoveToContent() == XmlNodeType.Element ? reader.Name : null;
    }

    /// <summary>
    /// XML is a new attack surface, and both of its classic holes are shut here: external entities
    /// reach local files and the network, and entity expansion ("billion laughs") exhausts memory.
    /// Prohibiting DTDs closes both, and a null resolver means nothing can be fetched even if one
    /// slipped through (Part 5).
    /// </summary>
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };

    private static string Text(ZipArchiveEntry entry, long limit) =>
        Encoding.UTF8.GetString(ReadEntry(entry, limit));

    /// <summary>
    /// Reads one entry into memory, and stops at the limit while reading rather than trusting the
    /// size in the archive's directory - that number is written by whoever made the file. A 10 GB
    /// <c>content.xml</c> in a 40 kB archive is trivial to build, which is why the XML has a limit
    /// of its own and does not share the image's (Part 5).
    /// <para>
    /// Nothing is ever unpacked to disk: two named entries are read into memory, and no entry name
    /// ever becomes a file path. There is no zip slip because there is no writing - and the
    /// architecture test holds that by forbidding the extracting assembly outright.
    /// </para>
    /// </summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        using var stream = entry.Open();
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > limit)
            {
                throw Refuse(
                    $"The entry {entry.FullName} unpacks to more than the {limit / 1024} kB allowed for it.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ImageRejectedException Refuse(string message) =>
        new(ImageRejection.Unreadable, message);

    private sealed record Described(
        string? Root, string? Name, string? PortraitKey, string? MapKey, string? Extension, byte[]? Image);
}

/// <summary>What came out of a token container.</summary>
/// <param name="Name">
/// The token's name, which becomes the stock entry's caption - so the entry reads "Testfigur"
/// rather than a hash. For this path it is the best source the five-stage name derivation has
/// (Part 3).
/// </param>
/// <param name="Image">
/// The picture AS IT LAY IN THE CONTAINER - unpacked, not yet normalised. That is what "source"
/// means for the identity, and it is why the same portrait out of two different tokens, and the
/// same picture once as a token and once as a PNG, are ONE entry (Part 5).
/// </param>
/// <param name="Picture">Which of the two it turned out to be.</param>
public sealed record TokenContent(string Name, byte[] Image, TokenPicture Picture);

/// <summary>Which picture a token yielded.</summary>
public enum TokenPicture
{
    /// <summary>The portrait - what the DM wants to show.</summary>
    Portrait,

    /// <summary>The map token, taken only because there was no portrait.</summary>
    MapToken,
}

/// <summary>
/// The limits of the container itself. A zip bomb comes back here and needs its own set: the image
/// limits never see the XML, and the XML is where the cheap attack is (Part 5).
/// </summary>
/// <param name="MaxEntries">More than any token has - the counter-check to an archive of thousands.</param>
/// <param name="MaxXmlBytes">
/// Its own limit, and separate on purpose: a 10 GB <c>content.xml</c> in a 40 kB archive is
/// trivial to build.
/// </param>
/// <param name="MaxNoteBytes">
/// The note beside an asset. In the older shape it carries the picture, so it is sized for one.
/// </param>
/// <param name="MaxImageBytes">The picture entry. The ordinary asset limits apply after that.</param>
public sealed record TokenLimits(
    int MaxEntries = 64,
    long MaxXmlBytes = 4L * 1024 * 1024,
    long MaxNoteBytes = 128L * 1024 * 1024,
    long MaxImageBytes = 128L * 1024 * 1024)
{
    /// <summary>The values above, as the ordinary case.</summary>
    public static TokenLimits Default { get; } = new();
}
