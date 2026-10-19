using DnDOverlay.Core;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The store ends with the process - unless the process ends hard (Part 5). Three rules, and no
/// marker file: every end empties, newest first; a store found at start is pruned against the first
/// complete set of scenes; and without a control it is emptied after five minutes.
/// </summary>
public sealed class StockLifetimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dndoverlay-stock-" + Guid.NewGuid().ToString("N"));

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
    /// Rule 1: emptying goes newest first, so a cut-off leaves the oldest and least important -
    /// never what was on the table a moment ago. What was merely found from an earlier run goes
    /// last of all.
    /// </summary>
    [Fact]
    public void Emptying_takes_the_most_recently_used_first_and_the_found_last()
    {
        new AssetCache(_directory).Store(Asset(9), Bytes(10, seed: 9));

        var cache = new AssetCache(_directory);

        cache.Store(Asset(1), Bytes(10, seed: 1));
        cache.Store(Asset(2), Bytes(10, seed: 2));
        cache.Store(Asset(3), Bytes(10, seed: 3));
        Assert.True(cache.TryGet(Asset(1), out _));

        Assert.Equal([Asset(1), Asset(3), Asset(2), Asset(9)], cache.Empty(TestContext.Current.CancellationToken));
        Assert.Equal(0, cache.Count);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void An_empty_that_is_told_to_stop_leaves_what_it_had_not_reached()
    {
        var cache = new AssetCache(_directory);

        cache.Store(Asset(1), Bytes(10, seed: 1));

        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.Empty(cache.Empty(stop.Token));
        Assert.True(cache.TryGet(Asset(1), out _));
    }

    /// <summary>
    /// Rule 3: what no item needs goes, original and thumbnail together; what an item needs stays
    /// whole - dropping only the thumbnail would take the next load its quick start.
    /// </summary>
    [Fact]
    public void The_first_scenes_prune_what_was_found_and_keep_what_they_need_whole()
    {
        var before = new AssetCache(_directory);

        foreach (var n in new[] { 1, 2, 3 })
        {
            before.Store(Asset(n), Bytes(10, seed: n));
            before.StoreThumbnail(Asset(n), Bytes(4, seed: n + 10));
        }

        var cache = new AssetCache(_directory);
        using var lifetime = new StockLifetime(cache, new HeldClock());

        Assert.Equal((3, 2), lifetime.Settle(Needed(2)));

        Assert.True(cache.TryGet(Asset(2), out _));
        Assert.True(cache.TryGetThumbnail(Asset(2), out _));
        Assert.False(cache.TryGet(Asset(1), out _));
        Assert.False(cache.TryGetThumbnail(Asset(1), out _));
        Assert.Equal(2, Directory.EnumerateFiles(_directory).Count());
    }

    /// <summary>
    /// The prune is narrower than the running eviction, but only towards what was FOUND: a picture
    /// this run fetched, or one it has already used, is this run's and is not the prune's to take.
    /// </summary>
    [Fact]
    public void The_prune_never_takes_what_this_run_fetched_or_used()
    {
        var before = new AssetCache(_directory);

        before.Store(Asset(1), Bytes(10, seed: 1));
        before.Store(Asset(2), Bytes(10, seed: 2));

        var cache = new AssetCache(_directory);

        Assert.True(cache.TryGet(Asset(1), out _));
        cache.Store(Asset(3), Bytes(10, seed: 3));

        Assert.Equal((1, 1), cache.PruneAdopted(Needed()));
        Assert.True(cache.TryGet(Asset(1), out _));
        Assert.True(cache.TryGet(Asset(3), out _));
        Assert.False(cache.TryGet(Asset(2), out _));
    }

    /// <summary>
    /// Once. A display gets a snapshot after every reconnect; pruning on each would turn the store
    /// into a copy of the current table and cost every picture a second transfer after a hiccup.
    /// </summary>
    [Fact]
    public void The_prune_happens_once()
    {
        new AssetCache(_directory).Store(Asset(1), Bytes(10, seed: 1));

        var cache = new AssetCache(_directory);
        using var lifetime = new StockLifetime(cache, new HeldClock());

        Assert.Equal((1, 0), lifetime.Settle(Needed(1)));
        Assert.Null(lifetime.Settle(Needed()));
        Assert.True(cache.TryGet(Asset(1), out _));
    }

    /// <summary>Without a control there is nothing to show and so nothing to keep - after the wait.</summary>
    [Fact]
    public void A_found_store_with_no_control_is_emptied_after_the_wait()
    {
        new AssetCache(_directory).Store(Asset(1), Bytes(10, seed: 1));

        var cache = new AssetCache(_directory);
        var clock = new HeldClock();
        using var lifetime = new StockLifetime(cache, clock);
        var emptied = -1;

        lifetime.Begin(count => emptied = count);

        Assert.Equal(StockLifetime.DefaultPatience, clock.Due);
        Assert.Equal(-1, emptied);

        clock.Fire();

        Assert.Equal(1, emptied);
        Assert.Equal(0, cache.Count);
    }

    /// <summary>Scenes in time end the wait: the store is pruned, not emptied, and the clock does nothing more.</summary>
    [Fact]
    public void Scenes_before_the_wait_runs_out_end_it()
    {
        new AssetCache(_directory).Store(Asset(1), Bytes(10, seed: 1));

        var cache = new AssetCache(_directory);
        var clock = new HeldClock();
        using var lifetime = new StockLifetime(cache, clock);
        var emptied = -1;

        lifetime.Begin(count => emptied = count);
        lifetime.Settle(Needed(1));
        clock.Fire();

        Assert.Equal(-1, emptied);
        Assert.True(cache.TryGet(Asset(1), out _));
    }

    /// <summary>A clean start has nothing to wait about, and no timer is made.</summary>
    [Fact]
    public void A_start_with_nothing_found_waits_for_nothing()
    {
        var clock = new HeldClock();
        using var lifetime = new StockLifetime(new AssetCache(_directory), clock);

        lifetime.Begin(_ => { });

        Assert.Null(clock.Due);
    }

    private static HashSet<AssetId> Needed(params int[] numbers) => [.. numbers.Select(Asset)];

    private static AssetId Asset(int n) => new(n.ToString(null as IFormatProvider).PadLeft(64, 'a'));

    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);

        return bytes;
    }

    /// <summary>A clock whose one timer fires when the test says so, and not before.</summary>
    private sealed class HeldClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;

        internal TimeSpan? Due { get; private set; }

        internal void Fire() => _callback?.Invoke(_state);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            Due = dueTime;

            return new Held(this);
        }

        private sealed class Held(HeldClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => clock._callback = null;

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}
