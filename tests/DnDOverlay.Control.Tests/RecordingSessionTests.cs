using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The timeline in front of the hub (siting question 3): which commands become a step, and that a
/// step is taken back surgically. Against the real hub session, so the patches are the real ones.
/// </summary>
public sealed class RecordingSessionTests
{
    private static readonly ScreenRef Screen = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000c1")), new ScreenId("DISPLAY1"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_a_picture_is_one_step_and_undo_takes_it_off_again()
    {
        using var session = Recording();

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);

        Assert.Equal("Add \"Grimmbart\"", session.Timeline.UndoLabel);

        Assert.Equal("Add \"Grimmbart\"", await session.UndoAsync());
        Assert.Empty((await session.GetSceneAsync(Screen, Cancellation)).Items);
        Assert.Equal("Add \"Grimmbart\"", session.Timeline.RedoLabel);
    }

    /// <summary>Locking is not an act that lays or takes a picture - it passes through without a step (Part 7).</summary>
    [Fact]
    public async Task Unlocking_all_is_not_a_step()
    {
        using var session = Recording();

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);
        await session.UnlockAllAsync(Screen, Cancellation);

        Assert.Equal("Add \"Grimmbart\"", session.Timeline.UndoLabel);
    }

    /// <summary>
    /// "Krieger" laid and taken off, then renamed to "Grimmbart" in the stock: undo brings it back as
    /// "Grimmbart" - one picture, one name (Part 3) - not under the name the step recorded.
    /// </summary>
    [Fact]
    public async Task Undo_after_a_rename_brings_the_picture_back_under_its_new_name()
    {
        using var session = Recording();
        var picture = Picture("Krieger");

        var item = await session.AddItemAsync(Screen, picture, position: null, Cancellation);
        await session.RemoveItemAsync(Screen, item, Cancellation);
        await session.RenameAssetAsync(picture.AssetId, "Grimmbart", Cancellation);
        await session.UndoAsync();

        var back = Assert.IsType<ImageItem>(Assert.Single((await session.GetSceneAsync(Screen, Cancellation)).Items));

        Assert.Equal("Grimmbart", back.Name);
    }

    private static AssetRef Picture(string name) =>
        new(new AssetId(new string('a', 64)), new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64)), name);

    /// <summary>A session whose screen is known - reported once, as a connecting device would.</summary>
    private static RecordingSession Recording()
    {
        var screens = new ScreenCatalog();

        screens.Report(Screen.Device, [new ScreenInfo(Screen.Screen, "TISCH-PC//1", null, new PixelSize(1920, 1080), 96, true)], reported: null);

        return new(new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance));
    }
}
