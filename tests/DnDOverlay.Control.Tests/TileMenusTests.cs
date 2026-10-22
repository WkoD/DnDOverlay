using System.Windows.Controls;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>The item menu of a tile (Part 7): what it offers depends on where the picture lies.</summary>
public sealed class TileMenusTests
{
    /// <summary>
    /// "Reset item" is offered only for a picture lying out, like "Turn" and "Park": the fan owns a
    /// parked card's place and angle, and a reset there would be a second truth beside it.
    /// </summary>
    [Fact]
    public async Task Reset_item_is_offered_only_for_a_picture_lying_out()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var menus = new TileMenus(CampaignBench.Screen, bench.Session, () => bench.Session.Screens);
            var context = ScreenContext.Default(new PixelSize(1920, 1080), 96);
            var lying = Picture(parked: false);
            var parked = Picture(parked: true);
            var scene = SceneState.Empty with { Items = [lying, parked] };

            Assert.Contains(Strings.TileMenuResetItem, Headers(menus.ItemMenu(new Grid(), scene, lying, context, new Selection(), default)));
            Assert.DoesNotContain(Strings.TileMenuResetItem, Headers(menus.ItemMenu(new Grid(), scene, parked, context, new Selection(), default)));

            await Task.CompletedTask;
        });
    }

    private static ImageItem Picture(bool parked) =>
        new(
            new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.2, 4.0 / 3, 0, 0, false, parked, 1,
            new AssetId(new string('a', 64)),
            new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64)),
            "Grimmbart",
            false,
            false);

    private static List<string> Headers(ContextMenu menu) =>
        [.. menu.Items.OfType<MenuItem>().Select(item => item.Header as string).OfType<string>()];
}
