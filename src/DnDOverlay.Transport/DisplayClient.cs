using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport;

/// <summary>
/// The display's end of the WebSocket. It says <c>Hello</c>, then hands everything that arrives
/// to a channel the application reads - which keeps this type free of any notion of a UI thread
/// and lets it be tested without one.
/// <para>
/// M1a connects to a host that was configured, once, and gives up when it fails. Discovery,
/// pairing, the reconnect backoff and the heartbeat are M1b. What already holds: the address is
/// used and never remembered beyond the run, and the asset path comes from the
/// <see cref="WelcomeMessage"/> rather than from a stored base URL (Part 4, Part 5).
/// </para>
/// </summary>
public sealed class DisplayClient
{
    private readonly ILogger<DisplayClient> _logger;

    public DisplayClient(ILogger<DisplayClient> logger) => _logger = logger;

    /// <summary>
    /// Connects, announces itself and pumps incoming messages into <paramref name="inbox"/>
    /// until the connection ends or the token fires. The channel is completed on the way out, so
    /// a reader knows the difference between "quiet" and "over".
    /// </summary>
    /// <param name="ready">
    /// Handed the outgoing queues the moment the socket stands and before anything reads or
    /// writes on it. Everything this device says after the <c>Hello</c> goes through them - which
    /// is how the discard rules of Part 4 reach this end of the wire at all: until M3c the display
    /// had one unbounded channel, and touch points and gestures run on exactly that one.
    /// </param>
    public async Task RunAsync(
        Uri hubUri,
        HelloMessage hello,
        ChannelWriter<ProtocolMessage> inbox,
        SendLimits limits,
        Action<SendQueues> ready,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubUri);
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(ready);

        using var socket = new ClientWebSocket();
        using var over = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        using var outbox = new SendQueues(socket, limits, new HubReport(_logger, hubUri), TimeProvider.System);

        Task sending = Task.CompletedTask;

        try
        {
            TransportLog.Connecting(_logger, hubUri);
            await socket.ConnectAsync(hubUri, cancellationToken).ConfigureAwait(false);

            // Sent before the loops start, while nothing else can be writing. Everything after
            // this goes through the outbox, because two concurrent sends on one WebSocket are no
            // more allowed here than they are in the hub - which is why the Pong below is queued
            // rather than written where it is answered.
            await socket.SendAsync(
                ProtocolJson.Serialise(hello),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);

            // Only now: the queues own the socket, and nothing may be handed to them before the
            // Hello above is out - two concurrent sends on one WebSocket are not allowed.
            ready(outbox);

            sending = outbox.PumpAsync(over.Token);

            await ReceiveLoopAsync(socket, inbox, outbox, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (WebSocketException exception)
        {
            TransportLog.ConnectFailed(_logger, exception, hubUri);
        }
        finally
        {
            await over.CancelAsync().ConfigureAwait(false);

            await sending.ConfigureAwait(false);

            inbox.TryComplete();
            TransportLog.Disconnected(_logger, hubUri);
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        ChannelWriter<ProtocolMessage> inbox,
        SendQueues outbox,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await WebSocketFraming
                .ReceiveAsync(socket, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                return;
            }

            ProtocolMessage? message;

            try
            {
                message = ProtocolJson.Parse(payload);
            }
            catch (JsonException exception)
            {
                // An unknown message type is ignored and logged, never fatal - that is what
                // lets an older display face a newer control at all (Part 1, rule 7).
                TransportLog.UnknownMessageIgnored(_logger, exception);
                continue;
            }

            if (message is WelcomeMessage welcome)
            {
                TransportLog.Connected(_logger, welcome.ControlId, welcome.AssetPath);
            }

            // Answered here rather than by the application, because it says nothing about the
            // application: it says this socket is alive. It is also what tells a clone from a
            // crashed display coming straight back - the hub asks the connection it already has,
            // and silence is the answer that replaces it (Part 4).
            if (message is PingMessage)
            {
                _ = outbox.TrySend(new PongMessage());
            }

            if (message is not null)
            {
                await inbox.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
