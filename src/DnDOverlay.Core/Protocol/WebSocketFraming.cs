using System.Buffers;
using System.Net.WebSockets;

namespace DnDOverlay.Core.Protocol;

/// <summary>
/// Reading one complete WebSocket message, however many frames it arrives in - and ending a
/// connection politely.
/// <para>
/// <b>It lives in Core because both ends need it and neither may know the other.</b> It stood
/// twice, line for line, in <c>Hub</c> and in <c>Transport</c>, with the ceiling as a parameter on
/// one side and a constant on the other - and the two agreed on four megabytes by coincidence
/// rather than by decision. That kind of drift is silent where it hurts most: a message between
/// the two ceilings is taken by one end and refused by the other, and the display simply hangs up
/// (Part 1, "exactly once").
/// </para>
/// <para>
/// Writing is deliberately NOT here. On the hub the socket is owned by the send queues, so "exactly
/// one writer" is a property of the construction rather than a rule - and two concurrent sends on
/// one WebSocket are forbidden (Part 4).
/// </para>
/// </summary>
public static class WebSocketFraming
{
    /// <summary>
    /// The ceiling on one incoming message, and it is one number for both ends. An incoming
    /// message is untrusted even from a paired device: a fault or a taken-over display PC could
    /// otherwise walk the other side out of memory with a single <c>Hello</c> (Part 4).
    /// </summary>
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Ends a connection in the orderly way, so the other end sees a close rather than a socket
    /// that stops answering. The read loop on this side notices it too and unwinds without
    /// anybody having to abort anything.
    /// </summary>
    public static async Task CloseAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);

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
    /// The ceiling, defaulting to <see cref="MaxMessageBytes"/>. It stays a parameter because the
    /// hub's limits table may narrow it per endpoint - what must not happen is each end inventing
    /// its own.
    /// </param>
    public static async Task<byte[]?> ReceiveAsync(
        WebSocket socket,
        int maxBytes = MaxMessageBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);

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
                    throw new InvalidOperationException($"Message exceeds the {maxBytes} byte ceiling.");
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
