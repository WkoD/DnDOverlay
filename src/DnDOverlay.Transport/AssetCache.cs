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
/// <b>In M2 the store is bound to the session.</b> Emptying it on exit, adopting it after a crash
/// and trimming it with the first scene are M5a - and they are what will need bookkeeping that
/// outlives the process. Today it is held in memory, and a store found at start is simply unknown
/// to it.
/// </para>
/// </summary>
public sealed class AssetCache
{
    /// <summary>Part 6's number for the whole device, and it is handed in so a test can shrink it.</summary>
    public const long DefaultMaxBytes = 4L * 1024 * 1024 * 1024;

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
            _entries[id] = entry;
        }
    }

    private bool Take(AssetId id, bool thumbnail, out byte[] bytes)
    {
        var path = Path(id, thumbnail);

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

        return System.IO.Path.Combine(_directory, thumbnail ? id.Value + ".thumb" : id.Value);
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

        internal int OriginalBytes { get; set; }

        internal int ThumbnailBytes { get; set; }

        internal long Bytes => OriginalBytes + ThumbnailBytes;
    }
}
