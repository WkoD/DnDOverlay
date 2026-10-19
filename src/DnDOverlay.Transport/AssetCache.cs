using DnDOverlay.Core;

namespace DnDOverlay.Transport;

/// <summary>
/// The display's picture store on disk. A picture is transferred to a device <b>once</b> as long
/// as it lies here - moving it between screens costs nothing, and only an eviction costs a second
/// transfer (Part 5).
/// <para>
/// <b>It carries bytes and file paths, never decoded bitmaps.</b> That is not tidiness: a
/// <c>BitmapSource</c> in here would hang WPF off <c>Transport</c>, and two costed promises would
/// go with it - the price of the WinUI fallback from Spike A ("Core, Transport and the protocol
/// stay untouched") and the slim Display MSI, which weighs about 5 MB only because the control
/// alone carries the heavy decoder (Part 2, Part 9).
/// </para>
/// <para>
/// <b>What is found at start is counted, and that is all it is.</b> The three rules of Part 5 -
/// emptying on exit, adopting after a hard end, trimming with the first <c>SceneSnapshot</c> and
/// the five-minute wipe without a control - are M5a and none of them is built. What was built here
/// by accident was the middle of the first: the files survived, <see cref="TryGet"/> served them,
/// and the bookkeeping knew nothing about them - so the ceiling did not apply to a single one of
/// them and the directory grew without a bound across evenings. Reading them in at start is the
/// smallest thing that makes the ceiling true again, and it decides nothing that M5a has to decide.
/// </para>
/// </summary>
public sealed class AssetCache
{
    /// <summary>Part 6's number for the whole device, and it is handed in so a test can shrink it.</summary>
    public const long DefaultMaxBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// What tells the two files of one picture apart. It is written once and read back once, and
    /// both ends have to agree - a store that names them one way and adopts them another would come
    /// up empty every time.
    /// </summary>
    private const string ThumbnailSuffix = ".thumb";

    private readonly Lock _gate = new();
    private readonly Dictionary<AssetId, Entry> _entries = [];
    private readonly string _directory;
    private readonly long _maxBytes;

    /// <summary>
    /// Counts uses, and it is deliberately a counter rather than a clock. "Longest unused" over
    /// file timestamps stops being cosmetic the moment a machine's clock is wrong: it then evicts
    /// the wrong thing, and nothing about the symptom points at the clock. A monotonic sequence
    /// cannot be wrong in that way, and it needs no <c>TimeProvider</c> to be testable
    /// (Part 1, idea 7).
    /// </summary>
    private long _sequence;

    /// <param name="directory">
    /// Handed in rather than derived (rule 10). In the running application this is
    /// <c>&lt;data root&gt;\cache</c>, which the <c>--data</c> switch moves with everything else
    /// (Part 6).
    /// </param>
    public AssetCache(string directory, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _directory = directory;
        _maxBytes = maxBytes;

        Directory.CreateDirectory(directory);
        Adopt();
    }

    /// <summary>
    /// Takes what is already lying in the directory into the bookkeeping.
    /// <para>
    /// <b>Every adopted picture ranks below everything used in this session</b> - they all keep
    /// <c>LastUsed</c> at zero, and the first use of one lifts it above them. Among themselves no
    /// order is claimed, deliberately: the only thing that could order them is a file timestamp,
    /// and a store that evicts by the clock evicts the wrong thing the moment a machine's clock is
    /// wrong, with nothing about the symptom pointing at the cause.
    /// </para>
    /// <para>
    /// A directory that cannot be read is an empty one. This runs in the display's start-up path,
    /// and a picture store is never a reason for a display PC not to come up (Part 6).
    /// </para>
    /// </summary>
    private void Adopt()
    {
        IEnumerable<FileInfo> found;

        try
        {
            found = new DirectoryInfo(_directory).EnumerateFiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in found)
        {
            // A scratch file is not a picture. It is what a write that was cut off leaves behind,
            // and counting it would put a half file into the ceiling under no identifier at all.
            if (file.Name.EndsWith(AtomicFile.TemporarySuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var thumbnail = file.Name.EndsWith(ThumbnailSuffix, StringComparison.Ordinal);
            var id = new AssetId(thumbnail ? file.Name[..^ThumbnailSuffix.Length] : file.Name);

            // Anything else in there is somebody else's. The name is the identifier and nothing
            // else, so a file that is not one was not written by this store.
            if (!id.IsWellFormed)
            {
                continue;
            }

            var entry = _entries.TryGetValue(id, out var known) ? known : new Entry { Adopted = true };

            if (thumbnail)
            {
                entry.ThumbnailBytes = file.Length;
            }
            else
            {
                entry.OriginalBytes = file.Length;
            }

            _entries[id] = entry;
        }
    }

    /// <summary>How many pictures were found on disk from an earlier run and not yet settled.</summary>
    public int Adopted
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values.Count(entry => entry.Adopted);
            }
        }
    }

    /// <summary>
    /// Settles what was found from an earlier run against the first complete set of scenes: whatever
    /// no item of this device needs goes, original and thumbnail together; what is needed stays and
    /// counts as this run's from now on (Part 5, rule 3).
    /// <para>
    /// <b>Narrower than the running eviction, on purpose.</b> In the run, a picture no longer needed
    /// stays until the ceiling asks for room - it will probably come back. A leftover from an
    /// earlier run has no such prospect; it is a store whose purpose is over. Only adopted entries
    /// are looked at, so nothing this run has fetched can be taken by it.
    /// </para>
    /// </summary>
    /// <returns>How many adopted pictures there were, and how many of them went.</returns>
    public (int Adopted, int Discarded) PruneAdopted(IReadOnlySet<AssetId> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        lock (_gate)
        {
            var adopted = _entries.Where(pair => pair.Value.Adopted).ToList();
            var discarded = 0;

            foreach (var (id, entry) in adopted)
            {
                if (inUse.Contains(id))
                {
                    entry.Adopted = false;

                    continue;
                }

                Delete(Path(id, thumbnail: false));
                Delete(Path(id, thumbnail: true));
                _entries.Remove(id);
                discarded++;
            }

            return (adopted.Count, discarded);
        }
    }

    /// <summary>
    /// Empties the store, the most recently used picture first (Part 5, rule 1).
    /// <para>
    /// <b>The order is what makes a cut-off harmless.</b> Windows grants a shutdown a few seconds and
    /// the process may not get to the end; what survives is then the oldest and least important,
    /// never what was on the table a moment ago. Whatever is left, the next start adopts and the
    /// first scene prunes (rule 3) - so a cut-off empty and a crash look alike, and there is one path.
    /// </para>
    /// </summary>
    /// <returns>The pictures removed, in the order they went - until the store was empty or the token said stop.</returns>
    public IReadOnlyList<AssetId> Empty(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var removed = new List<AssetId>();

            // Adopted entries carry no order among themselves - their LastUsed is 0 - and they go
            // last, which is right: they are older than anything this run has shown.
            foreach (var (id, _) in _entries.OrderByDescending(pair => pair.Value.LastUsed).ToList())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Delete(Path(id, thumbnail: false));
                Delete(Path(id, thumbnail: true));
                _entries.Remove(id);
                removed.Add(id);
            }

            return removed;
        }
    }

    /// <summary>What the store currently holds, originals and thumbnails together.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values.Sum(entry => entry.Bytes);
            }
        }
    }

    /// <summary>How many pictures are held - a picture, not a file.</summary>
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
    /// Puts the delivered bytes into the store. Written beside and renamed, so an aborted write
    /// never leaves a half file under a valid name (Part 11).
    /// </summary>
    public void Store(AssetId id, ReadOnlySpan<byte> bytes) => Put(id, bytes, thumbnail: false);

    /// <summary>
    /// The thumbnail of the same picture. It is held <b>with</b> the original and evicted with it:
    /// the two belong together, and dropping only the thumbnail would take the next load path
    /// exactly its quick start (Part 5).
    /// </summary>
    public void StoreThumbnail(AssetId id, ReadOnlySpan<byte> bytes) => Put(id, bytes, thumbnail: true);

    /// <summary>Reads a picture back, and counts as a use.</summary>
    public bool TryGet(AssetId id, out byte[] bytes) => Take(id, thumbnail: false, out bytes);

    /// <summary>Reads a thumbnail back, and counts as a use of the picture.</summary>
    public bool TryGetThumbnail(AssetId id, out byte[] bytes) => Take(id, thumbnail: true, out bytes);

    /// <summary>
    /// Brings the store back under its ceiling by dropping the longest unused pictures, and
    /// reports which they were.
    /// <para>
    /// <paramref name="inUse"/> is <b>never</b> evicted, however old it is - a file a current item
    /// needs is the one file that must not go (Part 11). If that leaves the store above its
    /// ceiling, it stays above it: showing the table what it is meant to show beats a number.
    /// </para>
    /// <para>
    /// The caller decides when. Only the display knows which pictures its scenes are standing on,
    /// and a cache that tried to find out would have to know about scenes.
    /// </para>
    /// </summary>
    public IReadOnlyList<AssetId> Trim(IReadOnlySet<AssetId> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        var evicted = new List<AssetId>();

        lock (_gate)
        {
            var total = _entries.Values.Sum(entry => entry.Bytes);

            if (total <= _maxBytes)
            {
                return evicted;
            }

            var candidates = _entries
                .Where(pair => !inUse.Contains(pair.Key))
                .OrderBy(pair => pair.Value.LastUsed)
                .ToList();

            foreach (var (id, entry) in candidates)
            {
                if (total <= _maxBytes)
                {
                    break;
                }

                Delete(Path(id, thumbnail: false));
                Delete(Path(id, thumbnail: true));

                _entries.Remove(id);
                total -= entry.Bytes;
                evicted.Add(id);
            }
        }

        return evicted;
    }

    /// <summary>
    /// Written beside and renamed, and a lost rename is <b>success</b> - the same answer the
    /// campaign store needed, arrived at the same way: two downloads of one picture racing each
    /// other made the loser come back with "access to the path is denied".
    /// <para>
    /// The reasoning is one step weaker here than there and worth saying out loud. The name is the
    /// hash of the SOURCE, not of the delivered bytes, so two racing writers are not byte-identical
    /// by construction - an encoder change on the control side would deliver a different encoding
    /// of the same picture. They are nonetheless interchangeable: either is a correct answer to
    /// "the picture for this AssetId", which is the only question this store answers.
    /// </para>
    /// <para>
    /// The scratch file carries a discriminator of its own, so two writers never share one - the
    /// loser must not be able to rename a half-written file into place under a valid identifier.
    /// </para>
    /// </summary>
    private void Put(AssetId id, ReadOnlySpan<byte> bytes, bool thumbnail)
    {
        AtomicFile.WriteContentAddressed(Path(id, thumbnail), bytes, Guid.NewGuid().ToString("N"));

        lock (_gate)
        {
            var entry = _entries.TryGetValue(id, out var known) ? known : new Entry();

            if (thumbnail)
            {
                entry.ThumbnailBytes = bytes.Length;
            }
            else
            {
                entry.OriginalBytes = bytes.Length;
            }

            entry.LastUsed = ++_sequence;
            entry.Adopted = false;
            _entries[id] = entry;
        }
    }

    private bool Take(AssetId id, bool thumbnail, out byte[] bytes)
    {
        var path = Path(id, thumbnail);

        // Asked before it is read, although the catch below would answer just as correctly. A miss
        // is the NORMAL case - every picture the table has not seen yet is one, twice over, once for
        // the thumbnail and once for the original - and going through an exception for it puts a
        // FileNotFoundException in the debugger's output for every new picture. It cost the M2c
        // hand-run a finding that looked like a defect and was none. The race stays covered: the
        // file can still vanish between the question and the answer, which is what the catch is for.
        if (!File.Exists(path))
        {
            bytes = [];

            return false;
        }

        // Read before touching the bookkeeping: a file that is gone from underneath us is not a
        // use, and recording one would keep a phantom alive at the top of the list.
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            bytes = [];

            return false;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.LastUsed = ++_sequence;
                entry.Adopted = false;
            }
        }

        return true;
    }

    /// <summary>
    /// The name is the identifier and nothing else - no extension. The display decodes bytes and
    /// never asks what the file is called, so an extension here would be a second statement about
    /// the format with nothing keeping it true.
    /// <para>
    /// <b>The identifier is checked before it becomes a path</b>, exactly as at the hub's endpoint.
    /// It arrives inside a scene, over the wire - so this end has the same question to answer, and
    /// answering it only at the other end would make the check a property of who asked rather than
    /// of the value (Part 5).
    /// </para>
    /// </summary>
    private string Path(AssetId id, bool thumbnail)
    {
        if (!id.IsWellFormed)
        {
            throw new ArgumentException($"'{id.Value}' is not a well-formed asset identifier.", nameof(id));
        }

        return System.IO.Path.Combine(_directory, thumbnail ? id.Value + ThumbnailSuffix : id.Value);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The ceiling is hygiene, not correctness. A file we cannot remove now is one the next
            // trim tries again, and it is not worth failing a scene over.
        }
    }

    private sealed class Entry
    {
        internal long LastUsed { get; set; }

        /// <summary>Found on disk at start and neither used nor settled in this run yet.</summary>
        internal bool Adopted { get; set; }

        // Long rather than int, because an adopted entry is measured off a FILE. What this store
        // writes cannot exceed the source ceiling, but what it finds was not necessarily written
        // by it, and a cast would turn that into a negative size inside the ceiling.
        internal long OriginalBytes { get; set; }

        internal long ThumbnailBytes { get; set; }

        internal long Bytes => OriginalBytes + ThumbnailBytes;
    }
}
