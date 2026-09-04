using System.Text;
using System.Text.Json;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Scene;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Tests.Protocol;

public sealed class ProtocolJsonTests
{
    private static readonly ScreenRef Screen =
        new(new DeviceId(Guid.NewGuid()), new ScreenId(@"\\?\DISPLAY#IVM1234#5&1a2b"));

    /// <summary>
    /// Declared BEFORE <see cref="Messages"/>: static field initialisers run in declaration
    /// order, so the other way round this is still null when the messages are built - and the
    /// symptom is a null argument, not a compile error.
    /// </summary>
    private static readonly ScreenContext Context = ScreenContext.Default(new PixelSize(1920, 1080), 96);

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

        // The Hello with everything it grew in M1b: the full effective parameter set as the
        // baseline of the two-sided configuration, and the scene the device still has - the two
        // fields a restarting control needs to take anything over at all (Part 4).
        new HelloMessage(
            Screen.Device,
            "TISCH-PC",
            "1.0.0",
            DnDOverlay.Core.Protocol.Protocol.Version,
            [new ScreenInfo(Screen.Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)],
            Token: "a-token",
            PairingCode: null,
            Settings: new ConfigUpdate(
                [new ScreenConfigUpdate(Screen.Screen, ScreenSettings.Of(Context, "Touch table"))],
                new DeviceSettings(LogLevel.Debug, LogLevel.Warning, KeepAwake: true)),
            Scenes: [new ScreenScene(Screen.Screen, Build.SceneWith(Build.Item()))]),

        new ScreensChangedMessage(
            [new ScreenInfo(Screen.Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(3840, 2160), 144, true)]),

        new ConfigUpdateMessage(new ConfigUpdate(
            [
                new ScreenConfigUpdate(
                    Screen.Screen,
                    new ScreenSettings(ParkEdge: ParkEdge.Bottom),
                    new ScreenCommand(ScreenState.Diagnostic, SuppressReason.ControlWindow)),
            ],
            new DeviceSettings(ForwardAtLeast: LogLevel.Debug, KeepAwake: false))),

        // The one switch for every device (M3c). It is a FIELD on a type this list already covered,
        // which is the hole this list cannot see by itself: the guard below compares TYPES, so a
        // field added to a covered one is exactly as invisible as a type added to an uncovered
        // list. Same shape as the ItemTransformed gap of M3a, one level down.
        new ConfigUpdateMessage(new ConfigUpdate([], TouchPoints: false)),

        // Carries nothing, and that is the statement: the device knows its own screens and what
        // each is called, so a list from the control would be a second copy of the names (Part 6).
        new IdentifyScreensMessage(),

        // The first message that is not state (Part 4). It names no device: the hub knows which
        // connection a reading came in on, and that is the answer a device cannot get wrong.
        new AssetProgressMessage(
        [
            new AssetLoad(new AssetId(new string('a', 64)), 0.4, AssetLoadState.Loading),
            new AssetLoad(new AssetId(new string('b', 64)), 1, AssetLoadState.Decoding),
        ]),

        // An intention going upwards, with the revision the display had when the hand took hold.
        new ItemTransformedMessage(
            Screen.Screen,
            new ItemTransform(new ItemId(Guid.Parse("22222222-0000-0000-0000-000000000001")), 0.3, 0.7, 0.25, 90),
            KnownRevision: 17,
            Grabbed: true),

        new ItemParkedMessage(
            Screen.Screen,
            new ItemId(Guid.Parse("22222222-0000-0000-0000-000000000002")),
            Parked: true),

        // Every finger on one screen in one message, each with its own identity and its own path
        // since the last send - two people pointing must not arrive as one zigzag (Part 4).
        new TouchPointsMessage(
            Screen.Screen,
            [
                new TouchTrail(7, [new TouchPoint(0.10, 0.20, 90), new TouchPoint(0.12, 0.24, 0)]),
                new TouchTrail(8, [new TouchPoint(0.80, 0.55, 40)]),
            ]),

        // The empty list is a statement and not an absence: the last finger has lifted (Part 4).
        new TouchPointsMessage(Screen.Screen, []),

        // The five that had gone over real sockets in the seam tests and through no round trip of
        // their own - which is how they stayed off this list without anybody noticing.
        new PairingPendingMessage("482 913"),
        new RejectedMessage(RejectionReason.Denied),
        new PingMessage(RoundTripMs: 12),
        new PongMessage(),
        new LogEntryMessage(
            3005,
            "AssetFailed",
            LogLevel.Warning,
            new DateTimeOffset(2026, 8, 18, 20, 15, 0, TimeSpan.FromHours(2)),
            [new LogValue("Asset", new string('a', 64)), new LogValue("Reason", "Unreadable")],
            RawText: null,
            Screen: Screen.Screen),
    ];

    public static TheoryData<ProtocolMessage> AllMessages() => [.. Messages];

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Every_message_survives_the_round_trip(ProtocolMessage message)
    {
        var restored = ProtocolJson.Parse(ProtocolJson.Serialise(message));

        Assert.Equal(message, restored);
    }

    /// <summary>
    /// The list above is hand-written, so this is what stops it drifting - and it is here because it
    /// was NOT, which cost a milestone's worth of coverage without anything going red.
    /// <para>
    /// <c>PatchOp</c> has had this guard since M1a; the messages did not, and M3a walked straight
    /// into the gap: <c>ItemTransformed</c> was added, registered, sent over a real socket in a seam
    /// test - and round-tripped by nothing. The one test that would have said so is this one.
    /// </para>
    /// <para>
    /// <b>The lesson is not "add the message to the list".</b> A mechanism that exists for one
    /// closed list and not for the closed list beside it is the shape to look for: the second list
    /// looks guarded because the first one is.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_message_is_covered_here()
    {
        var declared = typeof(ProtocolMessage).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ProtocolMessage)))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        var covered = Messages
            .Select(message => message.GetType().Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(declared, covered);
    }

    /// <summary>The envelope is <c>{ "t": "…" }</c>, and the wire name is the contract (rule 7).</summary>
    [Theory]
    [InlineData("Hello")]
    [InlineData("Welcome")]
    [InlineData("SceneSnapshot")]
    [InlineData("ScenePatch")]
    [InlineData("ScreensChanged")]
    [InlineData("ConfigUpdate")]
    [InlineData("IdentifyScreens")]
    [InlineData("AssetProgress")]
    [InlineData("ItemTransformed")]
    [InlineData("ItemParked")]
    [InlineData("PairingPending")]
    [InlineData("Rejected")]
    [InlineData("Ping")]
    [InlineData("Pong")]
    [InlineData("LogEntry")]
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

    /// <summary>
    /// Names rather than numbers, so that inserting a value into an enum cannot silently change
    /// what an older counterpart reads. Asked of <c>ParkEdge</c> since M4 - it used to be asked of
    /// the background's fit, and that is no longer a stored value.
    /// </summary>
    [Fact]
    public void Enums_travel_as_names_rather_than_numbers()
    {
        var message = new ConfigUpdateMessage(
            new ConfigUpdate([new ScreenConfigUpdate(Screen.Screen, new ScreenSettings(ParkEdge: ParkEdge.Left))]));

        var json = Encoding.UTF8.GetString(ProtocolJson.Serialise(message));

        Assert.Contains("\"Left\"", json, StringComparison.Ordinal);
    }
}
