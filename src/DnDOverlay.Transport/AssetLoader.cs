using System.Security.Cryptography;
using System.Threading.Channels;
using DnDOverlay.Core;

namespace DnDOverlay.Transport;

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
    public async Task LoadAsync(
        Uri hubBaseAddress,
        string assetPath,
        IReadOnlyList<AssetWanted> wanted,
        string token,
        ChannelWriter<AssetArrived> arrivals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(arrivals);

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
                Note(Interlocked.Increment(ref inFlight));

                await FetchAsync(hubBaseAddress, assetPath, item, token, arrivals, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
                slots.Release();
            }
        });

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private bool Held(AssetId asset) => _cache.TryGet(asset, out _);

    private void Note(int inFlight)
    {
        lock (_peak)
        {
            PeakConcurrency = Math.Max(PeakConcurrency, inFlight);
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

                return;
            }

            _cache.Store(item.Asset, bytes);

            // Decoding is the caller's, and it is not free - so the state says so, and DONE is left
            // for whoever decoded it (Part 11).
            _progress.Decoding(item.Asset);

            await arrivals
                .WriteAsync(new AssetArrived(item.Asset, bytes, IsThumbnail: false), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            _progress.Failed(item.Asset);
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
