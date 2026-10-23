using System.Globalization;
using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// "Names at the table: off, until somebody wants it otherwise" (Part 7). A default per screen
/// decides whether a NEWLY inserted picture arrives captioned (Part 6, default off); what already
/// lies there and what a scene brings keep their own choice, because it belongs to the instance.
/// </summary>
public sealed class NewPictureNameTests
{
    private static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000040"));
    private static readonly ScreenRef First = new(Device, new ScreenId(@"\\?\DISPLAY#NAMES#1"));
    private static readonly ScreenRef Second = new(Device, new ScreenId(@"\\?\DISPLAY#NAMES#2"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A screen nobody set: a new picture carries its file name unseen - "ork_04" under a portrait is worse than nothing.</summary>
    [Fact]
    public async Task By_default_a_new_picture_arrives_without_its_name()
    {
        using var session = Session();

        await session.AddItemAsync(First, Picture(0), position: null, Cancellation);
        await session.AddItemsAsync(First, [Picture(1), Picture(2)], Cancellation);

        Assert.All(await Pictures(session, First), picture => Assert.False(picture.ShowName));
    }

    /// <summary>Switched on once, every picture to come arrives captioned - by each way in, and only on that screen.</summary>
    [Fact]
    public async Task Switched_on_every_new_picture_on_that_screen_shows_its_name()
    {
        using var session = Session();

        await NamesOn(session, First);

        await session.AddItemAsync(First, Picture(0), position: null, Cancellation);
        await session.AddItemsAsync(First, [Picture(1)], Cancellation);
        await session.SendToAllAsync([Picture(2)], Cancellation);

        var first = await Pictures(session, First);

        Assert.Equal(3, first.Count);
        Assert.All(first, picture => Assert.True(picture.ShowName));
        Assert.False(Assert.Single(await Pictures(session, Second)).ShowName);
    }

    /// <summary>The default decides the arrival only: pictures already lying there keep what they had.</summary>
    [Fact]
    public async Task Switching_it_on_leaves_the_pictures_already_there_alone()
    {
        using var session = Session();

        await session.AddItemsAsync(First, [Picture(0), Picture(1)], Cancellation);
        await NamesOn(session, First);

        Assert.All(await Pictures(session, First), picture => Assert.False(picture.ShowName));
    }

    /// <summary>A loaded scene keeps what it was saved with - captioned and nameless alike, whatever the screen's default.</summary>
    [Fact]
    public async Task A_loaded_scene_keeps_the_names_it_was_saved_with()
    {
        using var session = Session();
        var laid = await session.AddItemsAsync(First, [Picture(0), Picture(1)], Cancellation);

        await session.SetShowNameAsync(First, laid[0], show: true, Cancellation);

        var saved = await session.GetSceneAsync(First, Cancellation);

        await NamesOn(session, First);
        await session.LoadSceneAsync(First, saved, SceneLoad.Replace, Cancellation);

        var loaded = (await Pictures(session, First)).OrderBy(picture => picture.ZOrder).ToList();

        Assert.Equal(saved.Items.OrderBy(item => item.ZOrder).Select(item => ((ImageItem)item).ShowName), loaded.Select(picture => picture.ShowName));
        Assert.Equal([true, false], loaded.Select(picture => picture.ShowName));
    }

    /// <summary>A background does not follow it: the caption under a background was taken out in M4.</summary>
    [Fact]
    public async Task A_new_background_stays_without_its_name()
    {
        using var session = Session();

        await NamesOn(session, First);
        await session.SetBackgroundAsync(First, Picture(0), Cancellation);

        Assert.False((await session.GetSceneAsync(First, Cancellation)).Background?.ShowName);
    }

    private static Task NamesOn(SessionApi session, ScreenRef screen) =>
        session.ApplyConfigAsync(
            screen.Device,
            new ConfigUpdate([new ScreenConfigUpdate(screen.Screen, new ScreenSettings(ShowImageName: true))]),
            Cancellation);

    private static async Task<IReadOnlyList<ImageItem>> Pictures(SessionApi session, ScreenRef screen) =>
        [.. (await session.GetSceneAsync(screen, Cancellation)).Items.OfType<ImageItem>()];

    private static AssetRef Picture(int index) =>
        new(
            new AssetId(index.ToString("x4", CultureInfo.InvariantCulture).PadLeft(64, 'e')),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "ork_04");

    private static SessionApi Session()
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        var screens = new ScreenCatalog();

        screens.Report(Device, [Info(First.Screen, "TISCH-PC//1"), Info(Second.Screen, "TISCH-PC//2")], reported: null);

        return new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(options), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
    }

    private static ScreenInfo Info(ScreenId screen, string label) =>
        new(screen, label, null, new PixelSize(1920, 1080), 96, IsPrimary: false);
}
