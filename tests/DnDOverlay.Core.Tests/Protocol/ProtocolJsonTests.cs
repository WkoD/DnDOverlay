using System.Text;
using System.Text.Json;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Scene;

namespace DnDOverlay.Core.Tests.Protocol;

public sealed class ProtocolJsonTests
{
    private static readonly ScreenRef Screen =
        new(new DeviceId(Guid.NewGuid()), new ScreenId(@"\\?\DISPLAY#IVM1234#5&1a2b"));

    private static readonly ProtocolMessage[] Messages =
    [
        new HelloMessage(
            Screen.Device,
            "TISCH-PC",
            "1.0.0",
            DnDOverlay.Core.Protocol.Protocol.Version,
            [new ScreenInfo(Screen.Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)]),
        new WelcomeMessage(Guid.NewGuid(), DnDOverlay.Core.Protocol.Protocol.AssetPath),
        new SceneSnapshotMessage(Screen, Build.SceneWith(Build.Item())),
        new ScenePatchMessage(new ScenePatch([new ScreenOp(Screen, new AddItem(Build.Item()))])),
    ];

    public static TheoryData<ProtocolMessage> AllMessages() => [.. Messages];

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Every_message_survives_the_round_trip(ProtocolMessage message)
    {
        var restored = ProtocolJson.Parse(ProtocolJson.Serialise(message));

        Assert.Equal(message, restored);
    }

    /// <summary>The envelope is <c>{ "t": "…" }</c>, and the wire name is the contract (rule 7).</summary>
    [Theory]
    [InlineData("Hello")]
    [InlineData("Welcome")]
    [InlineData("SceneSnapshot")]
    [InlineData("ScenePatch")]
    public void The_wire_names_are_the_ones_the_plan_promises(string wireName)
    {
        var payloads = Messages
            .Select(message => Encoding.UTF8.GetString(ProtocolJson.Serialise(message)))
            .ToList();

        Assert.Contains(payloads, json => json.Contains($"\"t\":\"{wireName}\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// A scene item is resolved over a fixed list of permitted types, never over a transmitted
    /// type name. That is the security property, not an optimisation (Part 4).
    /// </summary>
    [Fact]
    public void A_scene_item_carries_its_kind_and_comes_back_as_that_type()
    {
        var message = new SceneSnapshotMessage(Screen, Build.SceneWith(Build.Item()));

        var json = Encoding.UTF8.GetString(ProtocolJson.Serialise(message));
        var restored = (SceneSnapshotMessage)ProtocolJson.Parse(ProtocolJson.Serialise(message))!;

        Assert.Contains("\"kind\":\"image\"", json, StringComparison.Ordinal);
        Assert.IsType<ImageItem>(Assert.Single(restored.Scene.Items));
    }

    /// <summary>
    /// A type name that is not on the list must not resolve to anything. This is the test that
    /// would notice if somebody swapped the source-generated context for a reflection-based one.
    /// </summary>
    [Fact]
    public void An_unknown_type_name_does_not_resolve()
    {
        var hostile = Encoding.UTF8.GetBytes("""{"t":"System.Diagnostics.Process, System","name":"x"}""");

        Assert.Throws<JsonException>(() => ProtocolJson.Parse(hostile));
    }

    [Fact]
    public void Enums_travel_as_names_rather_than_numbers()
    {
        var background = new BackgroundItem(
            new AssetId(new string('c', 64)),
            Build.Meta(),
            "Waterdeep",
            ShowName: true,
            BackgroundFit.Contain,
            OffsetX: 0,
            OffsetY: 0,
            AnimationPaused: false);

        var message = new SceneSnapshotMessage(Screen, SceneState.Empty with { Background = background });

        var json = Encoding.UTF8.GetString(ProtocolJson.Serialise(message));

        Assert.Contains("\"Contain\"", json, StringComparison.Ordinal);
    }
}
