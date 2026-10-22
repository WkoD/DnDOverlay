namespace DnDOverlay.Core;

/// <summary>What one command did to one screen: the items it laid, the items it took, and the background before and after.</summary>
public sealed record ScreenChange(
    ScreenRef Screen,
    IReadOnlyList<SceneItem> Added,
    IReadOnlyList<SceneItem> Removed,
    bool BackgroundChanged,
    BackgroundItem? BackgroundBefore,
    BackgroundItem? BackgroundAfter);

/// <summary>
/// One step of the timeline (Part 4): one act of the DM, with what it changed on every screen it
/// touched - a single entry for "clear all", "send to all" or a layout over four screens.
/// <para>
/// <b>The counter-move is surgical, and that is the whole design</b> (siting question 3, test
/// question 7). A step holds only the items the command <i>added</i> and the items it <i>removed</i>,
/// and the background if it changed - never an item that was there before and after. Undoing takes
/// away what was added and brings back what was removed, and touches nothing else: a picture the
/// players have pushed around since stays exactly where they left it, whether they moved it at the
/// table or the DM did in the thumbnail. A snapshot of the whole screen would have put it back.
/// </para>
/// <para>
/// The ops it produces are ordinary patch operations, so every patch rule holds: an operation on an
/// item that is gone meanwhile does nothing (Part 3) - which is exactly the case of the DM having
/// removed it since.
/// </para>
/// </summary>
public sealed record TimelineStep(string Label, IReadOnlyList<ScreenChange> Changes)
{
    /// <summary>
    /// The step between two pictures of the table, or none when the command changed nothing a step
    /// is made of - a move onto the screen something already lies on, a background set to itself.
    /// </summary>
    public static TimelineStep? Between(
        string label,
        IReadOnlyDictionary<ScreenRef, SceneState> before,
        IReadOnlyDictionary<ScreenRef, SceneState> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<ScreenChange>();

        foreach (var screen in before.Keys.Concat(after.Keys).Distinct())
        {
            var was = before.GetValueOrDefault(screen) ?? SceneState.Empty;
            var now = after.GetValueOrDefault(screen) ?? SceneState.Empty;
            var wasIds = was.Items.Select(item => item.ItemId).ToHashSet();
            var nowIds = now.Items.Select(item => item.ItemId).ToHashSet();
            var added = now.Items.Where(item => !wasIds.Contains(item.ItemId)).ToList();
            var removed = was.Items.Where(item => !nowIds.Contains(item.ItemId)).ToList();
            var background = !Equals(was.Background, now.Background);

            if (added.Count > 0 || removed.Count > 0 || background)
            {
                changes.Add(new ScreenChange(screen, added, removed, background, was.Background, now.Background));
            }
        }

        return changes.Count == 0 ? null : new TimelineStep(label, changes);
    }

    /// <summary>The counter-move: what was added goes, what was removed comes back, the background returns.</summary>
    public IReadOnlyList<ScreenOp> UndoOps() =>
        [.. Changes.SelectMany(change => Ops(change.Screen, change.Added, change.Removed, change.BackgroundChanged, change.BackgroundBefore))];

    /// <summary>The same act again, as it was done: what was removed goes, what was added comes back.</summary>
    public IReadOnlyList<ScreenOp> RedoOps() =>
        [.. Changes.SelectMany(change => Ops(change.Screen, change.Removed, change.Added, change.BackgroundChanged, change.BackgroundAfter))];

    /// <summary>Whether undoing or redoing this step could bring one of these pictures onto a screen.</summary>
    /// <remarks>
    /// The backgrounds count only when the step changed them: a step stores both whatever happened,
    /// and thirty tokens laid on a map would otherwise each "show" the map and be cut with it,
    /// although none of their ops ever sets it (Part 7: cut up to the oldest entry that still holds
    /// the picture).
    /// </remarks>
    public bool Shows(IReadOnlySet<AssetId> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        return Changes.Any(change =>
            change.Added.Concat(change.Removed).OfType<ImageItem>().Any(item => assets.Contains(item.AssetId))
            || (change.BackgroundChanged
                && ((change.BackgroundBefore is { } before && assets.Contains(before.AssetId))
                    || (change.BackgroundAfter is { } after && assets.Contains(after.AssetId)))));
    }

    /// <summary>
    /// The same step with a picture's new name in every item and background it would bring back -
    /// a rename in the stock changes the name everywhere (Part 3, "one picture, one name"), and an
    /// undo must not lay the picture down under the name it had when the step was recorded.
    /// </summary>
    public TimelineStep Renamed(AssetId asset, string name) =>
        this with
        {
            Changes =
            [
                .. Changes.Select(change => change with
                {
                    Added = Renamed(change.Added, asset, name),
                    Removed = Renamed(change.Removed, asset, name),
                    BackgroundBefore = Renamed(change.BackgroundBefore, asset, name),
                    BackgroundAfter = Renamed(change.BackgroundAfter, asset, name),
                }),
            ],
        };

    private static List<SceneItem> Renamed(IReadOnlyList<SceneItem> items, AssetId asset, string name) =>
        [.. items.Select(item => item is ImageItem image && image.AssetId == asset ? image with { Name = name } : item)];

    private static BackgroundItem? Renamed(BackgroundItem? background, AssetId asset, string name) =>
        background is { } found && found.AssetId == asset ? found with { Name = name } : background;

    private static IEnumerable<ScreenOp> Ops(
        ScreenRef screen, IReadOnlyList<SceneItem> going, IReadOnlyList<SceneItem> coming, bool background, BackgroundItem? backgroundTo)
    {
        foreach (var item in going)
        {
            yield return new ScreenOp(screen, new RemoveItem(item.ItemId));
        }

        foreach (var item in coming)
        {
            yield return new ScreenOp(screen, new AddItem(item));
        }

        if (background)
        {
            yield return new ScreenOp(screen, backgroundTo is null ? new ClearBackground() : new SetBackground(backgroundTo));
        }
    }
}

/// <summary>
/// Undo and redo for the whole session (Part 4): control-wide, not saved, gone with the control.
/// <para>
/// <b>It lives in the control</b> (siting question 3): the control remembers what it sent and sends
/// the counter-move itself. What goes in is decided in ONE place on the control's side, the session
/// every grip passes through - not at the buttons (<c>C15</c>).
/// </para>
/// <para>
/// <b>Capped only as hygiene</b> (Part 1, idea 6): past <see cref="Limit"/> the oldest step goes, and
/// undo simply no longer reaches all the way back. A new step drops what was undone - the next act
/// discards redo, as everywhere.
/// </para>
/// <para>
/// <b>Every member takes one lock.</b> The recording session writes from the thread pool (under its
/// own gate, after <c>ConfigureAwait(false)</c>), while the buttons read the labels and the delete
/// question counts the lost steps on the UI thread - without the lock, a <see cref="Record"/> in
/// the middle of a <see cref="CountLost"/> ends the enumeration with "collection was modified".
/// </para>
/// </summary>
public sealed class Timeline
{
    /// <summary>How many steps are kept. Steps are small; the cap is against an evening that never ends, not against memory.</summary>
    public const int Limit = 500;

    private readonly Lock _lock = new();
    private readonly LinkedList<TimelineStep> _done = new();
    private readonly Stack<TimelineStep> _undone = new();

    /// <summary>What the next undo takes back - the label the button carries (Part 7). None when there is nothing.</summary>
    public string? UndoLabel
    {
        get
        {
            lock (_lock)
            {
                return _done.Last?.Value.Label;
            }
        }
    }

    /// <summary>What the next redo does again.</summary>
    public string? RedoLabel
    {
        get
        {
            lock (_lock)
            {
                return _undone.TryPeek(out var step) ? step.Label : null;
            }
        }
    }

    /// <summary>How many steps undo can go back.</summary>
    public int Depth
    {
        get
        {
            lock (_lock)
            {
                return _done.Count;
            }
        }
    }

    /// <summary>A new act. Whatever was undone cannot be redone any more.</summary>
    public void Record(TimelineStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        lock (_lock)
        {
            _done.AddLast(step);
            _undone.Clear();

            if (_done.Count > Limit)
            {
                _done.RemoveFirst();
            }
        }
    }

    /// <summary>The step to take back, now moved to redo - or none at the start of the session.</summary>
    public TimelineStep? Undo()
    {
        lock (_lock)
        {
            if (_done.Last is not { } last)
            {
                return null;
            }

            _done.RemoveLast();
            _undone.Push(last.Value);

            return last.Value;
        }
    }

    /// <summary>The step to do again, now back in undo - or none.</summary>
    public TimelineStep? Redo()
    {
        lock (_lock)
        {
            if (!_undone.TryPop(out var step))
            {
                return null;
            }

            _done.AddLast(step);

            return step;
        }
    }

    /// <summary>
    /// Gives a picture its new name in every step - called with the rename from the stock, so that
    /// undo and redo bring it back under the name the stock, the scenes and the table carry
    /// (Part 3, "one picture, one name"). The labels stay as they were: they tell what was done.
    /// </summary>
    public void Rename(AssetId asset, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_lock)
        {
            for (var node = _done.First; node is not null; node = node.Next)
            {
                node.Value = node.Value.Renamed(asset, name);
            }

            // A stack enumerates from the top; pushed back from the bottom up, the order holds.
            var undone = _undone.ToArray();

            _undone.Clear();

            for (var index = undone.Length - 1; index >= 0; index--)
            {
                _undone.Push(undone[index].Renamed(asset, name));
            }
        }
    }

    /// <summary>How many steps <see cref="Truncate"/> would cost - the number the delete question names (Part 7).</summary>
    public int CountLost(IReadOnlySet<AssetId> assets)
    {
        lock (_lock)
        {
            var (done, undone) = Lost(assets);

            return done + undone;
        }
    }

    /// <summary>
    /// Cuts the timeline so that no undo and no redo can bring these pictures back - called before
    /// they are deleted from the stock (Part 4: "only what is still in the stock comes back"). Undo
    /// loses everything up to and including the newest step that shows one of them, since nothing
    /// older can be reached past it; redo is dropped whole if any of its steps shows one. Returns how
    /// many steps went.
    /// </summary>
    public int Truncate(IReadOnlySet<AssetId> assets)
    {
        lock (_lock)
        {
            var (done, undone) = Lost(assets);

            for (var index = 0; index < done; index++)
            {
                _done.RemoveFirst();
            }

            if (undone > 0)
            {
                _undone.Clear();
            }

            return done + undone;
        }
    }

    /// <summary>Empties the timeline - a campaign switch or delete, behind which lie pictures the hub can no longer deliver.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _done.Clear();
            _undone.Clear();
        }
    }

    private (int Done, int Undone) Lost(IReadOnlySet<AssetId> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var done = 0;
        var index = 0;

        foreach (var step in _done)
        {
            index++;

            if (step.Shows(assets))
            {
                done = index;
            }
        }

        var undone = _undone.Any(step => step.Shows(assets)) ? _undone.Count : 0;

        return (done, undone);
    }
}
