using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using DnDOverlay.Core;

namespace DnDOverlay.Transport;

/// <summary>
/// What one load run came to: how many pictures were fetched, how many were already here, how many
/// bytes crossed the wire, how many requests were in flight at the busiest moment, and how long it
/// all took.
/// <para>
/// It exists because of a gap the M2c hand-run found. The only duration anywhere was the control's
/// <c>2001</c>, which measures hashing, decoding and storing - the ingest - and reads "0 ms" for a
/// small picture. Nothing measured the part the DM actually waits for, so the one number for "a
/// picture stands within N seconds" was a stopwatch in somebody's hand (Part 5, Part 11).
/// </para>
/// <para>
/// <paramref name="Peak"/> is per RUN rather than the loader's lifetime peak, because that is the
/// question being asked: did this evening's pictures really go three at a time, or did they queue
/// up one behind the other and only look parallel?
/// </para>
/// </summary>
public sealed record AssetLoadRun(
    int Fetched,
    int AlreadyHere,
    long Bytes,
    int Peak,
    long Milliseconds,
    IReadOnlyList<AssetLoadFailure> Failed);

/// <summary>
/// One picture that did not arrive, with what the wire said about it.
/// <para>
/// It travels in the report rather than being logged here, because this library has no logger and
/// should not get one for it: a download failure is a sentence about the SESSION, and the sentence
/// belongs where the names are - only the display knows that this hash is "Dilwyn Kemri" (Part 8).
/// </para>
/// <para>
/// It is carried at all because it was lost. Until M2b the display fetched in its own code and said
/// so when a fetch failed; the loader took the fetching and not the saying, and from then on a
/// picture that never came was silent in the log - the ring showed it and nothing else did. The M2c
/// hand-run only saw those 401 lines because the surface was running the older build.
/// </para>
/// </summary>
public sealed record AssetLoadFailure(AssetId Asset, string Detail);

/// <summary>What a scene needs, and how badly.</summary>
/// <param name="IsBackground">
/// The background goes first. It is the layer everything else lies on, and a table that gets its
/// portraits before its map has been assembled in the wrong order in front of everyone (Part 5).
/// </param>
public sealed record AssetWanted(AssetId Asset, AssetMeta Meta, bool IsBackground = false);

/// <summary>
/// One picture's bytes, ready to be decoded by whoever can.
/// </summary>
/// <param name="IsThumbnail">
/// A thumbnail arrives first and is deliberately handed over on its own: it lets the picture STAND
/// at its place within a second, blurred, while the full one is still coming (Part 5, Part 10).
/// The original follows as a second arrival for the same asset.
/// </param>
public sealed record AssetArrived(AssetId Asset, byte[] Bytes, bool IsThumbnail);

/// <summary>
/// The load path: what a scene needs, fetched in the right order, checked, stored, and reported.
/// <para>
/// It lives here rather than in the display for the reason the display keeps proving: the loop in
/// <c>App.xaml.cs</c> is where nothing can be tested, and that is where a display once stopped
/// reconnecting for good without a word. What stays with the application is decoding and drawing -
/// the two things that genuinely need a window.
/// </para>
/// <para>
/// <b>Bytes only.</b> The same boundary <see cref="AssetClient"/> and <see cref="AssetCache"/> keep,
/// and for the same two costed promises (Part 2, Part 9).
/// </para>
/// </summary>
public sealed class AssetLoader
{
    /// <summary>
    /// Part 6's number. Twenty items fetched at once are twenty pictures that are all slow: after
    /// ten seconds not one of them is there. Three at a time is the same volume and a different
    /// evening - the first picture stands after a second (Part 5).
    /// </summary>
    public const int DefaultMaxConcurrent = 3;

    /// <summary>The width asked of the stock. A wish - what comes back is the step it holds.</summary>
    private const int ThumbnailWidth = 256;

    private readonly Lock _peak = new();
    private readonly AssetClient _client;
    private readonly AssetCache _cache;
    private readonly AssetProgressTracker _progress;
    private readonly int _maxConcurrent;

    public AssetLoader(
        AssetClient client,
        AssetCache cache,
        AssetProgressTracker progress,
        int maxConcurrent = DefaultMaxConcurrent)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrent);

        _client = client;
        _cache = cache;
        _progress = progress;
        _maxConcurrent = maxConcurrent;
    }

    /// <summary>The most downloads that were in flight at once. For the test that says three is three.</summary>
    public int PeakConcurrency { get; private set; }

    /// <summary>
    /// Fetches everything <paramref name="wanted"/> names that is not already here, and writes each
    /// picture into <paramref name="arrivals"/> as it becomes available.
    /// <para>
    /// A picture already in the store arrives at once and <b>no request goes out for it</b> - which
    /// is the whole of "transferred once per device" (Part 5). One that has to come down is reported
    /// as it comes, checked against its <see cref="AssetMeta.ContentHash"/>, and only then stored.
    /// </para>
    /// <para>
    /// A picture that fails does not take the others with it. It ends on
    /// <see cref="Core.Protocol.AssetLoadState.Failed"/> and the scene is drawn without it - a table
    /// missing one portrait beats a table missing everything.
    /// </para>
    /// </summary>
    /// <returns>
    /// What this run amounted to - the numbers the M2c hand-run had to take with a stopwatch,
    /// because the only duration anywhere was the control's ingest and that says nothing about the
    /// wire (see <see cref="AssetLoadRun"/>).
    /// </returns>
    public async Task<AssetLoadRun> LoadAsync(
        Uri hubBaseAddress,
        string assetPath,
        IReadOnlyList<AssetWanted> wanted,
        string token,
        ChannelWriter<AssetArrived> arrivals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(arrivals);

        var started = Stopwatch.GetTimestamp();
        var run = new Tally();

        // Background first, and otherwise the order the scene gave. Distinct, because two items on
        // one picture are one download.
        var order = wanted
            .DistinctBy(item => item.Asset)
            .OrderByDescending(item => item.IsBackground)
            .ToList();

        // Announced BEFORE anything is fetched, so the first reading already carries every picture
        // the table is waiting for. Reporting them as each begins would have the ring appear one at
        // a time and read as "nothing else is coming".
        foreach (var item in order)
        {
            if (Held(item.Asset))
            {
                _progress.AlreadyHere(item.Asset);
            }
            else
            {
                _progress.Started(item.Asset);
            }
        }

        using var slots = new SemaphoreSlim(_maxConcurrent);
        var inFlight = 0;

        var running = order.Select(async item =>
        {
            if (Held(item.Asset))
            {
                await DeliverAsync(item.Asset, arrivals, cancellationToken).ConfigureAwait(false);

                return;
            }

            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Note(run, Interlocked.Increment(ref inFlight));

                await FetchAsync(hubBaseAddress, assetPath, item, token, arrivals, run, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
                slots.Release();
            }
        });

        await Task.WhenAll(running).ConfigureAwait(false);

        return new AssetLoadRun(
            run.Fetched,
            order.Count - run.Fetched,
            run.Bytes,
            run.Peak,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            run.Failed);
    }

    private bool Held(AssetId asset) => _cache.TryGet(asset, out _);

    private void Note(Tally run, int inFlight)
    {
        lock (_peak)
        {
            PeakConcurrency = Math.Max(PeakConcurrency, inFlight);
            run.Peak = Math.Max(run.Peak, inFlight);
        }
    }

    /// <summary>
    /// What one run has come to so far. A class rather than counters in the loop, because the loop
    /// body runs on several threads at once - the two counts go through Interlocked, and the peak
    /// through the lock that already guards it.
    /// </summary>
    private sealed class Tally
    {
        internal readonly List<AssetLoadFailure> Failed = [];

        internal int Fetched;
        internal long Bytes;
        internal int Peak;

        internal void Note(AssetId asset, string detail)
        {
            lock (Failed)
            {
                Failed.Add(new AssetLoadFailure(asset, detail));
            }
        }
    }

    /// <summary>Straight out of the store - no request, and finished the moment it is handed over.</summary>
    private async Task DeliverAsync(
        AssetId asset, ChannelWriter<AssetArrived> arrivals, CancellationToken cancellationToken)
    {
        if (_cache.TryGetThumbnail(asset, out var thumbnail))
        {
            await arrivals.WriteAsync(new AssetArrived(asset, thumbnail, IsThumbnail: true), cancellationToken)
                .ConfigureAwait(false);
        }

        if (_cache.TryGet(asset, out var bytes))
        {
            await arrivals.WriteAsync(new AssetArrived(asset, bytes, IsThumbnail: false), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task FetchAsync(
        Uri hubBaseAddress,
        string assetPath,
        AssetWanted item,
        string token,
        ChannelWriter<AssetArrived> arrivals,
        Tally run,
        CancellationToken cancellationToken)
    {
        try
        {
            // The thumbnail first, and its failure is not the picture's failure: a stock that holds
            // no thumbnail for this asset is a slower start, not a missing picture.
            try
            {
                var thumbnail = await _client
                    .GetThumbnailAsync(hubBaseAddress, assetPath, item.Asset, ThumbnailWidth, token, cancellationToken)
                    .ConfigureAwait(false);

                _cache.StoreThumbnail(item.Asset, thumbnail);
                Interlocked.Add(ref run.Bytes, thumbnail.Length);

                await arrivals
                    .WriteAsync(new AssetArrived(item.Asset, thumbnail, IsThumbnail: true), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Slower, not broken.
            }

            var bytes = await _client
                .GetReportingAsync(
                    hubBaseAddress, assetPath, item.Asset, token,
                    (received, total) => _progress.Received(item.Asset, received, total),
                    cancellationToken)
                .ConfigureAwait(false);

            // Identity and integrity are two questions, and this is where the second one is asked:
            // the file name carries the SOURCE hash, so without this check the display could not
            // verify at all (Part 5). It is the M2b half of that split.
            _progress.Verifying(item.Asset);

            if (!Matches(bytes, item.Meta.ContentHash))
            {
                _progress.Failed(item.Asset);
                run.Note(item.Asset, "The delivered bytes do not match the hash the scene carries.");

                return;
            }

            _cache.Store(item.Asset, bytes);

            // Counted where it ARRIVED, not where it was asked for: a picture that failed its hash
            // check or never came is not volume that reached this device, and counting it would make
            // the reading flatter than the evening was.
            Interlocked.Add(ref run.Bytes, bytes.Length);
            Interlocked.Increment(ref run.Fetched);

            // Decoding is the caller's, and it is not free - so the state says so, and DONE is left
            // for whoever decoded it (Part 11).
            _progress.Decoding(item.Asset);

            await arrivals
                .WriteAsync(new AssetArrived(item.Asset, bytes, IsThumbnail: false), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException failure)
        {
            _progress.Failed(item.Asset);
            run.Note(item.Asset, failure.Message);
        }
    }

    /// <summary>
    /// The delivered bytes against the hash the item carries. An empty hash means the sender made no
    /// claim - then there is nothing to check, and refusing would turn a missing statement into a
    /// missing picture.
    /// </summary>
    private static bool Matches(byte[] bytes, string contentHash) =>
        string.IsNullOrEmpty(contentHash)
        || Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(contentHash, StringComparison.OrdinalIgnoreCase);
}
