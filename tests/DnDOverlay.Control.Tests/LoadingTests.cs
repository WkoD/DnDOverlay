using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The load count on the tile - "how many of how many" stand at the table (Part 10, M5b). The whole
/// is the scene, what is missing is the part of it the device still reports.
/// </summary>
public sealed class LoadingTests
{
    private static readonly AssetMeta Meta = new(800, 600, "png", 1024, IsAnimated: false, new string('b', 64));

    [Fact]
    public void While_pictures_load_the_count_says_how_many_of_how_many_stand_at_the_table()
    {
        var scene = SceneState.Empty with { Items = [Picture(1), Picture(2), Picture(3), Picture(4)] };

        Assert.Equal((1, 4), Loading.Count(scene, [Asset(2), Asset(3), Asset(4)]));
    }

    [Fact]
    public void Nothing_of_the_scene_loading_is_no_count()
    {
        var scene = SceneState.Empty with { Items = [Picture(1)] };

        Assert.Null(Loading.Count(scene, []));

        // Another screen of the same device loading something this one does not show.
        Assert.Null(Loading.Count(scene, [Asset(9)]));
    }

    /// <summary>One picture laid three times is one transfer; the background is one more; a text is none.</summary>
    [Fact]
    public void Pictures_are_counted_once_each_with_the_background_and_without_texts()
    {
        var text = new TextItem(new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.2, 4, 0, 0, false, false, 1, "Townsvil");
        var scene = SceneState.Empty with
        {
            Items = [Picture(1), Picture(1), Picture(1), Picture(2), text],
            Background = new BackgroundItem(Asset(3), Meta, null, false, 0.5, 0.5, 1, 0, false),
        };

        Assert.Equal((2, 3), Loading.Count(scene, [Asset(3)]));
    }

    private static AssetId Asset(int number) => new(number.ToString("x64", System.Globalization.CultureInfo.InvariantCulture));

    private static ImageItem Picture(int number) =>
        new(new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.2, 4.0 / 3, 0, 0, false, false, 1, Asset(number), Meta, "Bild", false, false);
}
