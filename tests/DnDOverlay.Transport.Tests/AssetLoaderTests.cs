using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The load path on its own: what order it asks in, how many at a time, what it does with a picture
/// it already has, and what it refuses.
/// <para>
/// The counterpart here is a stand-in, deliberately - the subject is the ORDERING and the
/// bookkeeping, not whether this end and the hub agree. That question has its own test with the
/// real hub next door, because a stand-in agrees with whoever wrote it (checks/M2.md).
/// </para>
/// </summary>
public sealed class AssetLoaderTests : IDisposable
{
    private static readonly Uri Hub = new("http://127.0.0.1:9/");
    private const string Path = "/assets";
    private const string Token = "a-token";

    private readonly string _directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "dndoverlay-loader-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>
    /// The background is the layer everything else lies on. A table that gets its portraits before
    /// its map has been assembled in the wrong order in front of everyone (Part 5).
    /// </summary>
    [Fact]
    public async Task The_background_is_asked_for_first()
    {
        var stub = new Counterpart();
        var loader = Loader(stub);

        await LoadAsync(loader, [Want(1), Want(2), Want(3, background: true)]);

        // Thumbnails and originals interleave, so what is compared is which ASSET came first.
        Assert.Equal(Asset(3), stub.Asked[0].Asset);
    }

    /// <summary>
    /// Three at a time. Twenty at once are twenty pictures that are all slow, and after ten seconds
    /// not one of them is there; three is the same volume and a different evening (Part 5, Part 6).
    /// </summary>
    [Fact]
    public async Task No_more_than_the_allowed_downloads_run_at_once()
    {
        var stub = new Counterpart { Dwell = TimeSpan.FromMilliseconds(60) };
        var loader = Loader(stub);

        await LoadAsync(loader, [.. Enumerable.Range(1, 9).Select(n => Want(n))]);

        Assert.Equal(3, loader.PeakConcurrency);
    }

    /// <summary>
    /// A parked picture goes last. It lies in the slot bar at the edge, tidied away by the players
    /// themselves - the one thing on the table nobody is currently looking at, so it must not be in
    /// front of the portrait the DM has just sent (Part 11).
    /// </summary>
    [Fact]
    public async Task A_parked_picture_is_asked_for_last()
    {
        var stub = new Counterpart();
        var loader = Loader(stub);

        await LoadAsync(loader, [Want(1, parked: true), Want(2), Want(3, background: true)]);

        Assert.Equal(
            [Asset(3), Asset(2), Asset(1)],
            [.. stub.Asked.Select(asked => asked.Asset).Distinct()]);
    }

    /// <summary>
    /// <b>While a hand is on the table, downloads drop to one at a time.</b> The gesture beats new
    /// pictures - first rule of the order of precedence, and the one the players notice (Part 1).
    /// </summary>
    [Fact]
    public async Task While_somebody_is_pushing_a_picture_only_one_download_runs()
    {
        var stub = new Counterpart { Dwell = TimeSpan.FromMilliseconds(60) };
        var loader = Loader(stub, busy: () => true);

        await LoadAsync(loader, [.. Enumerable.Range(1, 6).Select(n => Want(n))]);

        Assert.Equal(1, loader.PeakConcurrency);
    }

    /// <summary>
    /// And it is asked per picture rather than once per run: a gesture that ends must not keep the
    /// rest of the run waiting behind a decision taken before it began.
    /// </summary>
    [Fact]
    public async Task A_gesture_that_ends_gives_the_downloads_their_slots_back()
    {
        var stub = new Counterpart { Dwell = TimeSpan.FromMilliseconds(60) };
        var pushing = true;
        var loader = Loader(stub, busy: () => pushing);

        var wanted = Enumerable.Range(1, 9).Select(n => Want(n)).ToList();

        var run = LoadAsync(loader, wanted);

        // Long enough that the first pictures went down one at a time, then the hand leaves.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        pushing = false;

        await run;

        Assert.Equal(3, loader.PeakConcurrency);
    }

    /// <summary>
    /// A run says what it came to, and the numbers are about THIS run.
    /// <para>
    /// The M2c hand-run had to time the pictures by hand, because the only duration in either
    /// process was the control's ingest - which reads "0 ms" for a small file and never touched the
    /// wire. What is checked here is the part a hand-run cannot check: that what is counted is what
    /// arrived, that a picture already in the store is counted as such rather than as traffic, and
    /// that the peak belongs to this run.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_run_reports_what_it_came_to()
    {
        var stub = new Counterpart { Dwell = TimeSpan.FromMilliseconds(30) };
        var loader = Loader(stub);
        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        var run = await loader.LoadAsync(
            Hub, Path, [.. Enumerable.Range(1, 6).Select(n => Want(n))], Token,
            arrivals.Writer, TestContext.Current.CancellationToken);

        arrivals.Writer.Complete();

        Assert.Equal(6, run.Fetched);
        Assert.Equal(0, run.AlreadyHere);
        Assert.Equal(3, run.Peak);

        // Both halves of every picture crossed the wire, and nothing else did.
        var carried = 0L;

        await foreach (var arrived in arrivals.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            carried += arrived.Bytes.Length;
        }

        Assert.Equal(carried, run.Bytes);

        // Six pictures, three at a time, thirty milliseconds each: the run cannot have been
        // instantaneous, and saying so is what makes the number a measurement rather than a field.
        Assert.True(run.Milliseconds >= 30, $"the run reported {run.Milliseconds} ms");
    }

    /// <summary>
    /// The whole of "transferred once per device": a picture in the store is handed over at once
    /// and <b>no request goes out for it</b> (Part 5).
    /// </summary>
    [Fact]
    public async Task A_picture_already_in_the_store_costs_no_request()
    {
        var stub = new Counterpart();
        var cache = new AssetCache(_directory);
        var progress = new AssetProgressTracker();

        cache.Store(Asset(1), Body(1));

        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, progress);

        var arrived = await LoadAsync(loader, [Want(1)]);

        Assert.Empty(stub.Asked);
        Assert.Equal(Asset(1), Assert.Single(arrived).Asset);
        Assert.Equal(AssetLoadState.Done, Assert.Single(progress.Reading()!.Loads).State);
    }

    /// <summary>
    /// The thumbnail is handed over on its own and first - it is what lets the picture STAND at its
    /// place within a second, blurred, while the full one is still coming (Part 5, Part 10).
    /// </summary>
    [Fact]
    public async Task The_thumbnail_arrives_before_the_original()
    {
        var stub = new Counterpart();
        var loader = Loader(stub);

        var arrived = await LoadAsync(loader, [Want(1)]);

        Assert.Equal([true, false], arrived.Select(item => item.IsThumbnail));
    }

    /// <summary>
    /// Identity and integrity are two questions (Part 5). The file name carries the SOURCE hash, so
    /// this is the only place the DELIVERED bytes are ever checked - the half of that split that
    /// was recorded as M2b.
    /// </summary>
    [Fact]
    public async Task Bytes_that_do_not_match_their_content_hash_are_refused()
    {
        var stub = new Counterpart();
        var cache = new AssetCache(_directory);
        var progress = new AssetProgressTracker();
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, progress);

        var wanted = new AssetWanted(Asset(1), Meta(new string('f', 64)));

        var arrived = await LoadAsync(loader, [wanted]);

        Assert.DoesNotContain(arrived, item => !item.IsThumbnail);
        Assert.False(cache.TryGet(Asset(1), out _));
        Assert.Equal(AssetLoadState.Failed, Assert.Single(progress.Reading()!.Loads).State);
    }

    /// <summary>
    /// One picture failing does not take the others with it. A table missing one portrait beats a
    /// table missing everything.
    /// </summary>
    [Fact]
    public async Task One_picture_that_cannot_be_had_does_not_stop_the_rest()
    {
        var stub = new Counterpart { Missing = { Asset(2) } };
        var cache = new AssetCache(_directory);
        var progress = new AssetProgressTracker();
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, progress);

        var arrived = await LoadAsync(loader, [Want(1), Want(2), Want(3)]);

        Assert.Equal(
            [Asset(1), Asset(3)],
            arrived
                .Where(item => !item.IsThumbnail)
                .Select(item => item.Asset)
                .OrderBy(asset => asset.Value, StringComparer.Ordinal));

        var failed = progress.Reading()!.Loads.Single(load => load.State == AssetLoadState.Failed);
        Assert.Equal(Asset(2), failed.Asset);
    }

    /// <summary>
    /// What did not arrive comes back NAMED, so that somebody can say it.
    /// <para>
    /// Until M2b the display fetched in its own code and logged a failed fetch itself. The loader
    /// took over the fetching and not the saying, and from then on a picture that never came was
    /// silent in the log - the ring showed it, and nothing else did. The M2c hand-run only saw those
    /// 401 lines because the surface was still running the older build, which is the opposite of
    /// reassuring.
    /// </para>
    /// <para>
    /// It comes back rather than being logged here because this library has no logger and should
    /// not get one: only the display knows that a hash is "Dilwyn Kemri", and the name is the whole
    /// point of the line (Part 8).
    /// </para>
    /// </summary>
    [Fact]
    public async Task What_did_not_arrive_comes_back_with_it()
    {
        var stub = new Counterpart { Missing = { Asset(2) } };
        var cache = new AssetCache(_directory);
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, new AssetProgressTracker());
        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        var run = await loader.LoadAsync(
            Hub, Path, [Want(1), Want(2), Want(3)], Token, arrivals.Writer,
            TestContext.Current.CancellationToken);

        arrivals.Writer.Complete();

        Assert.Equal(Asset(2), Assert.Single(run.Failed).Asset);
        Assert.NotEmpty(run.Failed[0].Detail);

        // The other two are not failures, and the count says so rather than the absence of a name.
        Assert.Equal(2, run.Fetched);

        // And the one that failed is not counted as one that was already here. Nothing was in the
        // store at all - the line 3020 writes would otherwise have said "1 already here" directly
        // above the 3005 naming that same picture as missing.
        Assert.Equal(0, run.AlreadyHere);
    }

    /// <summary>
    /// The three counts of a run add up to what was asked for, and each picture lands in exactly
    /// one of them. Written as the sum rather than as three separate numbers: the fault this
    /// replaces was a subtraction that silently put failures into "already here", which no single
    /// count could have shown.
    /// </summary>
    [Fact]
    public async Task Fetched_already_here_and_failed_account_for_every_picture()
    {
        var stub = new Counterpart { Missing = { Asset(2) } };
        var cache = new AssetCache(_directory);
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, new AssetProgressTracker());
        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        // One in the store, one that cannot be had, two ordinary ones.
        cache.Store(Asset(4), Body(4));

        var run = await loader.LoadAsync(
            Hub, Path, [Want(1), Want(2), Want(3), Want(4)], Token, arrivals.Writer,
            TestContext.Current.CancellationToken);

        arrivals.Writer.Complete();

        Assert.Equal(2, run.Fetched);
        Assert.Equal(1, run.AlreadyHere);
        Assert.Single(run.Failed);
        Assert.Equal(4, run.Fetched + run.AlreadyHere + run.Failed.Count);
    }

    /// <summary>
    /// A picture whose bytes do not match the hash the scene carries fails with a sentence of its
    /// own. It is not a network fault and must not read like one: the bytes arrived, and they were
    /// the wrong ones (Part 5).
    /// </summary>
    [Fact]
    public async Task Bytes_that_fail_their_hash_are_named_as_that()
    {
        var stub = new Counterpart();
        var cache = new AssetCache(_directory);
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, new AssetProgressTracker());
        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        var run = await loader.LoadAsync(
            Hub, Path, [new AssetWanted(Asset(1), Meta(new string('f', 64)))], Token, arrivals.Writer,
            TestContext.Current.CancellationToken);

        arrivals.Writer.Complete();

        Assert.Contains("hash", Assert.Single(run.Failed).Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every picture the table is waiting for is in the FIRST reading, before anything is fetched.
    /// Announcing them as each begins would have the rings appear one at a time, which reads as
    /// "nothing else is coming" (Part 7).
    /// </summary>
    [Fact]
    public async Task Every_picture_being_waited_for_is_in_the_first_reading()
    {
        var stub = new Counterpart { Dwell = TimeSpan.FromMilliseconds(150) };
        var cache = new AssetCache(_directory);
        var progress = new AssetProgressTracker();
        var loader = new AssetLoader(new AssetClient(new HttpClient(stub)), cache, progress);

        var arrivals = Channel.CreateUnbounded<AssetArrived>();
        var running = loader.LoadAsync(
            Hub, Path, [Want(1), Want(2), Want(3)], Token, arrivals.Writer, TestContext.Current.CancellationToken);

        await stub.FirstRequest;

        Assert.Equal(3, progress.Reading()!.Loads.Count);

        await running;
    }

    private AssetLoader Loader(Counterpart stub, Func<bool>? busy = null) =>
        new(
            new AssetClient(new HttpClient(stub)),
            new AssetCache(_directory),
            new AssetProgressTracker(),
            busy: busy);

    private static async Task<List<AssetArrived>> LoadAsync(
        AssetLoader loader, IReadOnlyList<AssetWanted> wanted)
    {
        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        await loader.LoadAsync(
            Hub, Path, wanted, Token, arrivals.Writer, TestContext.Current.CancellationToken);

        arrivals.Writer.Complete();

        var collected = new List<AssetArrived>();

        await foreach (var arrived in arrivals.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            collected.Add(arrived);
        }

        return collected;
    }

    private static AssetWanted Want(int n, bool background = false, bool parked = false) =>
        new(Asset(n), Meta(Convert.ToHexStringLower(SHA256.HashData(Body(n)))), background, parked);

    private static AssetMeta Meta(string contentHash) =>
        new(64, 64, "png", Bytes: 64, IsAnimated: false, ContentHash: contentHash);

    private static AssetId Asset(int n) =>
        new(n.ToString(null as IFormatProvider).PadLeft(64, 'a'));

    private static byte[] Body(int n) => [.. Enumerable.Repeat((byte)n, 64)];

    /// <summary>
    /// Stands in for the hub. It answers thumbnails and originals, can be told to dwell so
    /// concurrency is observable, and can be told a picture is missing.
    /// </summary>
    private sealed class Counterpart : HttpMessageHandler
    {
        private readonly TaskCompletionSource _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Lock _gate = new();

        internal List<(AssetId Asset, bool Thumbnail)> Asked { get; } = [];

        internal HashSet<AssetId> Missing { get; } = [];

        internal TimeSpan Dwell { get; set; }

        internal Task FirstRequest => _first.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var thumbnail = path.EndsWith("/thumb", StringComparison.Ordinal);
            var id = new AssetId(path.Replace("/thumb", string.Empty, StringComparison.Ordinal).Split('/')[^1]);

            lock (_gate)
            {
                Asked.Add((id, thumbnail));
            }

            _first.TrySetResult();

            if (Dwell > TimeSpan.Zero)
            {
                await Task.Delay(Dwell, cancellationToken);
            }

            if (Missing.Contains(id))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var number = int.Parse(id.Value.TrimStart('a'), null as IFormatProvider);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(thumbnail ? [1, 2, 3] : Body(number)),
            };
        }
    }
}
