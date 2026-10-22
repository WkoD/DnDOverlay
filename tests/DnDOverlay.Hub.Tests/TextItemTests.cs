using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>A free text on a screen (M5b9): laid down like a picture, cleaned, held to a size that fits.</summary>
public sealed class TextItemTests
{
    private static readonly ScreenRef Screen = new(new DeviceId(Guid.Parse("dddddddd-0000-0000-0000-00000000000a")), new ScreenId("DISPLAY1"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_text_arrives_on_top_at_a_headings_height_with_control_characters_gone()
    {
        using var session = Session();

        var id = await session.AddTextAsync(Screen, "  Kerker\tvon\r\nTownsvil  ", 4, Cancellation);
        var text = Assert.IsType<TextItem>((await session.GetSceneAsync(Screen, Cancellation)).Items.Single());

        Assert.Equal(id, text.ItemId);
        Assert.Equal("Kerker von\nTownsvil", text.Text);
        Assert.Equal(TextItem.ArrivalScale, text.Scale, 9);
        Assert.Equal(4, text.AspectRatio);
    }

    /// <summary>A long line comes in lower rather than reaching past both edges, and the length is cut.</summary>
    [Fact]
    public async Task A_long_text_is_cut_and_held_inside_the_screen_width()
    {
        using var session = Session();

        await session.AddTextAsync(Screen, new string('x', TextItem.MaxLength + 50), 60, Cancellation);

        var text = Assert.IsType<TextItem>((await session.GetSceneAsync(Screen, Cancellation)).Items.Single());
        var context = session.ContextOf(Screen);

        Assert.Equal(TextItem.MaxLength, text.Text.Length);
        Assert.True(Layout.ItemToRect(text, context).Width <= 0.9 + 1e-9);
    }

    [Fact]
    public async Task Changing_the_words_keeps_place_and_height()
    {
        using var session = Session();

        var id = await session.AddTextAsync(Screen, "Kerker", 3, Cancellation);
        var before = Assert.IsType<TextItem>((await session.GetSceneAsync(Screen, Cancellation)).Items.Single());

        await session.SetTextAsync(Screen, id, "Kerker von Townsvil", 8, Cancellation);

        var after = Assert.IsType<TextItem>((await session.GetSceneAsync(Screen, Cancellation)).Items.Single());

        Assert.Equal(before with { Text = "Kerker von Townsvil", AspectRatio = 8 }, after);
    }

    private static SessionApi Session() =>
        new(
            new SceneStore(),
            new ScreenCatalog(),
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
}
