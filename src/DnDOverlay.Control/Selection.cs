using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// What the DM has picked out on one screen.
/// <para>
/// <b>An ordered list and not a set</b> (Part 3, Part 7). In M4 nothing reads the order; from M5b
/// the focus does - "four items focused → all four <i>in selection order</i>" - and a
/// <c>HashSet</c> would go on looking right until that day, when the pictures would land on the
/// grid in whatever sequence a hash produced. It is the same shape of mistake that cost four
/// screen parameters in M2.
/// </para>
/// <para>
/// <b>It belongs beside the tile rather than in it</b>: the stock reuses the whole mechanism in
/// M5b, with exactly one difference - there a finger drag scrolls instead of drawing a frame
/// (Part 7). What the two share is this list and the rules on it.
/// </para>
/// <para>
/// <b>One per screen.</b> A selection across screens does not exist in the model - <c>FocusItems</c>
/// belongs to a <c>SceneState</c> (Part 3) - and the focus button would not know what it referred
/// to.
/// </para>
/// </summary>
internal sealed class Selection
{
    private readonly List<ItemId> _items = [];

    /// <summary>What is selected, oldest choice first.</summary>
    internal IReadOnlyList<ItemId> Items => _items;

    /// <summary>Whether anything is selected - which is also what makes the circles appear.</summary>
    internal bool Any => _items.Count > 0;

    /// <summary>Raised whenever the list changed, so the drawing can follow.</summary>
    internal event EventHandler? Changed;

    internal bool Contains(ItemId item) => _items.Contains(item);

    /// <summary>
    /// This one and nothing else - the ordinary tap. It is not a mode: a tap on free area clears,
    /// a tap on a picture replaces (Part 7).
    /// </summary>
    internal void Only(ItemId item) => Set([item]);

    /// <summary>
    /// Adds or removes one - Ctrl+click with the mouse, a tap on the selection circle with a
    /// finger. <b>Added at the end</b>, because the end is where the newest choice belongs in an
    /// order the focus later reads.
    /// </summary>
    internal void Toggle(ItemId item)
    {
        if (!_items.Remove(item))
        {
            _items.Add(item);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Everything the frame caught, in place of what was selected before.</summary>
    internal void Set(IEnumerable<ItemId> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items.Distinct());

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Everything the frame caught, on top of what was selected - the frame with Ctrl.</summary>
    internal void Add(IEnumerable<ItemId> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.AddRange(items.Where(item => !_items.Contains(item)));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops whatever is no longer on the screen. A picture removed at the table or moved away by a
    /// second control would otherwise stay selected invisibly, and the next menu command would go
    /// to an item that is not there - ineffective at the hub, and a promise broken in the surface.
    /// </summary>
    internal void Keep(SceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (_items.RemoveAll(item => !scene.Items.Any(lying => lying.ItemId == item)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
