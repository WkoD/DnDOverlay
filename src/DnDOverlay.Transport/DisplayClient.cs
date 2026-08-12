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
    public async Task RunAsync(
        Uri hubUri,
        HelloMessage hello,
        ChannelWriter<ProtocolMessage> inbox,
        Channel<ProtocolMessage> outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubUri);
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(outbox);

        using var socket = new ClientWebSocket();
        using var over = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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

            sending = SendLoopAsync(socket, outbox.Reader, over.Token);

            await ReceiveLoopAsync(socket, inbox, outbox.Writer, cancellationToken).ConfigureAwait(false);
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
            outbox.Writer.TryComplete();

            await sending.ConfigureAwait(false);

            inbox.TryComplete();
            TransportLog.Disconnected(_logger, hubUri);
        }
    }

    /// <summary>
    /// The one writer on this socket. Everything the display says after the Hello passes here -
    /// the Pong and the forwarded log entries - so that "exactly one sender" is a property of the
    /// construction rather than a rule somebody has to remember.
    /// </summary>
    private static async Task SendLoopAsync(
        ClientWebSocket socket,
        ChannelReader<ProtocolMessage> outbox,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in outbox.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await socket.SendAsync(
                    ProtocolJson.Serialise(message),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
            // The connection ended. The receive side reports it; saying it twice would be noise.
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        ChannelWriter<ProtocolMessage> inbox,
        ChannelWriter<ProtocolMessage> outbox,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await WebSocketMessages
                .ReceiveAsync(socket, cancellationToken)
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
                outbox.TryWrite(new PongMessage());
            }

            if (message is not null)
            {
                await inbox.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
