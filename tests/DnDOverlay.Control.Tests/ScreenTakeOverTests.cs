using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// "Take over" (Part 3, "reassign screen"), the control's half: the screen that is there gets the missing
/// one's tile place and view rotation, and the old entry goes.
/// </summary>
public sealed class ScreenTakeOverTests
{
    private static readonly ScreenRef Old = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000c1")), new ScreenId("DISPLAY1"));
    private static readonly ScreenRef Fresh = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000c2")), new ScreenId("DISPLAY1"));
    private static readonly ScreenRef Wall = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000c3")), new ScreenId("DISPLAY1"));

    /// <summary>
    /// Against the real hub, with the inventory written back on its announcement as the control does it
    /// (<see cref="ScreenViews.KeptThrough"/>): the old screen's view reaches the new one. Read after the
    /// hub was asked, the write-back had already taken the old row away, and the view with it.
    /// </summary>
    [Fact]
    public async Task The_new_screen_takes_over_the_old_ones_view()
    {
        var catalog = new ScreenCatalog();

        catalog.Restore([Row(Old, ViewRotation.Quarter)]);

        using var file = new TemporaryConfiguration();
        var settings = new ControlSettings(file.File, new ControlConfiguration { KnownScreens = [Row(Old, ViewRotation.Quarter)] });

        catalog.Changed += () => settings.Update(configuration => ScreenViews.KeptThrough(configuration, catalog.Snapshot()));
        catalog.Report(Fresh.Device, [new ScreenInfo(Fresh.Screen, "BEAMER//1", null, new PixelSize(1920, 1080), 96, true)], reported: null);

        Assert.True(await ScreenTakeOver.RunAsync(Session(catalog), settings, Old, Fresh));

        Assert.Equal(ViewRotation.Quarter, ScreenViews.Of(settings.Current, Fresh));
        Assert.Null(ScreenViews.Find(settings.Current, Old));
    }

    /// <summary>The hub refuses - the old screen is not missing: nothing in control.json changes.</summary>
    [Fact]
    public async Task A_take_over_the_hub_refuses_changes_nothing()
    {
        var catalog = new ScreenCatalog();
        using var file = new TemporaryConfiguration();
        var before = new ControlConfiguration { KnownScreens = [Row(Old, ViewRotation.Quarter)], TileOrder = [Key(Old)] };
        var settings = new ControlSettings(file.File, before);

        Assert.False(await ScreenTakeOver.RunAsync(Session(catalog), settings, Old, Fresh));
        Assert.Same(before, settings.Current);
    }

    /// <summary>The new screen stands where the old one stood, and only there - not twice.</summary>
    [Fact]
    public void The_new_screen_takes_the_old_ones_tile_place()
    {
        var configuration = new ControlConfiguration { TileOrder = [Key(Fresh), Key(Wall), Key(Old)] };

        var after = ScreenTakeOver.Inherited(configuration, Old, Fresh, view: null);

        Assert.Equal([Key(Wall), Key(Fresh)], after.TileOrder);
    }

    /// <summary>An old screen that had no tile place leaves the order as it is.</summary>
    [Fact]
    public void Without_a_tile_place_the_order_stays()
    {
        var configuration = new ControlConfiguration { TileOrder = [Key(Fresh), Key(Wall)] };

        Assert.Equal([Key(Fresh), Key(Wall)], ScreenTakeOver.Inherited(configuration, Old, Fresh, view: null).TileOrder);
    }

    /// <summary>With no view known for the old screen, the new one keeps its own.</summary>
    [Fact]
    public void Without_an_old_view_the_new_screen_keeps_its_own()
    {
        var configuration = new ControlConfiguration { KnownScreens = [Row(Old), Row(Fresh, ViewRotation.Half)] };

        var after = ScreenTakeOver.Inherited(configuration, Old, Fresh, view: null);

        Assert.Equal(ViewRotation.Half, ScreenViews.Of(after, Fresh));
        Assert.Null(ScreenViews.Find(after, Old));
    }

    private static ScreenKey Key(ScreenRef screen) => new(screen.Device.Value, screen.Screen.Value);

    private static KnownScreen Row(ScreenRef screen, ViewRotation view = ViewRotation.None) =>
        new(screen.Device.Value, screen.Screen.Value, "TISCH-PC//1", ScreenState.Enabled, new PixelSize(1920, 1080), 96, new ScreenSettings(), view);

    private static SessionApi Session(ScreenCatalog catalog) =>
        new(
            new SceneStore(),
            catalog,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);

    /// <summary>A control.json in a folder of its own, removed afterwards.</summary>
    private sealed class TemporaryConfiguration : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenTakeOverTests-" + Guid.NewGuid().ToString("N"));

        internal TemporaryConfiguration() =>
            File = new(System.IO.Path.Combine(_folder, "control.json"), ConfigurationJsonContext.Default.ControlConfiguration, TimeProvider.System);

        internal ConfigurationFile<ControlConfiguration> File { get; }

        public void Dispose()
        {
            File.Dispose();

            if (System.IO.Directory.Exists(_folder))
            {
                System.IO.Directory.Delete(_folder, recursive: true);
            }
        }
    }
}
