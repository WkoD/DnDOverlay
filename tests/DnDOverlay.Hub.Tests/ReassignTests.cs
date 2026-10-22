using System.Globalization;
using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>"Reassign screen" (Part 3): a screen that is there takes over a missing one, arrangement included.</summary>
public sealed class ReassignTests
{
    private static readonly DeviceId Old = new(Guid.Parse("cccccccc-0000-0000-0000-000000000001"));
    private static readonly DeviceId New = new(Guid.Parse("cccccccc-0000-0000-0000-000000000002"));
    private static readonly ScreenRef Gone = new(Old, new ScreenId(@"\\?\DISPLAY#OLD#1"));
    private static readonly ScreenRef There = new(New, new ScreenId(@"\\?\DISPLAY#NEW#1"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The arrangement of the missing screen comes onto the one that is there as one patch, and the missing one goes.</summary>
    [Fact]
    public async Task The_arrangement_moves_onto_the_screen_that_is_there()
    {
        var (session, catalog) = Session();

        using (session)
        {
            var laid = await session.AddItemsAsync(Gone, [.. Many(2)], Cancellation);
            var presence = catalog.Report(Old, [Info(Gone.Screen)], reported: null).Presence;

            catalog.Departed(Old, presence);
            await session.AddItemsAsync(There, [.. Many(1)], Cancellation);

            Assert.True(await session.ReassignScreenAsync(Gone, There, Cancellation));

            var scene = await session.GetSceneAsync(There, Cancellation);

            Assert.Equal(laid, scene.Items.Select(item => item.ItemId));
            Assert.Null(catalog.ViewOf(Gone));
            Assert.Empty((await session.GetSceneAsync(Gone, Cancellation)).Items);
        }
    }

    /// <summary>
    /// After a change of hardware the arrangement is fitted to the screen that is there, as a loaded
    /// scene is: a background that covered the old screen covers the new one, and the focus carries
    /// on with the pictures. Before, everything was laid down unchanged - a "Cover" background on a
    /// 1280x1024 monitor kept the centre and scale of the 1920x1080 one, and the focus was dropped.
    /// </summary>
    [Fact]
    public async Task The_arrangement_is_fitted_to_the_new_screen_and_keeps_its_focus()
    {
        var (session, catalog) = Session();

        using (session)
        {
            var laid = await session.AddItemsAsync(Gone, [.. Many(2)], Cancellation);

            await session.SetBackgroundAsync(Gone, Many(3).Last(), Cancellation);
            await session.SetBackgroundFitAsync(Gone, BackgroundFit.Cover, Cancellation);
            _ = await session.SetFocusAsync(Gone, [laid[1]], Cancellation);

            catalog.Departed(Old, catalog.Report(Old, [Info(Gone.Screen)], reported: null).Presence);
            catalog.Report(New, [Info(There.Screen) with { Size = new PixelSize(1280, 1024) }], reported: null);

            Assert.True(await session.ReassignScreenAsync(Gone, There, Cancellation));

            var scene = await session.GetSceneAsync(There, Cancellation);
            var (centre, scale) = Layout.FitBackground(
                scene.Background!.Meta.AspectRatio, BackgroundFit.Cover, session.ContextOf(There), 0);

            Assert.Equal(scale, scene.Background.Scale, 9);
            Assert.Equal(centre.X, scene.Background.CenterX, 9);
            Assert.Equal([laid[1]], scene.FocusItems);
        }
    }

    /// <summary>A screen that is still there cannot be taken over - it is somebody's table.</summary>
    [Fact]
    public async Task A_screen_that_is_there_is_not_taken_over()
    {
        var (session, catalog) = Session();

        using (session)
        {
            catalog.Report(Old, [Info(Gone.Screen)], reported: null);

            Assert.False(await session.ReassignScreenAsync(Gone, There, Cancellation));
            Assert.NotNull(catalog.ViewOf(Gone));
        }
    }

    private static (SessionApi Session, ScreenCatalog Catalog) Session()
    {
        var options = new HubOptions
        {
            KnownDevices =
            [
                new PairedDevice(Old, "ALT-PC", PairingRole.Display, "old-token"),
                new PairedDevice(New, "NEU-PC", PairingRole.Display, "new-token"),
            ],
        };

        var catalog = new ScreenCatalog();

        catalog.Report(Old, [Info(Gone.Screen)], reported: null);
        catalog.Report(New, [Info(There.Screen)], reported: null);

        return (
            new SessionApi(
                new SceneStore(),
                catalog,
                new DisplayConnections(),
                new PairingDirectory(Options.Create(options), TimeProvider.System),
                new SessionEvents(),
                null,
                NullLogger<SessionApi>.Instance),
            catalog);
    }

    private static IEnumerable<AssetRef> Many(int count) =>
        Enumerable.Range(0, count).Select(index => new AssetRef(
            new AssetId(index.ToString("x4", CultureInfo.InvariantCulture).PadLeft(64, 'e')),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Söldner"));

    private static ScreenInfo Info(ScreenId screen) =>
        new(screen, screen.Value, null, new PixelSize(1920, 1080), 96, IsPrimary: false);
}
