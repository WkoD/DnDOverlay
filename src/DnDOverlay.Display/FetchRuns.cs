using System.Collections.Concurrent;
using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The picture fetches of each screen: <b>one run at a time per screen, and a second ask while one is
/// going does not queue a second run</b> - it marks the running one to look again when it is done.
/// <para>
/// Queueing would give twenty arriving pictures twenty runs of one picture each, which is exactly the
/// shape that kept the loader's three-at-a-time from ever engaging (<c>Peak=1</c> in every run of every
/// hand-run before it). Looking again instead means the next round sees everything still missing and
/// fetches it together.
/// </para>
/// <para>
/// Asked from the connection task and from the end of a run on another thread - concurrent for both.
/// An ask that lands between a run's last look and its letting go is not lost: the run says so when
/// it finishes, and the caller starts the next one.
/// </para>
/// </summary>
internal sealed class FetchRuns
{
    private readonly ConcurrentDictionary<ScreenId, byte> _running = new();
    private readonly ConcurrentDictionary<ScreenId, byte> _again = new();

    /// <summary>
    /// Asks for a fetch: <see langword="true"/> when the caller has to start the run, <see langword="false"/>
    /// when one is going and has been marked to look again.
    /// </summary>
    internal bool Ask(ScreenId screen)
    {
        if (_running.TryAdd(screen, 0))
        {
            return true;
        }

        _again[screen] = 0;

        return false;
    }

    /// <summary>A round of the run begins: what was asked so far, it is about to see.</summary>
    internal void Looking(ScreenId screen) => _ = _again.TryRemove(screen, out _);

    /// <summary>Whether the screen was asked for again since the round began - the run goes round once more.</summary>
    internal bool AskedAgain(ScreenId screen) => _again.ContainsKey(screen);

    /// <summary>
    /// The run lets go of the screen. <see langword="true"/> when an ask came in between its last look
    /// and now - a window of a few instructions, and therefore certain to happen eventually - so the
    /// caller has to ask again, or a picture would never arrive.
    /// </summary>
    internal bool Finished(ScreenId screen)
    {
        _ = _running.TryRemove(screen, out _);

        return _again.ContainsKey(screen);
    }
}
