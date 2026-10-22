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

    /// <summary>
    /// What has to stay free beyond the picture itself. A starting point, not a measurement (Part
    /// 10): it covers the thumbnail, the inventory and the difference between source and delivered
    /// bytes, and it is small on purpose - a limit that fires in normal operation costs more than
    /// what it guards against. The 5 GB in Part 7 is a different number for a different job: that
    /// one WARNS while there is still room, this one refuses when there is not.
    /// </summary>
    private const long FreeSpaceReserve = 64L * 1024 * 1024;

    private readonly IImageCodec _codec;
    private readonly IContainerReader? _containers;
    private readonly Func<long> _freeSpace;
    private readonly AssetLimits _limits;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InventoryEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>See <see cref="InventoryDocument.Removed"/>; guarded by <see cref="_gate"/> like the entries.</summary>
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>See <see cref="Close"/>; guarded by <see cref="_gate"/>.</summary>
    private bool _closed;

    private AssetStore(
        string directory,
        IImageCodec codec,
        IContainerReader? containers,
        Func<long>? freeSpace,
        AssetLimits limits,
        TimeProvider time,
        InventoryDocument document)
    {
        Directory = directory;
        _codec = codec;
        _containers = containers;
        _freeSpace = freeSpace ?? (() => FreeSpaceOn(directory));
        _limits = limits;
        _time = time;
        CreatedAt = document.CreatedAt;

        foreach (var entry in document.Entries)
        {
            _entries[entry.AssetId] = entry;
        }

        _removed.UnionWith(document.Removed ?? []);
    }

    /// <summary>The campaign folder this stock belongs to.</summary>
    public string Directory { get; }

    /// <summary>When the campaign was created, from the inventory rather than the file system.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Whether the inventory could not be read and was set aside - the reconciliation rebuilds it (Part 3).</summary>
    public bool Reconstructed { get; private init; }

    /// <summary>
    /// Entries whose file is gone - deleted by hand, a sync not finished. <b>Marked, never removed</b>
    /// (Part 3): the DM may put the file back, and taking the same picture in again heals the entry
    /// with every scene reference intact, because the hash is the same.
    /// </summary>
    public IReadOnlySet<AssetId> Missing
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values
                    .Where(entry => !File.Exists(FileOf(entry)))
                    .Select(entry => new AssetId(entry.AssetId))
                    .ToHashSet();
            }
        }
    }

    /// <summary>
    /// Whether this stock was closed (<see cref="Close"/>) and writes nothing any more.
    /// </summary>
    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    /// <summary>
    /// Ends the writes into this folder: the campaign is renamed, deleted, switched away from or moved.
    /// A stock is bound to its folder's path for good, and a write after that - an ingest that was
    /// still decoding, a folder check still settling - recreated the old folder with a full inventory
    /// and brought the campaign back as a ghost (Part 3: the delete is final; Part 7: nobody moves a
    /// folder pictures are being written into). Returns once a write in progress is done; reading
    /// stays possible. Idempotent.
    /// </summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }

    /// <summary>Takes back <see cref="Close"/> when the step it was for did not happen - a rename the system refused.</summary>
    internal void Reopen()
    {
        lock (_gate)
        {
            _closed = false;
        }
    }

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
    /// <param name="containers">
    /// The unpacker for containers that hold a picture (Part 5). Optional: without one a
    /// <c>.rptok</c> is simply an unreadable file, which is what a build with no unpacker means.
    /// </param>
    /// <param name="freeSpace">
    /// How many bytes are still free on the campaign's drive. Handed in like every other limit
    /// (rule 10) - the case worth proving is a FULL disk, and no test may create one.
    /// </param>
    public static AssetStore Open(
        string directory,
        IImageCodec codec,
        TimeProvider time,
        AssetLimits? limits = null,
        IContainerReader? containers = null,
        Func<long>? freeSpace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(time);

        System.IO.Directory.CreateDirectory(Path.Combine(directory, AssetsFolder, ThumbnailFolder));

        var path = Path.Combine(directory, InventoryFileName);
        var document = Read(path, out var unreadable);
        var reconstructed = unreadable;

        if (unreadable)
        {
            // <b>Rebuilt, not refused</b> (Part 3): the inventory is an index over the folder, not a
            // document, so files plus scene references give it back - the reconciliation does that
            // right after. The unreadable file is set aside rather than deleted: it is the DM's, and
            // somebody may want to look at what happened to it.
            File.Move(path, path + ".unreadable-" + time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), overwrite: true);
        }

        document ??= new InventoryDocument { CreatedAt = time.GetUtcNow() };

        var store = new AssetStore(
            directory, codec, containers, freeSpace, limits ?? AssetLimits.Default, time, document)
        {
            Reconstructed = reconstructed,
        };

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

    /// <summary>
    /// What the file system says is free where the campaign lies.
    /// <para>
    /// An unanswerable question is answered with "plenty", deliberately: a drive that cannot be
    /// asked - a network share, an unusual mount - must not turn every ingest into a refusal. The
    /// guard is against a disk filling up, not against not knowing.
    /// </para>
    /// </summary>
    private static long FreeSpaceOn(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));

            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    /// <summary>
    /// Renames a picture, campaign-wide (Part 3). The name is an identifier, not a file name: a name
    /// another picture carries is <b>refused</b> - compared ordinally and ignoring case, like the
    /// filter that could never tell the two apart - while a change of case of its own name passes.
    /// Typed names are trimmed; an empty one is refused.
    /// </summary>
    public NameOutcome Rename(AssetId asset, string name)
    {
        var wanted = name?.Trim();

        if (string.IsNullOrEmpty(wanted))
        {
            return NameOutcome.Empty;
        }

        lock (_gate)
        {
            if (_closed || !_entries.TryGetValue(asset.Value, out var entry))
            {
                return NameOutcome.Unknown;
            }

            if (_entries.Values.Any(other => !ReferenceEquals(other, entry) && string.Equals(other.Name, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                return NameOutcome.Taken;
            }

            entry.Name = wanted;
            SaveInventory();
        }

        return NameOutcome.Done;
    }

    /// <summary>
    /// Takes a picture out of the stock: the entry, the file and its thumbnail. <b>Only this</b> - the
    /// screens and the saved scenes are the caller's to clear first (Part 7: what is deleted
    /// disappears everywhere), and the order matters: the reference goes before the bytes. A file
    /// that cannot be deleted right now - open in a viewer - stays behind as a foreign file the next
    /// reconciliation names (M5b5) - noted in the inventory, or it would come back in as a picture
    /// without an entry; the entry is gone either way. False when there was no such entry, or the
    /// stock is closed: its path may name another campaign's folder by now.
    /// </summary>
    public bool Remove(AssetId asset)
    {
        lock (_gate)
        {
            if (_closed || !_entries.Remove(asset.Value, out var entry))
            {
                return false;
            }

            SaveInventory();

            try
            {
                File.Delete(FileOf(entry));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _removed.Add(Path.GetFileName(FileOf(entry)));
                SaveInventory();
            }

            try
            {
                File.Delete(ThumbnailPath(asset));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Derived: the next reconciliation removes it as a thumbnail nobody has.
            }
        }

        return true;
    }

    /// <summary>Whether this file is the picture of an entry that was removed while its file could not be deleted (M5b5).</summary>
    internal bool WasRemoved(string file)
    {
        lock (_gate)
        {
            return _removed.Contains(Path.GetFileName(file));
        }
    }

    /// <summary>Forgets the notes of <see cref="Remove"/> whose file is gone by now - deleted by hand, or taken in again.</summary>
    internal void ForgetRemovedGone()
    {
        lock (_gate)
        {
            if (!_closed && _removed.RemoveWhere(name => !File.Exists(Path.Combine(Directory, AssetsFolder, name))) > 0)
            {
                try
                {
                    SaveInventory();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Only notes about files that are gone: the next write of the inventory drops them.
                }
            }
        }
    }

    /// <summary>A picture taken in again is no longer a removed one; called under <see cref="_gate"/> as its entry is added.</summary>
    private void Unremove(string assetId) =>
        _removed.RemoveWhere(name => Path.GetFileNameWithoutExtension(name).Equals(assetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where the thumbnail of an asset lies, whether or not it exists yet.</summary>
    public string ThumbnailPath(AssetId id) =>
        Path.Combine(
            Directory,
            AssetsFolder,
            ThumbnailFolder,
            string.Create(CultureInfo.InvariantCulture, $"{id.Value}_{ThumbnailWidth}.png"));

    private IngestResult Ingest(ReadOnlyMemory<byte> source, string proposedName)
    {
        // A container is opened FIRST, before the hash is taken, and both halves of that matter.
        // The identity has to be the extracted picture's, or the same portrait out of two tokens
        // would be two entries and the same picture as token and as PNG likewise (Part 5). And the
        // name it carried beats the one the entrance proposed - that is stage 1 of the five, and
        // the only stage no entrance can supply, because only this step sees it (Part 3).
        if (_containers is { } containers && containers.Holds(source))
        {
            try
            {
                var content = containers.Read(source);

                source = content.Image;
                proposedName = content.Name;
            }
            catch (ImageRejectedException rejected)
            {
                return new IngestResult.Refused(rejected.Reason.AsIntake(), rejected.Message);
            }
        }

        // Identity first, and it hashes the SOURCE. Hashing the output instead breaks dedup two
        // ways that both surface late: the encoder writes timestamps by default, and an encoder
        // update changes the bytes - after which the same file yields a new hash and the store
        // collects duplicates (Part 5).
        var assetId = new AssetId(Convert.ToHexStringLower(SHA256.HashData(source.Span)));

        lock (_gate)
        {
            // Already here. Nothing is written and, above all, the name the DM gave it STAYS -
            // re-importing a file must not rename it (Part 7). Unless its file is gone: then this is
            // the picture coming back, and it is written again under the entry it always had - the
            // scenes pointing at it are healed with it (Part 3).
            if (_entries.TryGetValue(assetId.Value, out var known) && File.Exists(FileOf(known)))
            {
                return new IngestResult.Taken(
                    new AssetRef(assetId, known.Meta, known.Name), AlreadyPresent: true, FormatStanding.Promised);
            }
        }

        // Room is asked about BEFORE the decode, in the same place and for the same reason as the
        // size limits: decoding is the expensive step, and spending twelve seconds on a picture
        // that cannot be written afterwards helps nobody. An evening of large maps adds up, and the
        // failure is stated with the campaign named rather than surfacing as a broken write
        // (Part 5).
        if (_freeSpace() is var free && free < source.Length + FreeSpaceReserve)
        {
            return new IngestResult.Refused(
                IntakeRejection.NoSpace,
                Strings.NoSpace(free / (1024 * 1024), (source.Length + FreeSpaceReserve) / (1024 * 1024), Directory));
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
            return new IngestResult.Refused(rejected.Reason.AsIntake(), rejected.Message);
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
            return new IngestResult.Refused(rejected.Reason.AsIntake(), rejected.Message);
        }

        lock (_gate)
        {
            // Closed while this one decoded - renamed, deleted, switched away from, moved. Writing now
            // would bring the old folder back as a campaign with a full inventory and one file in it
            // (Part 3: the delete is final). Asked under the same lock Close takes, so a write that
            // has begun is finished before the folder may move, and none begins after.
            if (_closed)
            {
                return new IngestResult.Refused(IntakeRejection.Unavailable, Strings.CampaignClosed);
            }

            // Written before the entry exists, and that order is the promise: an ingest that fails
            // after the file is there leaves no item without a picture, because the reference only
            // goes out once the bytes are on disk (Part 11).
            WriteFiles(entry, normalised, thumbnail);

            // Somebody else may have finished the same image while this one was decoding. Theirs
            // won - the bytes are identical by construction, so the loser drops its work rather
            // than making a second entry. A healed entry lands here too, and keeps its name.
            if (_entries.TryGetValue(assetId.Value, out var raced))
            {
                return new IngestResult.Taken(
                    new AssetRef(assetId, raced.Meta, raced.Name), AlreadyPresent: true, normalised.Standing);
            }

            entry.Name = FreeName(proposedName);
            _entries[assetId.Value] = entry;
            Unremove(assetId.Value);
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

    /// <summary>
    /// Puts back the file of an entry marked missing, from a copy found in the folder - "taking the
    /// same picture in again heals the entry" (Part 3). Only when the copy is the file that went: its
    /// bytes match the content hash the entry was recorded with, and nothing lies in its place.
    /// </summary>
    /// <returns>Whether the file was moved into place.</returns>
    internal bool Restore(AssetId asset, string file, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        lock (_gate)
        {
            if (_closed
                || !_entries.TryGetValue(asset.Value, out var entry)
                || !string.Equals(entry.ContentHash, hash, StringComparison.Ordinal)
                || File.Exists(FileOf(entry)))
            {
                return false;
            }

            File.Move(file, FileOf(entry));
        }

        return true;
    }

    /// <summary>Whether the stock has an entry for this picture, whether or not its file is there.</summary>
    internal bool Knows(AssetId asset)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(asset.Value);
        }
    }

    /// <summary>
    /// Whether a file of this extension is one the stock delivers as it lies - what
    /// <see cref="IImageCodec.Normalise"/> hands out. Only such a file can be adopted under its hash name
    /// without going through the intake: anything else would be served as it is, past the conversion
    /// and with a content type nobody can read (Part 5).
    /// </summary>
    internal static bool IsDeliveryFormat(string extension) =>
        extension.ToLowerInvariant() is "png" or "gif" or "jpg" or "jpeg";

    /// <summary>
    /// Takes a picture named by its hash that has no entry - a crash between writing and recording,
    /// or a rebuilt inventory (Part 3). The identity is the file name; the file is read once to
    /// measure it. It is checked BEFORE it is touched - a delivery format, a picture this build reads,
    /// within the limits - and only then moved into <c>assets\</c>, never over a file that lies there:
    /// a file that fails any of this is foreign and stays where it is (Part 3).
    /// </summary>
    /// <returns>
    /// The entry's reference, or <see langword="null"/> when the file is no picture to adopt this way,
    /// another file lies where it would go, or the stock is closed.
    /// </returns>
    public AssetRef? AdoptStored(string file, string name, DateTimeOffset addedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = new AssetId(Path.GetFileNameWithoutExtension(file).ToLowerInvariant());
        var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();

        if (!id.IsWellFormed || !IsDeliveryFormat(extension))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(file);
        ImageProbe probe;

        try
        {
            probe = _codec.Probe(bytes);

            // The same limits as at the door: a file does not get past them by carrying a hash name.
            _limits.ThrowIfExceeded(probe, bytes.Length);
        }
        catch (ImageRejectedException)
        {
            return null;
        }

        var target = Path.Combine(Directory, AssetsFolder, id.Value + "." + extension);

        var entry = new InventoryEntry
        {
            AssetId = id.Value,
            AddedAt = addedAt,
            Extension = extension,
            PixelWidth = probe.PixelWidth,
            PixelHeight = probe.PixelHeight,
            IsAnimated = probe.Frames > 1,
            Bytes = bytes.LongLength,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        };

        lock (_gate)
        {
            if (_closed)
            {
                return null;
            }

            if (_entries.TryGetValue(id.Value, out var known))
            {
                return new AssetRef(id, known.Meta, known.Name);
            }

            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                // Moved only now, after the checks. And never over what lies there: nobody has looked
                // at whether those bytes match, and a damaged copy must not replace a good file.
                if (File.Exists(target))
                {
                    return null;
                }

                File.Move(file, target);
            }

            entry.Name = FreeName(name);
            _entries[id.Value] = entry;
            Unremove(id.Value);
            SaveInventory();
        }

        return new AssetRef(id, entry.Meta, entry.Name);
    }

    /// <summary>
    /// "Verify stock" (Part 3, Part 7): every picture's file read and hashed against the content hash
    /// it was recorded with - the thorough check the name match cannot make, and the reason it is
    /// voluntary: two thousand pictures are minutes of reading. A file that is gone is not counted
    /// here; it is in <see cref="Missing"/> already.
    /// </summary>
    /// <param name="progress">Told the number of pictures checked so far.</param>
    /// <returns>The pictures whose file no longer matches what was taken in.</returns>
    public async Task<StockVerification> VerifyAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var damaged = new List<AssetId>();
        var checkedFiles = 0;
        var done = 0;

        foreach (var entry in Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = FileOf(entry);

            if (File.Exists(file))
            {
                byte[] hash;

                var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

                await using (stream.ConfigureAwait(false))
                {
                    hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                checkedFiles++;

                if (!string.Equals(Convert.ToHexStringLower(hash), entry.ContentHash, StringComparison.Ordinal))
                {
                    damaged.Add(new AssetId(entry.AssetId));
                }
            }

            progress?.Report(++done);
        }

        return new StockVerification(checkedFiles, damaged);
    }

    /// <summary>
    /// Makes the thumbnails that are missing and removes the ones nobody has - derived files,
    /// repaired without a word (Part 3). Only pictures WITHOUT a thumbnail are read.
    /// </summary>
    /// <returns>How many were made.</returns>
    public int RepairThumbnails()
    {
        var made = 0;

        foreach (var entry in Entries)
        {
            var thumbnail = ThumbnailPath(new AssetId(entry.AssetId));
            var file = FileOf(entry);

            if (File.Exists(thumbnail) || !File.Exists(file))
            {
                continue;
            }

            try
            {
                // Made outside the lock - it is a decode - and written inside it, like every write here,
                // so a closed stock writes nothing (see Close).
                var bytes = _codec.Thumbnail(File.ReadAllBytes(file), ThumbnailWidth);

                lock (_gate)
                {
                    if (_closed)
                    {
                        return made;
                    }

                    AtomicFile.WriteContentAddressed(thumbnail, bytes);
                }

                made++;
            }
            catch (ImageRejectedException)
            {
                // A picture whose thumbnail cannot be made shows a blank tile; its file is still the DM's.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Derived, and tried again on the next check - never a reason to end the one running.
            }
        }

        var folder = Path.Combine(Directory, AssetsFolder, ThumbnailFolder);

        // Under the lock, and the entries asked inside it rather than from a list taken at the start: an
        // ingest writes its thumbnail and adds its entry in one step under the same lock, so a picture
        // dropped while this ran is never seen with a thumbnail and without an entry - and its fresh
        // thumbnail is not removed as one nobody has.
        lock (_gate)
        {
            if (_closed || !System.IO.Directory.Exists(folder))
            {
                return made;
            }

            foreach (var stray in System.IO.Directory.EnumerateFiles(folder, "*.png"))
            {
                var id = Path.GetFileNameWithoutExtension(stray).Split('_')[0];

                if (_entries.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    File.Delete(stray);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held open by the stock grid's loader, say: gone on a later check.
                }
            }
        }

        return made;
    }

    /// <summary>
    /// Whether the stock's file for this picture holds exactly these bytes - the one condition under
    /// which the reconciliation may remove the file they were read from. Part 3 removes
    /// "<b>byte-identical</b> copies" and nothing else: a token the picture was taken out of, or a WebP
    /// that became a PNG on the way in, is not what lies in <c>assets\</c>, and deleting it would lose
    /// what the copy does not carry. Compared against the file on disk, not against the recorded hash,
    /// so a damaged stock file never costs the good original.
    /// </summary>
    internal bool Holds(AssetId asset, ReadOnlySpan<byte> bytes)
    {
        InventoryEntry? entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(asset.Value, out entry))
            {
                return false;
            }
        }

        try
        {
            var file = new FileInfo(FileOf(entry));

            return file.Exists && file.Length == bytes.Length && File.ReadAllBytes(file.FullName).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether this file is where the stock keeps an entry's picture - by name, without opening it.</summary>
    public bool Keeps(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var stem = Path.GetFileNameWithoutExtension(file);

        lock (_gate)
        {
            return _entries.TryGetValue(stem, out var entry)
                && string.Equals(Path.GetFullPath(FileOf(entry)), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
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
            Removed = [.. _removed.Order(StringComparer.OrdinalIgnoreCase)],
        };

        AtomicFile.Write(
            Path.Combine(Directory, InventoryFileName), JsonSerializer.SerializeToUtf8Bytes(document, Json));
    }

    private static InventoryDocument? Read(string path, out bool unreadable)
    {
        unreadable = false;

        if (!File.Exists(path))
        {
            return null;
        }

        InventoryDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<InventoryDocument>(File.ReadAllBytes(path), Json);
        }
        catch (JsonException)
        {
            unreadable = true;

            return null;
        }

        if (document is null)
        {
            return null;
        }

        if (document.SchemaVersion > InventoryDocument.CurrentSchemaVersion)
        {
            throw new CampaignSchemaException(
                Strings.CampaignTooNew(path, document.SchemaVersion, InventoryDocument.CurrentSchemaVersion));
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

/// <summary>What became of renaming a picture.</summary>
public enum NameOutcome
{
    /// <summary>Renamed and written.</summary>
    Done,

    /// <summary>Another picture carries that name - refused, the control names it (Part 3).</summary>
    Taken,

    /// <summary>Nothing was typed.</summary>
    Empty,

    /// <summary>There is no such picture in the stock.</summary>
    Unknown,
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

/// <summary>What "verify stock" found: how many files were read, and which no longer match (Part 3).</summary>
/// <param name="Checked">Files read and hashed - pictures whose file is gone are not among them.</param>
/// <param name="Damaged">Pictures whose file's content no longer matches the hash it was taken in with.</param>
public sealed record StockVerification(int Checked, IReadOnlyList<AssetId> Damaged);
