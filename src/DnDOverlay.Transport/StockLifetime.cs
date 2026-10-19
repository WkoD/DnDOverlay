using DnDOverlay.Core;

namespace DnDOverlay.Transport;

/// <summary>
/// What becomes of a picture store found on disk at start: pruned against the first complete set
/// of scenes, or emptied when no control comes (Part 5, rule 3).
/// <para>
/// <b>No marker file and no crash flag.</b> A store that is still there at start IS the news that
/// the last end was not a regular one - a regular end empties it (rule 1). So nothing has to be
/// remembered across runs, and a cut-off empty and a crash are handled the same way.
/// </para>
/// <para>
/// A type of its own because the decisions in it - when the wait starts, what ends it, that the
/// prune happens once - are exactly the ones a test can only see with a clock it controls.
/// </para>
/// </summary>
public sealed class StockLifetime : IDisposable
{
    /// <summary>
    /// How long a found store waits for a control before it is emptied. Not chosen at random: both
    /// machines are switched on together, and the Surface regularly takes longer to a running
    /// control than a mini PC takes to a running display (Part 5).
    /// </summary>
    public static readonly TimeSpan DefaultPatience = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly AssetCache _cache;
    private readonly TimeProvider _time;
    private readonly TimeSpan _patience;
    private ITimer? _wait;
    private bool _settled;

    public StockLifetime(AssetCache cache, TimeProvider time, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(time);

        _cache = cache;
        _time = time;
        _patience = patience ?? DefaultPatience;
    }

    /// <summary>
    /// Starts the wait, if there is anything to wait about. <paramref name="emptied"/> is told how
    /// many pictures went when the wait runs out - from a timer's thread.
    /// </summary>
    public void Begin(Action<int> emptied)
    {
        ArgumentNullException.ThrowIfNull(emptied);

        lock (_gate)
        {
            if (_settled || _wait is not null || _cache.Adopted == 0)
            {
                return;
            }

            _wait = _time.CreateTimer(
                _ =>
                {
                    lock (_gate)
                    {
                        if (_settled)
                        {
                            return;
                        }

                        _settled = true;
                        _wait?.Dispose();
                        _wait = null;
                    }

                    // Without a control there is nothing to show and so nothing to keep.
                    emptied(_cache.Empty().Count);
                },
                null,
                _patience,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// The first complete set of scenes is in: prunes the found store against it, once. Every call
    /// after the first answers <see langword="null"/> and touches nothing.
    /// </summary>
    /// <param name="inUse">The pictures any item of ANY of this device's screens needs - inactive ones included, because the scene stands there too (Part 3).</param>
    public (int Adopted, int Discarded)? Settle(IReadOnlySet<AssetId> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        lock (_gate)
        {
            if (_settled)
            {
                return null;
            }

            _settled = true;
            _wait?.Dispose();
            _wait = null;
        }

        return _cache.PruneAdopted(inUse);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wait?.Dispose();
            _wait = null;
        }
    }
}
