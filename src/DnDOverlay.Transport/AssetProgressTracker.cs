using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Transport;

/// <summary>
/// What this device is loading, kept so it can be reported. It is the source the progress ring on
/// the item is fed from (Part 7) - and it lives beside the store rather than in the application,
/// because loading is what it describes.
/// <para>
/// The promises it carries are the ones a ring makes to whoever is watching it: the fraction never
/// goes backwards within an attempt, <b>done</b> means decoded and not "the last byte arrived",
/// and a retry continues the attempt rather than starting over. A ring that jumps back to zero
/// reads as "this is going wrong" when it is merely going slowly.
/// </para>
/// </summary>
public sealed class AssetProgressTracker
{
    private readonly Lock _gate = new();
    private readonly Dictionary<AssetId, Load> _loads = [];

    /// <summary>
    /// A picture that has to be fetched. Calling it a second time does <b>not</b> reset the
    /// fraction - that is the retry case, and the attempt continues.
    /// </summary>
    public void Started(AssetId asset)
    {
        lock (_gate)
        {
            if (!_loads.ContainsKey(asset))
            {
                _loads[asset] = new Load();
            }
        }
    }

    /// <summary>
    /// A request for this picture is going out now. It is the moment the ring at the table starts
    /// to mean something - before it, the picture is only on the list.
    /// </summary>
    public void Fetching(AssetId asset)
    {
        lock (_gate)
        {
            if (_loads.TryGetValue(asset, out var load) && load.State is AssetLoadState.Waiting)
            {
                load.State = AssetLoadState.Loading;
            }
        }
    }

    /// <summary>
    /// A picture that was already in the store. Reported as finished <b>at once</b> and without a
    /// request ever going out - the ring must not appear for a picture nobody is waiting for
    /// (Part 5, Part 11).
    /// </summary>
    public void AlreadyHere(AssetId asset) => Set(asset, 1, AssetLoadState.Done);

    /// <summary>
    /// How much has arrived. A total of zero or less means the counterpart did not say how big the
    /// picture is - then the fraction stays where it was rather than being invented, because a
    /// ring guessing is worse than a ring waiting.
    /// </summary>
    public void Received(AssetId asset, long bytes, long total)
    {
        lock (_gate)
        {
            if (!_loads.TryGetValue(asset, out var load) || load.State is AssetLoadState.Done)
            {
                return;
            }

            if (total > 0)
            {
                // Never backwards: a resumed or retried transfer reports the high-water mark, so
                // the ring keeps what it had.
                load.Fraction = Math.Max(load.Fraction, Math.Clamp((double)bytes / total, 0, 1));
            }

            load.State = AssetLoadState.Loading;
        }
    }

    /// <summary>All bytes are in, the delivered hash is being checked.</summary>
    public void Verifying(AssetId asset) => Advance(asset, AssetLoadState.Verifying);

    /// <summary>
    /// Being decoded. This is the step that keeps <b>done</b> honest: on a large picture it costs
    /// real time, and a ring that filled with the last byte would sit full while nothing was on
    /// the screen yet (Part 11).
    /// </summary>
    public void Decoding(AssetId asset) => Advance(asset, AssetLoadState.Decoding);

    /// <summary>Drawable. Only now.</summary>
    public void Done(AssetId asset) => Set(asset, 1, AssetLoadState.Done);

    /// <summary>
    /// Finally unsuccessful - a state of its own, not a ring that stops. The fraction is left where
    /// it stopped, because how far it got is the useful part of the report.
    /// </summary>
    public void Failed(AssetId asset)
    {
        lock (_gate)
        {
            var load = _loads.TryGetValue(asset, out var known) ? known : new Load();

            load.State = AssetLoadState.Failed;
            _loads[asset] = load;
        }
    }

    /// <summary>
    /// Takes what has finished or failed out of the report. Called after a reading has gone out:
    /// a final state is worth saying once, and the next reading is about what is still running.
    /// </summary>
    public void Settle()
    {
        lock (_gate)
        {
            foreach (var (asset, load) in _loads.ToList())
            {
                if (load.State is AssetLoadState.Done or AssetLoadState.Failed)
                {
                    _loads.Remove(asset);
                }
            }
        }
    }

    /// <summary>
    /// The reading to send, or <see langword="null"/> when there is nothing to report.
    /// <para>
    /// Null rather than an empty list, and that is the point: with an empty load list a device
    /// sends <b>nothing at all</b>, which is the normal case for a table where nothing is
    /// happening (Part 4). An empty message every few hundred milliseconds would be traffic whose
    /// only content is that there is no content.
    /// </para>
    /// </summary>
    public AssetProgressMessage? Reading()
    {
        lock (_gate)
        {
            if (_loads.Count == 0)
            {
                return null;
            }

            return new AssetProgressMessage(
                [.. _loads.Select(pair => new AssetLoad(pair.Key, pair.Value.Fraction, pair.Value.State))]);
        }
    }

    private void Advance(AssetId asset, AssetLoadState state)
    {
        lock (_gate)
        {
            if (_loads.TryGetValue(asset, out var load) && load.State is not AssetLoadState.Done)
            {
                load.State = state;
            }
        }
    }

    private void Set(AssetId asset, double fraction, AssetLoadState state)
    {
        lock (_gate)
        {
            var load = _loads.TryGetValue(asset, out var known) ? known : new Load();

            load.Fraction = fraction;
            load.State = state;
            _loads[asset] = load;
        }
    }

    private sealed class Load
    {
        internal double Fraction { get; set; }

        internal AssetLoadState State { get; set; } = AssetLoadState.Waiting;
    }
}
