using System.Threading.Channels;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// Sends this device's log entries to the control over the connection that is already there - no
/// second channel and no second port (Part 8).
/// <para>
/// The mark lives HERE and not in the connection, and that is the whole point of the ring buffer:
/// the most interesting messages of all come up while nothing can be sent, and they go out when
/// the connection comes back. A forwarder that started afresh on every attempt would lose exactly
/// those.
/// </para>
/// </summary>
/// <param name="atLeast">
/// The forwarding level, Warning by default. It is a separate knob from what the device PRODUCES:
/// the file keeps everything, the wire carries what is worth the DM's attention (Part 6).
/// </param>
internal sealed class LogForwarding(ProcessLog log, LogLevel atLeast)
{
    /// <summary>
    /// How many go out in one pass before the buffer is looked at again. Small enough that a
    /// backlog does not monopolise the socket, large enough that an ordinary evening never needs a
    /// second pass.
    /// </summary>
    private const int Batch = 64;

    private long _mark;

    /// <summary>
    /// Runs for the length of one connection. It does NOT log anything itself - a line about
    /// forwarding would produce a line to forward, and that is a loop with no bottom (Part 8).
    /// </summary>
    public async Task RunAsync(ChannelWriter<ProtocolMessage> outbox, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        // Starts at one, so whatever came up while there was no connection goes out at once
        // instead of waiting for the next message to happen.
        using var work = new SemaphoreSlim(1, 1);

        void Wake(LogRecord _)
        {
            try
            {
                work.Release();
            }
            catch (SemaphoreFullException)
            {
                // A pass is already pending; it will pick this up as well.
            }
            catch (ObjectDisposedException)
            {
                // The connection ended between the entry and this.
            }
        }

        log.Added += Wake;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await work.WaitAsync(cancellationToken).ConfigureAwait(false);

                // What fell out of the ring before it could be sent is counted per reader and
                // deliberately not reported from here: saying so would be a line to forward.
                while (log.Ring.Since(_mark, atLeast, Batch, out var next, out _) is { Count: > 0 } batch)
                {
                    foreach (var record in batch)
                    {
                        await outbox.WriteAsync(Message(record), cancellationToken).ConfigureAwait(false);
                    }

                    // Moved only after the entries are in the outbox, so a connection that ends
                    // mid-batch leaves them to the next one rather than dropping them.
                    _mark = next;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The connection ended, or the application is going away.
        }
        catch (ChannelClosedException)
        {
            // The socket went first; the mark stays where it was and the next connection resumes.
        }
        finally
        {
            log.Added -= Wake;
        }
    }

    private static LogEntryMessage Message(LogRecord record) =>
        new(
            record.EventId,
            record.EventName,
            record.Level,
            record.At,
            record.Values,
            record.RawText,
            record.Screen);
}
