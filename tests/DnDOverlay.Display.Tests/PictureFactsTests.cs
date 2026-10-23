using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>What the scenes say about one picture - its name for the log, its source width for sharpening, whether it moves.</summary>
public sealed class PictureFactsTests
{
    private static readonly AssetId Map = new("abcdef01" + new string('a', 56));
    private static readonly AssetId Dragon = new("12345678" + new string('b', 56));
    private static readonly AssetId Gone = new("deadbeef" + new string('c', 56));

    private static readonly AssetMeta Still = new(1000, 800, "png", 1, false, new string('e', 64));
    private static readonly AssetMeta Moving = new(640, 480, "gif", 12, true, new string('f', 64));

    [Fact]
    public void A_picture_is_named_as_the_DM_named_it()
    {
        Assert.Equal("Dilwyn Kemri", PictureFacts.NameOf([Scene(Item(Dragon, Still, "Dilwyn Kemri"))], Dragon));
    }

    /// <summary>A background without a name of its own, or a picture no scene holds any more, is named by the start of its hash.</summary>
    [Fact]
    public void Without_a_name_the_hash_speaks()
    {
        var scene = SceneState.Empty with { Background = new BackgroundItem(Map, Still, null, false, 0.5, 0.5, 1, 0, false) };

        Assert.Equal("abcdef01", PictureFacts.NameOf([scene], Map));
        Assert.Equal("deadbeef", PictureFacts.NameOf([scene], Gone));
    }

    [Fact]
    public void A_named_background_is_named()
    {
        var scene = SceneState.Empty with { Background = new BackgroundItem(Map, Still, "Brimsee", false, 0.5, 0.5, 1, 0, false) };

        Assert.Equal("Brimsee", PictureFacts.NameOf([scene], Map));
    }

    [Fact]
    public void The_source_width_comes_from_the_scene()
    {
        var table = Scene(Item(Dragon, Still, "Dragon"));
        var wall = SceneState.Empty with { Background = new BackgroundItem(Map, Moving, "Map", false, 0.5, 0.5, 1, 0, false) };

        Assert.Equal(1000, PictureFacts.SourceWidth([table, wall], Dragon));
        Assert.Equal(640, PictureFacts.SourceWidth([table, wall], Map));
    }

    /// <summary>A picture dropped while its sharpening was on its way has no source width, so no step is asked for.</summary>
    [Fact]
    public void A_picture_no_scene_holds_has_no_source_width()
    {
        Assert.Equal(0, PictureFacts.SourceWidth([Scene(Item(Dragon, Still, "Dragon"))], Gone));
    }

    /// <summary>Whether it moves is the codec's settled answer, read off any scene - an item or a background.</summary>
    [Fact]
    public void A_picture_moves_when_a_scene_says_so()
    {
        var table = Scene(Item(Dragon, Moving, "Dragon"));
        var wall = SceneState.Empty with { Background = new BackgroundItem(Map, Moving, "Map", false, 0.5, 0.5, 1, 0, false) };
        var still = Scene(Item(Gone, Still, "Still"));

        Assert.True(PictureFacts.Animated([still, table], Dragon));
        Assert.True(PictureFacts.Animated([still, wall], Map));
        Assert.False(PictureFacts.Animated([still, table, wall], Gone));
        Assert.False(PictureFacts.Animated([], Dragon));
    }

    private static ImageItem Item(AssetId asset, AssetMeta meta, string name) =>
        new(new ItemId(Guid.Parse("10000000-0000-0000-0000-000000000001")), 0.5, 0.5, 0.2, 1.25, 0, 0, false, false, 1, asset, meta, name, false, false);

    private static SceneState Scene(params SceneItem[] items) => SceneState.Empty with { Items = [.. items] };
}
