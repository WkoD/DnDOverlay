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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubUri);
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(inbox);

        using var socket = new ClientWebSocket();

        try
        {
            TransportLog.Connecting(_logger, hubUri);
            await socket.ConnectAsync(hubUri, cancellationToken).ConfigureAwait(false);

            await socket.SendAsync(
                ProtocolJson.Serialise(hello),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);

            await ReceiveLoopAsync(socket, inbox, cancellationToken).ConfigureAwait(false);
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
            inbox.TryComplete();
            TransportLog.Disconnected(_logger, hubUri);
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        ChannelWriter<ProtocolMessage> inbox,
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
                await socket.SendAsync(
                    ProtocolJson.Serialise(new PongMessage()),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }

            if (message is not null)
            {
                await inbox.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
