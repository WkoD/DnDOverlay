using System.Net.WebSockets;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The three queues in front of one socket.
/// <para>
/// The rules are driven here by hand, with pings standing in for real traffic, so that what is
/// being read is the queueing and nothing else. <b>The transient rank still carries no real
/// message</b> - touch points arrive with M3 - and a queue that has never had anything in it is a
/// queue nobody has proven (Part 4, Part 10). The progress rank stopped being one of those in M2b,
/// and the counter-check at the bottom is where it is put under a real load.
/// </para>
/// <para>
/// Nothing runs the pump while messages are being queued, so what comes out afterwards is a fact
/// about the queues rather than a race with them.
/// </para>
/// </summary>
public sealed class SendQueuesTests
{
    /// <summary>
    /// State first, then progress, then what is merely current. Under sustained load the touch
    /// points then stop getting a turn while the progress still does - and nothing had to throttle
    /// either of them (Part 1, precedence; Part 4).
    /// </summary>
    [Fact]
    public async Task What_must_arrive_goes_before_what_may_be_dropped()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket);

        Assert.True(queues.TrySend(Marked(3), SendClass.Transient));
        Assert.True(queues.TrySend(Marked(2), SendClass.Progress));
        Assert.True(queues.TrySend(Marked(1), SendClass.State));

        int[] written = [1, 9, 2, 3];

        Assert.Equal(written, await DrainAsync(queues, socket));
    }

    /// <summary>
    /// The state queue never discards. Five messages queued while nothing is reading them are five
    /// messages, in the order they were given.
    /// </summary>
    [Fact]
    public async Task State_is_never_dropped_and_keeps_its_order()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket);

        for (var i = 1; i <= 5; i++)
        {
            Assert.True(queues.TrySend(Marked(i), SendClass.State));
        }

        int[] written = [1, 2, 3, 4, 5, 9];

        Assert.Equal(written, await DrainAsync(queues, socket));
    }

    /// <summary>
    /// One slot, overwritten. A progress reading from a moment ago is worthless, not inaccurate -
    /// which is also why it does not belong in the queue that may never discard: a slow socket
    /// would fill it with numbers that overtake one another and end the connection over a display.
    /// </summary>
    [Fact]
    public async Task Progress_keeps_only_the_newest_reading()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket);

        Assert.True(queues.TrySend(Marked(1), SendClass.Progress));
        Assert.True(queues.TrySend(Marked(2), SendClass.Progress));
        Assert.True(queues.TrySend(Marked(3), SendClass.Progress));

        int[] written = [9, 3];

        Assert.Equal(written, await DrainAsync(queues, socket));
    }

    /// <summary>
    /// Transient traffic keeps a slot per kind and the newer reading replaces the older one -
    /// silently, because that is ordinary operation and not an incident.
    /// <para>
    /// <b>This replaces the floor that stood here until M3c.</b> Until the transient messages
    /// existed there was one small queue dropping its oldest, and the option that sized it said so
    /// in its own comment. Part 4 gives each kind a capacity of ONE; five readings of one kind are
    /// therefore one message, not the last two of five.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Transient_traffic_keeps_one_of_each_kind_without_a_word()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket);

        for (var i = 1; i <= 5; i++)
        {
            Assert.True(queues.TrySend(Marked(i), SendClass.Transient));
        }

        int[] written = [9, 5];

        Assert.Equal(written, await DrainAsync(queues, socket));
    }

    /// <summary>
    /// Two kinds are two slots, and neither pushes the other out - which is what "capacity one per
    /// kind" has to mean if the diagnostics of M5 are not to be silenced by a hand on the table.
    /// <para>
    /// The touch points of two screens are two kinds by the same rule: a table with two screens has
    /// two independent sets of fingers on it (Part 4).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_screens_worth_of_fingers_do_not_push_each_other_out()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket, time: new ManualTime());

        Assert.True(queues.TrySend(Fingers("DISPLAY1", 0.1)));
        Assert.True(queues.TrySend(Fingers("DISPLAY2", 0.2)));
        Assert.True(queues.TrySend(Fingers("DISPLAY1", 0.3)));

        var sent = await FingersOutAsync(queues, socket);

        Assert.Equal(2, sent.Count);

        // The first screen kept its place in the order rather than going to the back when it was
        // replaced: a busy hand must not be able to hold a quieter screen behind it for ever. And
        // it kept both of its points, because replacing a trail combines it.
        Assert.Equal("DISPLAY1", sent[0].Screen.Value);
        Assert.Equal([0.1, 0.3], Assert.Single(sent[0].Touches).Points.Select(point => point.X));
        Assert.Equal("DISPLAY2", sent[1].Screen.Value);
        Assert.Equal([0.2], Assert.Single(sent[1].Touches).Points.Select(point => point.X));
    }

    /// <summary>
    /// The one transient that does not simply overwrite: the trails are combined, so what is
    /// discarded is the delay and never the movement (Part 4).
    /// </summary>
    [Fact]
    public async Task A_replaced_trail_keeps_the_points_it_displaced()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket, time: new ManualTime());

        Assert.True(queues.TrySend(Fingers("DISPLAY1", 0.1, 0.2)));
        Assert.True(queues.TrySend(Fingers("DISPLAY1", 0.3)));

        var sent = Assert.Single(await FingersOutAsync(queues, socket));

        Assert.Equal([0.1, 0.2, 0.3], Assert.Single(sent.Touches).Points.Select(point => point.X));
    }

    /// <summary>
    /// A full state queue means neither drop nor wait: this connection can no longer be held
    /// consistent, so it ends and the ordinary reconnect puts the truth back. Waiting would carry
    /// the slowest device's backlog into the hub's own command path (Part 4).
    /// </summary>
    [Fact]
    public void A_full_state_queue_ends_the_connection_instead_of_waiting()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket, options => options.MaxStateMessages = 2);

        Assert.True(queues.TrySend(Marked(1), SendClass.State));
        Assert.True(queues.TrySend(Marked(2), SendClass.State));

        Assert.False(queues.Closing.IsCancellationRequested);
        Assert.False(queues.TrySend(Marked(3), SendClass.State));
        Assert.True(queues.Closing.IsCancellationRequested);
    }

    /// <summary>
    /// And by bytes as well, because one <c>SceneSnapshot</c> with twenty items weighs as much as
    /// a hundred small messages. Counting only messages would let a few big ones through where a
    /// great many small ones are refused.
    /// </summary>
    [Fact]
    public void The_byte_ceiling_bites_before_the_count_does()
    {
        using var socket = new RecordingSocket();
        var one = ProtocolJson.Serialise(Marked(1)).Length;

        using var queues = Queues(socket, options =>
        {
            options.MaxStateMessages = 256;
            options.MaxStateBytes = (2 * one) + 1;
        });

        Assert.True(queues.TrySend(Marked(1), SendClass.State));
        Assert.True(queues.TrySend(Marked(2), SendClass.State));
        Assert.False(queues.TrySend(Marked(3), SendClass.State));
        Assert.True(queues.Closing.IsCancellationRequested);
    }

    /// <summary>
    /// A sender that races the end of a connection is told <b>no</b>, not thrown at.
    /// <para>
    /// It is an ordinary race rather than a fault: the socket is disposed where it was opened,
    /// while a hand on the table, the log forwarder and the two reporters are all still running -
    /// they stop a continuation later, when the application notices the connection has ended.
    /// </para>
    /// <para>
    /// <b>It is also a regression this test exists for.</b> Until M3c the display wrote into a
    /// channel, and a completed channel refuses quietly; moving both ends onto these queues turned
    /// that into an <c>ObjectDisposedException</c> - on the UI thread it would have taken the
    /// display down, and in the forwarder it would have ended the reconnect loop until a restart.
    /// </para>
    /// <para>
    /// And it has to be <b>false</b> rather than a silent yes: the forwarder moves its mark on when
    /// a line is accepted, so a lie here would lose exactly the entries that explain the
    /// disconnection.
    /// </para>
    /// </summary>
    [Fact]
    public void A_disposed_set_of_queues_refuses_instead_of_throwing()
    {
        using var socket = new RecordingSocket();

        var queues = Queues(socket);

        queues.Dispose();

        Assert.False(queues.TrySend(Marked(1), SendClass.State));
        Assert.False(queues.TrySend(Marked(2), SendClass.Progress));
        Assert.False(queues.TrySend(Fingers("DISPLAY1", 0.5)));

        // Whoever owns the socket may still say the connection is over; it is over already.
        queues.RequestClose();
    }

    /// <summary>
    /// A counterpart that holds the connection open and takes nothing off it would otherwise only
    /// be noticed once the queue had filled - late, and after the memory had already been spent.
    /// The write limit catches it sooner, and it cancels the send rather than merely giving up on
    /// the wait, which aborts the socket. That is the intent (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_write_that_never_completes_ends_the_connection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();

        using var socket = new RecordingSocket { Blocked = new TaskCompletionSource() };
        using var queues = Queues(socket, options => options.WriteTimeout = TimeSpan.FromSeconds(10), time);

        var pump = queues.PumpAsync(cancellationToken);

        Assert.True(queues.TrySend(Marked(1), SendClass.State));

        await Until(() => socket.Writing, cancellationToken);

        time.Advance(TimeSpan.FromSeconds(11));

        await pump;

        Assert.True(queues.Closing.IsCancellationRequested);
        Assert.Empty(socket.Written);
    }

    /// <summary>
    /// <b>The counter-check, and it is the other direction from every test above.</b> Those stage a
    /// limit and show it bites; this one stages the evening the program is actually for and shows
    /// that <b>nothing bites at all</b> - four devices, twenty items on each scene, all connected,
    /// against the REAL defaults.
    /// <para>
    /// It was passed once, in M1b, and it was worth little there: <c>AssetProgress</c> did not
    /// exist, so the rank that shares the socket with the state queue was empty. M2b noted that it
    /// had to run again with that traffic in it and then did not run it - the posten fell between
    /// the derivation and the milestone's close, which is why it is written down as a test now
    /// rather than walked once by hand (checks/M2.md).
    /// </para>
    /// <para>
    /// <b>Four sockets, not one bigger queue.</b> The queues hang off the socket, so four devices
    /// are four of these - and staging them as one would quietly test a load four times too heavy
    /// against a ceiling that is per connection (Part 4).
    /// </para>
    /// <para>
    /// The pump never runs, which is the worst honest case: every message of the burst is in the
    /// queue at once, as it would be behind a socket that has stopped taking anything for a moment.
    /// </para>
    /// </summary>
    [Fact]
    public void A_realistic_evening_trips_no_limit()
    {
        var defaults = new HubOptions();
        var heaviest = 0;

        for (var device = 0; device < 4; device++)
        {
            using var socket = new RecordingSocket();
            using var queues = Queues(socket);

            var screen = new ScreenRef(new DeviceId(Guid.NewGuid()), new ScreenId("DISPLAY" + device));
            var assets = Enumerable.Range(0, 20).Select(Asset).ToList();
            var queued = 0L;

            // How a scene of twenty comes about: the snapshot the device gets on connecting, then
            // one patch per picture as the DM sends them.
            void Send(ProtocolMessage message, SendClass rank)
            {
                var bytes = ProtocolJson.Serialise(message).Length;

                heaviest = Math.Max(heaviest, bytes);

                if (rank is SendClass.State)
                {
                    queued += bytes;
                }

                Assert.True(queues.TrySend(message, rank), $"{message.GetType().Name} was refused");
            }

            Send(new SceneSnapshotMessage(screen, Scene(assets)), SendClass.State);

            for (var item = 0; item < 20; item++)
            {
                Send(
                    new ScenePatchMessage(new ScenePatch(
                        [new ScreenOp(screen, new AddItem(Item(assets[item])))])),
                    SendClass.State);

                // And the progress that goes with them, every picture in every reading - the rank
                // that was empty when this check last ran.
                Send(
                    new AssetProgressMessage(
                        [.. assets.Select(asset => new AssetLoad(asset, 0.5, AssetLoadState.Loading))]),
                    SendClass.Progress);
            }

            Assert.False(
                queues.Closing.IsCancellationRequested,
                $"device {device} tripped a limit on an ordinary evening");

            // Not merely under the ceiling - far under it, so that this stays an answer as scenes
            // grow. A burst that used nine tenths of the room would pass today and say nothing.
            Assert.True(
                queued * 4 < defaults.MaxStateBytes,
                $"the state burst was {queued} bytes against a ceiling of {defaults.MaxStateBytes}");
        }

        // The largest single message an evening produces, against the ceiling meant to catch a
        // runaway one. Stated as a number so a future scene that quadruples it fails here.
        Assert.True(
            heaviest * 8 < defaults.MaxStateBytes,
            $"the heaviest message was {heaviest} bytes against a ceiling of {defaults.MaxStateBytes}");
    }

    private static AssetId Asset(int n) =>
        new(n.ToString(null as IFormatProvider).PadLeft(64, 'e'));

    /// <summary>A scene as a table really carries it: a background and twenty named pictures.</summary>
    private static SceneState Scene(IReadOnlyList<AssetId> assets) =>
        SceneState.Empty with
        {
            Background = new BackgroundItem(
                new AssetId(new string('f', 64)), Meta(), "Sturmkueste", ShowName: false,
                BackgroundFit.Cover, OffsetX: 0, OffsetY: 0, AnimationPaused: false),
            Items = [.. assets.Select(Item)],
        };

    private static ImageItem Item(AssetId asset) =>
        new(
            ItemId: new ItemId(Guid.NewGuid()),
            CenterX: 0.5,
            CenterY: 0.5,
            Scale: 0.4,
            AspectRatio: 4d / 3d,
            RotationDeg: 0,
            ZOrder: 0,
            Locked: false,
            Parked: false,
            Revision: 1,
            AssetId: asset,
            Meta: Meta(),
            Name: "Dilwyn Kemri von den Hazim'Tor",
            ShowName: true,
            AnimationPaused: false);

    private static AssetMeta Meta() =>
        new(1920, 1080, "png", Bytes: 2_400_000, IsAnimated: false, ContentHash: new string('a', 64));

    /// <summary>
    /// Pings, distinguished by their round-trip field. All one type on the wire, so what is being
    /// read back is the queueing and nothing else.
    /// </summary>
    private static PingMessage Marked(int mark) => new(mark);

    /// <summary>One finger on one screen, at the given places, all of them just touched.</summary>
    private static TouchPointsMessage Fingers(string screen, params double[] xs) =>
        new(new ScreenId(screen), [new TouchTrail(1, [.. xs.Select(x => new TouchPoint(x, 0.5, 0))])]);

    /// <summary>
    /// Runs the pump to the end and reads back only the touch points. The sentinel that stops it
    /// is a state message, so it is written first and filtered out here.
    /// </summary>
    private static async Task<List<TouchPointsMessage>> FingersOutAsync(
        SendQueues queues,
        RecordingSocket socket)
    {
        queues.Finish(Marked(9));

        await queues.PumpAsync(CancellationToken.None);

        return [.. socket.Written.Select(payload => ProtocolJson.Parse(payload)).OfType<TouchPointsMessage>()];
    }

    private static SendQueues Queues(
        WebSocket socket,
        Action<HubOptions>? tune = null,
        TimeProvider? time = null)
    {
        var options = new HubOptions();

        tune?.Invoke(options);

        return new SendQueues(
            socket,
            options.SendLimits,
            new SocketReport("127.0.0.1", NullLogger.Instance),
            time ?? TimeProvider.System);
    }

    /// <summary>
    /// Runs the pump until everything queued is out and the socket has been closed, then reads the
    /// marks off the wire. <c>9</c> is the sentinel that ends the connection: it goes through the
    /// state queue, so it also shows where the state group ends.
    /// </summary>
    private static async Task<int[]> DrainAsync(SendQueues queues, RecordingSocket socket)
    {
        queues.Finish(Marked(9));

        await queues.PumpAsync(CancellationToken.None);

        return [.. socket.Written.Select(payload =>
            (int)(((PingMessage)ProtocolJson.Parse(payload)!).RoundTripMs ?? 0))];
    }

    private static async Task Until(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>A socket that keeps what was written to it, and can be made to stop taking it.</summary>
    private sealed class RecordingSocket : WebSocket
    {
        private readonly List<byte[]> _written = [];

        internal IReadOnlyList<byte[]> Written => _written;

        /// <summary>Set to hold every send. Left unset, the socket takes everything at once.</summary>
        internal TaskCompletionSource? Blocked { get; init; }

        /// <summary>Whether a send is sitting in <see cref="Blocked"/> right now.</summary>
        internal bool Writing { get; private set; }

        internal bool Closed { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => Closed ? WebSocketState.Closed : WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort() => Closed = true;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            Closed = true;

            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => Closed = true;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<WebSocketReceiveResult>(new CancellationToken(canceled: true));

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (Blocked is not null)
            {
                Writing = true;

                await Blocked.Task.WaitAsync(cancellationToken);
            }

            _written.Add(buffer.ToArray());
        }
    }
}
