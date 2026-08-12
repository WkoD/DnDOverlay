using System.Net.WebSockets;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The three queues in front of one socket.
/// <para>
/// Two of them carry no message yet - progress arrives with M2, the transient traffic with M3 -
/// so they are driven here by hand. A queue that has never had anything in it is a queue nobody
/// has proven, and the rules it enforces are exactly the ones that only show themselves under a
/// load that does not exist for another two milestones (Part 4, Part 10).
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
    /// Transient traffic drops its oldest and says nothing about it - that is ordinary operation,
    /// not an incident.
    /// </summary>
    [Fact]
    public async Task Transient_traffic_drops_the_oldest_without_a_word()
    {
        using var socket = new RecordingSocket();
        using var queues = Queues(socket, options => options.MaxTransientMessages = 2);

        for (var i = 1; i <= 5; i++)
        {
            Assert.True(queues.TrySend(Marked(i), SendClass.Transient));
        }

        int[] written = [9, 4, 5];

        Assert.Equal(written, await DrainAsync(queues, socket));
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
    /// Pings, distinguished by their round-trip field. All one type on the wire, so what is being
    /// read back is the queueing and nothing else.
    /// </summary>
    private static PingMessage Marked(int mark) => new(mark);

    private static SendQueues Queues(
        WebSocket socket,
        Action<HubOptions>? tune = null,
        TimeProvider? time = null)
    {
        var options = new HubOptions();

        tune?.Invoke(options);

        return new SendQueues(socket, "127.0.0.1", options, time ?? TimeProvider.System, NullLogger.Instance);
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
