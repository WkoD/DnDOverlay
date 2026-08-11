using System.Buffers;
using System.Net.WebSockets;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>Reading one complete WebSocket message, however many frames it arrives in.</summary>
internal static class WebSocketMessages
{
    /// <summary>
    /// Writes one message straight to the socket, for the few that go out before there is a
    /// connection object with its own send loop: the pairing answers and the refusals.
    /// </summary>
    internal static async Task SendAsync(
        WebSocket socket,
        ProtocolMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await socket.SendAsync(
                ProtocolJson.Serialise(message),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Telling a device that already went away is not worth a line, let alone a throw.
        }
    }

    /// <summary>
    /// Ends a connection in the orderly way, so the other end sees a close rather than a socket
    /// that stops answering. The read loop on this side notices it too and unwinds without
    /// anybody having to abort anything.
    /// </summary>
    internal static async Task CloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                statusDescription: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Closing something the other end already dropped is not worth a line.
        }
        catch (OperationCanceledException)
        {
            // Shutting down while being polite.
        }
    }

    /// <summary>
    /// Reads until the end of a message, or returns <see langword="null"/> when the other end
    /// closed.
    /// </summary>
    /// <param name="maxBytes">
    /// A hard ceiling, because an incoming message is untrusted even from a paired device: a
    /// fault or a taken-over display PC could otherwise walk the control out of memory with one
    /// <c>Hello</c> (Part 4). The real limits table arrives with M1b; this is the floor under it.
    /// </param>
    internal static async Task<byte[]?> ReceiveAsync(
        WebSocket socket,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        var message = new MemoryStream();

        try
        {
            while (true)
            {
                var result = await socket
                    .ReceiveAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (message.Length + result.Count > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"Message exceeds the {maxBytes} byte ceiling.");
                }

                message.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    return message.ToArray();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await message.DisposeAsync().ConfigureAwait(false);
        }
    }
}
