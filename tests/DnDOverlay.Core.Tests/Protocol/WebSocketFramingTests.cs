using System.Net.WebSockets;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Core.Tests.Protocol;

/// <summary>
/// The ceiling on one message, and the reassembly beneath it.
/// <para>
/// Both ends read through this one place since <c>033d4d7</c> - before that the framing existed
/// twice, line for line, and the two ceilings agreed at 4 MB by coincidence. The duplication is
/// gone; what was still missing is a test of the ceiling ITSELF, so what held it up was that both
/// copies happened to say the same number.
/// </para>
/// <para>
/// It matters because the ceiling is a defence, not a tidy limit: without it, one end announcing a
/// continuation frame forever is an unbounded allocation in the other end's process - a display PC
/// with no keyboard, or the DM's control in front of the group (Part 4).
/// </para>
/// </summary>
public sealed class WebSocketFramingTests
{
    [Fact]
    public async Task A_message_arriving_in_pieces_is_handed_over_whole()
    {
        var socket = new Scripted([Piece("left half, "), Piece("right half", last: true)]);

        var message = await WebSocketFraming.ReceiveAsync(
            socket, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("left half, right half", System.Text.Encoding.UTF8.GetString(message!));
    }

    /// <summary>
    /// The ceiling counts what has ARRIVED so far, not what one frame carries. A sender that never
    /// says "end of message" is the case it exists for, and each of its frames is harmless on its
    /// own.
    /// </summary>
    [Fact]
    public async Task A_message_over_the_ceiling_ends_the_connection_instead_of_the_memory()
    {
        var socket = new Scripted([Piece(new string('x', 600)), Piece(new string('x', 600), last: true)]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WebSocketFraming.ReceiveAsync(
                socket, maxBytes: 1000, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("1000", failure.Message, StringComparison.Ordinal);

        // And it stopped at the frame that crossed the line rather than reading the rest first.
        Assert.Equal(2, socket.Reads);
    }

    /// <summary>A close is an ending, not a fault: the caller gets nothing back and says so.</summary>
    [Fact]
    public async Task A_closed_socket_yields_nothing()
    {
        var socket = new Scripted([new WebSocketReceiveResult(0, WebSocketMessageType.Close, true)]);

        Assert.Null(await WebSocketFraming.ReceiveAsync(
            socket, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static WebSocketReceiveResult Piece(string text, bool last = false) =>
        new(System.Text.Encoding.UTF8.GetByteCount(text), WebSocketMessageType.Text, last, Payload: text);

    /// <summary>
    /// A socket that hands over what the test wrote for it. A real pair would prove the same thing
    /// and would need a server for it; what is under test here is the reassembly and the counting,
    /// and both are ours.
    /// </summary>
    private sealed class Scripted(IReadOnlyList<WebSocketReceiveResult> script) : WebSocket
    {
        internal int Reads { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override string? SubProtocol => null;

        public override WebSocketState State => WebSocketState.Open;

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var step = script[Reads++];

            if (step.Payload is { } text)
            {
                System.Text.Encoding.UTF8.GetBytes(text).CopyTo(buffer);
            }

            return ValueTask.FromResult(new ValueWebSocketReceiveResult(
                step.Count, step.MessageType, step.EndOfMessage));
        }

        public override void Abort()
        {
        }

        public override void Dispose()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The framing reads through the Memory overload.");

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>One step of the script: how many bytes, of what kind, and whether it ends the message.</summary>
    private sealed record WebSocketReceiveResult(
        int Count, WebSocketMessageType MessageType, bool EndOfMessage, string? Payload = null);
}
