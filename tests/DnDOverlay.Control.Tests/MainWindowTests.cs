using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The window-free decisions of the main window. No window is brought up: what is tested is a rule
/// the window asks, not what it draws.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// The last-campaign offer is about pictures that came with the table (M5b8c). A free text needs
    /// no campaign, so a table with only a text on it shows nothing from the last one.
    /// </summary>
    [Fact]
    public void A_free_text_alone_is_not_a_table_showing_pictures()
    {
        var text = new TextItem(new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.2, 4, 0, 0, false, false, 1, "Townsvil");

        Assert.False(MainWindow.ShowsPictures(SceneState.Empty with { Items = [text] }));
        Assert.False(MainWindow.ShowsPictures(SceneState.Empty));
    }

    [Fact]
    public void A_picture_or_a_background_is_a_table_showing_pictures()
    {
        var meta = new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64));
        var asset = new AssetId(new string('a', 64));
        var picture = new ImageItem(new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.2, 4.0 / 3, 0, 0, false, false, 1, asset, meta, "Grimmbart", false, false);

        Assert.True(MainWindow.ShowsPictures(SceneState.Empty with { Items = [picture] }));
        Assert.True(MainWindow.ShowsPictures(SceneState.Empty with { Background = new BackgroundItem(asset, meta, "Karte", false, 0.5, 0.5, 1, 0, false) }));
    }
}
