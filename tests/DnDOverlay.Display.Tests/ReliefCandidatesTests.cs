using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>What the memory rule may step down to its thumbnail, and how it sees each picture (Part 6).</summary>
public sealed class ReliefCandidatesTests
{
    private static readonly AssetId Map = new(new string('a', 64));
    private static readonly AssetId Dragon = new(new string('b', 64));
    private static readonly AssetId Goblin = new(new string('c', 64));
    private static readonly AssetId Gone = new(new string('d', 64));
    private static readonly AssetMeta Meta = new(1000, 800, "png", 1, false, new string('e', 64));

    private static readonly ItemId First = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly ItemId Second = new(Guid.Parse("10000000-0000-0000-0000-000000000002"));
    private static readonly ItemId Third = new(Guid.Parse("10000000-0000-0000-0000-000000000003"));

    [Fact]
    public void A_picture_on_the_table_is_a_candidate_aged_by_its_last_touch()
    {
        var scene = Scene(Item(First, Dragon, zOrder: 5));

        var picture = Assert.Single(Of([scene], [(Dragon, 200, 100)], touchedAt: _ => 42));

        Assert.Equal(new DecodedPicture(Dragon, 200L * 100 * 4, Parked: false, Age: 42, Held: false), picture);
    }

    /// <summary>A blurred background is the whole table gone soft: it is never offered.</summary>
    [Fact]
    public void A_background_is_never_a_candidate()
    {
        var scene = Scene(Item(First, Map, zOrder: 1)) with { Background = new BackgroundItem(Map, Meta, "Map", false, 0.5, 0.5, 1, 0, false) };

        Assert.Empty(Of([scene], [(Map, 4000, 3000)]));
    }

    /// <summary>Already down, or moving - its frames are built from its bytes - it is passed over.</summary>
    [Fact]
    public void A_picture_already_down_or_moving_is_passed_over()
    {
        var scene = Scene(Item(First, Dragon, zOrder: 1), Item(Second, Goblin, zOrder: 2));

        var candidates = Of(
            [scene],
            [(Dragon, 100, 100), (Goblin, 100, 100)],
            steppedDown: asset => asset == Dragon,
            moving: asset => asset == Goblin);

        Assert.Empty(candidates);
    }

    /// <summary>A bitmap no picture on a screen of this device uses is not the memory rule's - it is let go of elsewhere.</summary>
    [Fact]
    public void A_picture_no_scene_shows_is_not_a_candidate()
    {
        Assert.Empty(Of([Scene(Item(First, Dragon, zOrder: 1))], [(Gone, 100, 100)]));
    }

    /// <summary>In the fan its age is its place there - the lowest depth was parked longest ago - not its last touch.</summary>
    [Fact]
    public void A_parked_picture_is_aged_by_its_place_in_the_fan()
    {
        var scene = Scene(Item(First, Dragon, zOrder: 9, parked: true), Item(Second, Dragon, zOrder: 4, parked: true));

        var picture = Assert.Single(Of([scene], [(Dragon, 100, 100)], touchedAt: _ => 1_000));

        Assert.True(picture.Parked);
        Assert.Equal(4, picture.Age);
    }

    /// <summary>One item of the picture still on the table makes it a table picture: the fan comes first only when all of it lies there.</summary>
    [Fact]
    public void A_picture_parked_only_in_part_counts_as_on_the_table()
    {
        var scene = Scene(Item(First, Dragon, zOrder: 9, parked: true), Item(Second, Dragon, zOrder: 4));

        var picture = Assert.Single(Of([scene], [(Dragon, 100, 100)], touchedAt: _ => 7));

        Assert.False(picture.Parked);
        Assert.Equal(7, picture.Age);
    }

    /// <summary>A hand on any item that shows it makes the picture held - a gesture never goes blurred under the finger.</summary>
    [Fact]
    public void A_hand_on_one_of_its_items_holds_the_picture()
    {
        var table = Scene(Item(First, Dragon, zOrder: 1));
        var wall = Scene(Item(Second, Dragon, zOrder: 1), Item(Third, Goblin, zOrder: 2));

        var candidates = Of([table, wall], [(Dragon, 100, 100), (Goblin, 100, 100)], held: new HashSet<ItemId> { Second });

        Assert.True(candidates.Single(picture => picture.Asset == Dragon).Held);
        Assert.False(candidates.Single(picture => picture.Asset == Goblin).Held);
    }

    /// <summary>A background on one screen protects that picture on every screen of the device.</summary>
    [Fact]
    public void A_background_on_another_screen_protects_the_picture_everywhere()
    {
        var table = Scene(Item(First, Map, zOrder: 1));
        var wall = SceneState.Empty with { Background = new BackgroundItem(Map, Meta, "Map", false, 0.5, 0.5, 1, 0, false) };

        Assert.Empty(Of([table, wall], [(Map, 100, 100)]));
    }

    private static List<DecodedPicture> Of(
        IEnumerable<SceneState> scenes,
        IEnumerable<(AssetId, int, int)> decoded,
        Func<AssetId, bool>? steppedDown = null,
        Func<AssetId, bool>? moving = null,
        Func<AssetId, long>? touchedAt = null,
        HashSet<ItemId>? held = null) =>
        ReliefCandidates.Of(
            scenes,
            decoded,
            steppedDown ?? (_ => false),
            moving ?? (_ => false),
            touchedAt ?? (_ => 0),
            held ?? []);

    private static ImageItem Item(ItemId id, AssetId asset, int zOrder, bool parked = false) =>
        new(id, 0.5, 0.5, 0.2, 1.25, 0, zOrder, false, parked, 1, asset, Meta, "Dragon", false, false);

    private static SceneState Scene(params SceneItem[] items) => SceneState.Empty with { Items = [.. items] };
}
