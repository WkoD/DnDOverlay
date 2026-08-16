using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// The stock of one campaign: the image files, content-addressed, and the inventory over them.
/// <para>
/// It sits IN the campaign folder rather than beside it, and that costs deduplication across
/// campaigns - the same monster picture can lie on disk twice. It is still right, because
/// otherwise disk space would never actually be freed: a global store would have to keep every
/// image some other campaign still uses, and "remove NPC" would become bookkeeping without a
/// return (Part 3).
/// </para>
/// <para>
/// The hub reaches it through <see cref="IAssetSource"/> alone and never learns the folder
/// exists - the arrangement is the hub's, the material is the campaign's (Part 2). The
/// architecture test holds that line, because a reference from the hub into the stock would
/// compile without a murmur.
/// </para>
/// </summary>
public sealed class AssetStore : IAssetSource, IAssetSink
{
    private const string InventoryFileName = "inventory.json";
    private const string AssetsFolder = "assets";
    private const string ThumbnailFolder = "thumbs";

    /// <summary>
    /// The width thumbnails are made at. Two steps are foreseen for the stock grid (Part 7); the
    /// second joins with the grid itself, and adding it later costs one more file per image rather
    /// than a migration.
    /// </summary>
    private const int ThumbnailWidth = 256;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IImageCodec _codec;
    private readonly AssetLimits _limits;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InventoryEntry> _entries = new(StringComparer.Ordinal);

    private AssetStore(
        string directory, IImageCodec codec, AssetLimits limits, TimeProvider time, InventoryDocument document)
    {
        Directory = directory;
        _codec = codec;
        _limits = limits;
        _time = time;
        CreatedAt = document.CreatedAt;

        foreach (var entry in document.Entries)
        {
            _entries[entry.AssetId] = entry;
        }
    }

    /// <summary>The campaign folder this stock belongs to.</summary>
    public string Directory { get; }

    /// <summary>When the campaign was created, from the inventory rather than the file system.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>How many images are in the stock.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Opens the stock of a campaign folder, creating the inventory if there is none - the stock
    /// is not saved, it simply comes into being (Part 3).
    /// </summary>
    /// <exception cref="CampaignSchemaException">
    /// The inventory was written by a NEWER version. It is refused with a reason rather than
    /// replaced: unlike the configuration, where a display PC must never have the one outcome of
    /// failing to start, a campaign holds work, and starting fresh over it would destroy it
    /// (Part 3, Part 11).
    /// </exception>
    public static AssetStore Open(
        string directory, IImageCodec codec, TimeProvider time, AssetLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(time);

        System.IO.Directory.CreateDirectory(Path.Combine(directory, AssetsFolder, ThumbnailFolder));

        var path = Path.Combine(directory, InventoryFileName);
        var document = Read(path) ?? new InventoryDocument { CreatedAt = time.GetUtcNow() };

        var store = new AssetStore(directory, codec, limits ?? AssetLimits.Default, time, document);

        if (!File.Exists(path))
        {
            store.SaveInventory();
        }

        return store;
    }

    /// <inheritdoc />
    public bool TryOpen(AssetId id, out Stream data, out string contentType)
    {
        data = Stream.Null;
        contentType = string.Empty;

        // The endpoint takes this identifier from a paired device. Without the check,
        // GET /assets/..%5C..%5Cwindows%5C... reads arbitrary files off the DM's machine
        // (Part 4, Part 5) - so it is asked here too, not only at the endpoint.
        if (!id.IsWellFormed)
        {
            return false;
        }

        InventoryEntry? entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(id.Value, out entry))
            {
                return false;
            }
        }

        var file = FileOf(entry);

        if (!File.Exists(file))
        {
            return false;
        }

        data = File.OpenRead(file);
        contentType = ContentTypeOf(entry.Extension);

        return true;
    }

    /// <inheritdoc />
    public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType)
    {
        data = Stream.Null;
        contentType = string.Empty;

        // Same gate as the full picture, and for the same reason: this identifier comes off the
        // wire, and the path is built from it (Part 4, Part 5).
        if (!id.IsWellFormed || width <= 0)
        {
            return false;
        }

        var file = ThumbnailPath(id);

        if (!File.Exists(file))
        {
            return false;
        }

        data = File.OpenRead(file);
        contentType = "image/png";

        return true;
    }

    /// <inheritdoc />
    public Task<IngestResult> IngestAsync(
        ReadOnlyMemory<byte> source, string proposedName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedName);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Ingest(source, proposedName));
    }

    /// <summary>The entries, for the stock listing.</summary>
    public IReadOnlyList<InventoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values];
            }
        }
    }

    /// <summary>Where the thumbnail of an asset lies, whether or not it exists yet.</summary>
    public string ThumbnailPath(AssetId id) =>
        Path.Combine(
            Directory,
            AssetsFolder,
            ThumbnailFolder,
            string.Create(CultureInfo.InvariantCulture, $"{id.Value}_{ThumbnailWidth}.png"));

    private IngestResult Ingest(ReadOnlyMemory<byte> source, string proposedName)
    {
        // Identity first, and it hashes the SOURCE. Hashing the output instead breaks dedup two
        // ways that both surface late: the encoder writes timestamps by default, and an encoder
        // update changes the bytes - after which the same file yields a new hash and the store
        // collects duplicates (Part 5).
        var assetId = new AssetId(Convert.ToHexStringLower(SHA256.HashData(source.Span)));

        lock (_gate)
        {
            if (_entries.TryGetValue(assetId.Value, out var known))
            {
                // Already here. Nothing is written and, above all, the name the DM gave it STAYS -
                // re-importing a file must not rename it (Part 7).
                return new IngestResult.Taken(
                    new AssetRef(assetId, known.Meta, known.Name), AlreadyPresent: true, FormatStanding.Promised);
            }
        }

        NormalisedImage normalised;

        try
        {
            var probe = _codec.Probe(source);

            // Before decoding, always. A size check on a finished decode is worthless - decoding
            // is the expensive step (Part 5).
            _limits.ThrowIfExceeded(probe, source.Length);

            normalised = _codec.Normalise(source);
        }
        catch (ImageRejectedException rejected)
        {
            return new IngestResult.Refused(rejected.Reason, rejected.Message);
        }

        var entry = new InventoryEntry
        {
            AssetId = assetId.Value,
            AddedAt = _time.GetUtcNow(),
            Extension = normalised.Format,
            PixelWidth = normalised.PixelWidth,
            PixelHeight = normalised.PixelHeight,
            IsAnimated = normalised.IsAnimated,
            Bytes = normalised.Bytes.LongLength,

            // The second hash, over the DELIVERED bytes. Without it the display could not verify
            // a download at all, because the file name carries the source identity (Part 5).
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(normalised.Bytes)),
        };

        byte[]? thumbnail;

        try
        {
            thumbnail = Thumbnail(assetId, normalised);
        }
        catch (ImageRejectedException rejected)
        {
            // A picture whose thumbnail cannot be made is a picture that cannot be DECODED, and it
            // is turned away here rather than at the table.
            return new IngestResult.Refused(rejected.Reason, rejected.Message);
        }

        // Written before the entry exists, and that order is the promise: an ingest that fails
        // after the file is there leaves no item without a picture, because the reference only
        // goes out once the bytes are on disk (Part 11).
        WriteFiles(entry, normalised, thumbnail);

        lock (_gate)
        {
            // Somebody else may have finished the same image while this one was decoding. Theirs
            // won - the bytes are identical by construction, so the loser drops its work rather
            // than making a second entry.
            if (_entries.TryGetValue(assetId.Value, out var raced))
            {
                return new IngestResult.Taken(
                    new AssetRef(assetId, raced.Meta, raced.Name), AlreadyPresent: true, normalised.Standing);
            }

            entry.Name = FreeName(proposedName);
            _entries[assetId.Value] = entry;
            SaveInventory();
        }

        return new IngestResult.Taken(
            new AssetRef(assetId, entry.Meta, entry.Name), AlreadyPresent: false, normalised.Standing);
    }

    /// <summary>
    /// Makes the thumbnail, and it is the <b>acceptance test</b> for the picture as a whole.
    /// <c>null</c> means there already is one, from an earlier run.
    /// <para>
    /// <b>This used to be allowed to fail quietly, and the ground under that has moved.</b> The
    /// comment read "a missing thumbnail is a blank tile, not a lost image" - which held while
    /// <c>Normalise</c> decoded and re-encoded every picture, so a thumbnail failing was a SECOND
    /// failure of something already proved to work. Since M2b both JPEG and PNG hand their bytes
    /// through untouched, and this is the only place on this side where a picture is unfolded at
    /// all. A failure here is therefore the first and only news that the file is broken.
    /// </para>
    /// <para>
    /// Refusing costs one picture at the control. Not refusing costs it at the table, on every
    /// device at once, in front of everybody - and it would have been knowable here.
    /// </para>
    /// </summary>
    private byte[]? Thumbnail(AssetId assetId, NormalisedImage normalised)
    {
        // An existing thumbnail is proof enough: this picture decoded once already. Re-proving it
        // would put a full decode on every re-import of a picture the stock knows.
        return File.Exists(ThumbnailPath(assetId))
            ? null
            : _codec.Thumbnail(normalised.Bytes, ThumbnailWidth);
    }

    private void WriteFiles(InventoryEntry entry, NormalisedImage normalised, byte[]? thumbnail)
    {
        // The discriminator keeps two writers of the same image off one scratch file - without it
        // the loser could rename a half-written file into place under a valid hash (Part 11).
        var scratch = Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture);

        // Content-addressed: the name IS the content, so a writer that loses the rename race
        // finds its own bytes already there (Part 5).
        AtomicFile.WriteContentAddressed(FileOf(entry), normalised.Bytes, scratch);

        if (thumbnail is not null)
        {
            AtomicFile.WriteContentAddressed(
                ThumbnailPath(new AssetId(entry.AssetId)), thumbnail, scratch);
        }
    }

    /// <summary>
    /// Names are unique within a campaign, and an import of two hundred files must not stop to
    /// ask: a taken name is numbered rather than refused. RENAMING onto a taken name is the other
    /// case and is refused with a mention - that one is a deliberate act (Part 3).
    /// </summary>
    private string FreeName(string proposed)
    {
        var taken = new HashSet<string>(_entries.Values.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(proposed))
        {
            return proposed;
        }

        for (var number = 2; ; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{proposed} ({number})");

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string FileOf(InventoryEntry entry) =>
        Path.Combine(Directory, AssetsFolder, entry.AssetId + "." + entry.Extension);

    private void SaveInventory()
    {
        var document = new InventoryDocument
        {
            CreatedAt = CreatedAt,
            Entries = [.. _entries.Values.OrderBy(entry => entry.AddedAt)],
        };

        AtomicFile.Write(
            Path.Combine(Directory, InventoryFileName), JsonSerializer.SerializeToUtf8Bytes(document, Json));
    }

    private static InventoryDocument? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        InventoryDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<InventoryDocument>(File.ReadAllBytes(path), Json);
        }
        catch (JsonException ex)
        {
            throw new CampaignSchemaException(
                $"The inventory at {path} could not be read: {ex.Message}", ex);
        }

        if (document is null)
        {
            return null;
        }

        if (document.SchemaVersion > InventoryDocument.CurrentSchemaVersion)
        {
            throw new CampaignSchemaException(
                $"The campaign at {path} was written by a newer version of DnDOverlay "
                + $"(schema {document.SchemaVersion}, this version reads {InventoryDocument.CurrentSchemaVersion}). "
                + "It is left untouched - opening it here would destroy work.");
        }

        return document;
    }

    private static string ContentTypeOf(string extension) => extension switch
    {
        "png" => "image/png",
        "gif" => "image/gif",
        "jpg" or "jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };
}

/// <summary>
/// A campaign that cannot be opened as it stands. Refusing is the point: the configuration
/// cluster puts a broken file aside and starts on defaults, because a display PC must never have
/// the one outcome of failing to start - a campaign is the opposite case, it holds work
/// (Part 3, Part 6, Part 11).
/// </summary>
public sealed class CampaignSchemaException : Exception
{
    public CampaignSchemaException(string message)
        : base(message)
    {
    }

    public CampaignSchemaException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public CampaignSchemaException()
    {
    }
}
