using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>"Clear the picture store" empties it and fills it again for what lies on the screens (Part 7).</summary>
public sealed class AssetsWantedTests
{
    private static readonly AssetId Map = new(new string('a', 64));
    private static readonly AssetId Dragon = new(new string('b', 64));
    private static readonly AssetMeta Meta = new(1000, 800, "png", 1, false, new string('c', 64));

    [Fact]
    public void A_decoded_picture_is_not_fetched_again()
    {
        var wanted = AssetsWanted.Of(Scene(), decoded: _ => true, takeRefill: _ => false);

        Assert.Empty(wanted);
    }

    [Fact]
    public void A_picture_not_decoded_yet_is_fetched()
    {
        var wanted = AssetsWanted.Of(Scene(), decoded: asset => asset == Map, takeRefill: _ => false);

        var only = Assert.Single(wanted);
        Assert.Equal(Dragon, only.Asset);
        Assert.False(only.IsBackground);
    }

    /// <summary>The store was emptied under decoded pictures: they are fetched again, background included.</summary>
    [Fact]
    public void A_picture_marked_for_a_refill_is_fetched_although_decoded()
    {
        var marks = new HashSet<AssetId> { Map, Dragon };

        var wanted = AssetsWanted.Of(Scene(), decoded: _ => true, takeRefill: marks.Remove);

        Assert.Equal([Map, Dragon], wanted.Select(one => one.Asset));
        Assert.True(wanted[0].IsBackground);
        Assert.Empty(marks);
    }

    /// <summary>A picture fetched anyway takes its mark too, or the next run would fetch it a second time.</summary>
    [Fact]
    public void A_picture_fetched_anyway_gives_up_its_mark()
    {
        var marks = new HashSet<AssetId> { Dragon };

        _ = AssetsWanted.Of(Scene(), decoded: _ => false, takeRefill: marks.Remove);

        Assert.Empty(marks);
    }

    private static SceneState Scene() =>
        SceneState.Empty with
        {
            Background = new BackgroundItem(Map, Meta, "Map", false, 0.5, 0.5, 1, 0, false),
            Items =
            [
                new ImageItem(
                    new ItemId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
                    0.5, 0.5, 0.2, 1.25, 0, 0, false, false, 1, Dragon, Meta, "Dragon", false, false),
            ],
        };
}
