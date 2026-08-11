using System.Buffers;
using System.Net.WebSockets;

namespace DnDOverlay.Transport;

/// <summary>Reading one complete WebSocket message, however many frames it arrives in.</summary>
internal static class WebSocketMessages
{
    /// <summary>A ceiling on one incoming message, so a faulty control cannot exhaust the display.</summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    internal static async Task<byte[]?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
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

                if (message.Length + result.Count > MaxBytes)
                {
                    throw new InvalidOperationException($"Message exceeds the {MaxBytes} byte ceiling.");
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
