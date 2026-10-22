using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// What the DM has picked out on one screen (Part 3, Part 7): an ordered list the focus reads in
/// selection order, and the rules on it.
/// </summary>
public sealed class SelectionTests
{
    private static readonly ItemId Orc = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
    private static readonly ItemId Goblin = new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"));
    private static readonly ItemId Troll = new(Guid.Parse("00000000-0000-0000-0000-0000000000a3"));

    /// <summary>An ordered list, not a set: the oldest choice stands first, the newest last (Part 3).</summary>
    [Fact]
    public void The_order_is_the_order_of_choosing()
    {
        var selection = new Selection();

        selection.Set([Troll, Orc]);
        selection.Toggle(Goblin);

        Assert.Equal([Troll, Orc, Goblin], selection.Items);
    }

    /// <summary>Ctrl+click takes one out again; put back, it goes to the end, where the newest choice belongs.</summary>
    [Fact]
    public void Toggling_twice_moves_a_picture_to_the_end()
    {
        var selection = new Selection();

        selection.Set([Orc, Goblin]);
        selection.Toggle(Orc);

        Assert.Equal([Goblin], selection.Items);

        selection.Toggle(Orc);

        Assert.Equal([Goblin, Orc], selection.Items);
    }

    /// <summary>A tap on a picture replaces, it does not add (Part 7).</summary>
    [Fact]
    public void Only_replaces_whatever_was_selected()
    {
        var selection = new Selection();

        selection.Set([Orc, Goblin]);
        selection.Only(Troll);

        Assert.Equal([Troll], selection.Items);
        Assert.True(selection.Any);
        Assert.True(selection.Contains(Troll));
        Assert.False(selection.Contains(Orc));
    }

    /// <summary>A frame that caught one picture twice selects it once.</summary>
    [Fact]
    public void A_frame_selects_each_picture_once()
    {
        var selection = new Selection();

        selection.Set([Orc, Goblin, Orc]);

        Assert.Equal([Orc, Goblin], selection.Items);
    }

    /// <summary>The frame with Ctrl adds behind what stood, and what stood keeps its place.</summary>
    [Fact]
    public void Adding_keeps_what_was_selected_where_it_stood()
    {
        var selection = new Selection();

        selection.Set([Goblin]);
        selection.Add([Orc, Goblin, Troll]);

        Assert.Equal([Goblin, Orc, Troll], selection.Items);
    }

    /// <summary>
    /// A picture removed at the table or moved away by a second control does not stay selected
    /// invisibly - and what stays keeps its order.
    /// </summary>
    [Fact]
    public void Keep_drops_what_is_no_longer_on_the_screen_and_keeps_the_order()
    {
        var selection = new Selection();
        var changes = 0;

        selection.Set([Troll, Orc, Goblin]);
        selection.Changed += (_, _) => changes++;
        selection.Keep(SceneState.Empty with { Items = [Text(Goblin), Text(Troll)] });

        Assert.Equal([Troll, Goblin], selection.Items);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// The drawing follows every change - and only a change: a patch twenty times a second that
    /// takes nothing away must not redraw the marks twenty times.
    /// </summary>
    [Fact]
    public void Nothing_that_changes_nothing_says_it_changed()
    {
        var selection = new Selection();
        var changes = 0;

        selection.Changed += (_, _) => changes++;

        selection.Clear();
        selection.Drop(Orc);
        selection.Keep(SceneState.Empty);

        Assert.Equal(0, changes);

        selection.Only(Orc);
        selection.Keep(SceneState.Empty with { Items = [Text(Orc)] });
        selection.Drop(Goblin);

        Assert.Equal(1, changes);
    }

    /// <summary>Putting a picture away drops it from the selection; the others stay chosen (Handlauf M4).</summary>
    [Fact]
    public void Drop_takes_out_one_picture_and_leaves_the_rest()
    {
        var selection = new Selection();
        var changes = 0;

        selection.Set([Orc, Goblin, Troll]);
        selection.Changed += (_, _) => changes++;
        selection.Drop(Goblin);

        Assert.Equal([Orc, Troll], selection.Items);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Clear_empties_and_says_so_once()
    {
        var selection = new Selection();
        var changes = 0;

        selection.Set([Orc, Goblin]);
        selection.Changed += (_, _) => changes++;
        selection.Clear();
        selection.Clear();

        Assert.Empty(selection.Items);
        Assert.False(selection.Any);
        Assert.Equal(1, changes);
    }

    private static TextItem Text(ItemId item) => new(item, 0.5, 0.5, 0.2, 4, 0, 0, false, false, 1, "Townsvil");
}
