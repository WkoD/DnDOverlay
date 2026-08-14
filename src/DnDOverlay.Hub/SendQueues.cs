using System.Net.WebSockets;
using System.Threading.Channels;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// The three queues in front of one socket, and the single loop that writes them.
/// <para>
/// Three rather than one, because <see cref="BoundedChannelFullMode"/> is a property of the
/// channel and not of the message - a single channel cannot both "discard transient" and "never
/// discard state". And three rather than two, because rank 3 stands above rank 4 (Part 1): the
/// feedback <i>that</i> something is being transferred must not lie in the same drawer as the
/// touch points, or under load the first thing to fall away is the very display that explains
/// the load.
/// </para>
/// <para>
/// It owns the socket, and that is deliberate. "Exactly one writer per socket" is then a property
/// of the construction rather than a rule somebody has to keep - and it has to be kept from the
/// moment the socket is accepted, because the heartbeat runs during the pairing wait as well
/// (Part 4). Two concurrent sends on one WebSocket are not allowed.
/// </para>
/// </summary>
internal sealed class SendQueues : IDisposable
{
    private readonly WebSocket _socket;
    private readonly string _address;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _writeTimeout;
    private readonly long _maxStateBytes;

    private readonly Channel<byte[]> _state;
    private readonly Channel<byte[]> _progress;
    private readonly Channel<byte[]> _transient;

    /// <summary>
    /// One signal for all three queues. Waiting on three channels at once would leave a waiter
    /// behind in every channel that did not win the race, and those pile up per iteration.
    /// A permit too many only costs a wake-up that finds nothing.
    /// </summary>
    private readonly SemaphoreSlim _work = new(0);

    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _stateBytes;
    private volatile bool _finishing;

    /// <param name="address">
    /// Who is at the other end. The address rather than the device, because these queues exist
    /// from the moment the socket is accepted - before any <c>Hello</c> has said which device this
    /// is. It is also what a person uses to tell two connections apart while setting up (Part 3).
    /// </param>
    internal SendQueues(WebSocket socket, string address, HubOptions options, TimeProvider time, ILogger logger)
    {
        _socket = socket;
        _address = address;
        _time = time;
        _logger = logger;
        _writeTimeout = options.WriteTimeout;
        _maxStateBytes = options.MaxStateBytes;

        // Wait, so that a full state queue fails the TryWrite instead of blocking the caller.
        // Blocking would carry the slowest device's backlog into the hub's own command path.
        _state = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.MaxStateMessages)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

        // One slot, overwritten: the newer reading is the right one.
        _progress = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        _transient = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.MaxTransientMessages)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    /// <summary>
    /// Fires when this connection is over: displaced by a newer one for the same device, silent
    /// for too long, not taking anything, or refusing to be written to at all. Whoever owns the
    /// socket watches this and unwinds - nobody reaches into another handler's socket.
    /// </summary>
    internal CancellationToken Closing => _closing.Token;

    /// <summary>Completes once the pump has stopped, whether it drained or gave up.</summary>
    internal Task Drained => _drained.Task;

    /// <summary>Queues a message in the class it belongs to.</summary>
    internal bool TrySend(ProtocolMessage message) => TrySend(message, SendClasses.Of(message));

    /// <summary>
    /// Queues a message in a class chosen by the caller. Only the tests use this - it is how the
    /// two rear queues can be driven before there is any message that belongs in them (Part 10).
    /// </summary>
    internal bool TrySend(ProtocolMessage message, SendClass @class)
    {
        var payload = ProtocolJson.Serialise(message);

        var queued = @class switch
        {
            SendClass.Progress => Offer(_progress, payload),
            SendClass.Transient => Offer(_transient, payload),
            _ => OfferState(payload),
        };

        if (queued)
        {
            _work.Release();
        }

        return queued;
    }

    /// <summary>
    /// Queues a last message and asks the pump to close the socket once it is out. Used where the
    /// message is the whole point of the connection ending - a refusal, which the device is meant
    /// to act on and cannot if the socket merely stops answering (Part 4).
    /// </summary>
    internal void Finish(ProtocolMessage last)
    {
        _ = TrySend(last, SendClass.State);
        _finishing = true;
        _work.Release();
    }

    /// <summary>Ends this connection. What happens next belongs to whoever owns the socket.</summary>
    internal void RequestClose()
    {
        if (!_closing.IsCancellationRequested)
        {
            _closing.Cancel();
        }
    }

    /// <summary>The single writer. Everything that goes onto this socket comes through here.</summary>
    internal async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _work.WaitAsync(cancellationToken).ConfigureAwait(false);

                while (TryTake(out var payload))
                {
                    if (!await WriteAsync(payload, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                if (_finishing)
                {
                    await WebSocketFraming.CloseAsync(_socket, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down, or this connection was ended elsewhere. Both ordinary.
        }
        finally
        {
            _drained.TrySetResult();
        }
    }

    public void Dispose()
    {
        _closing.Dispose();
        _work.Dispose();
    }

    /// <summary>
    /// State first, then progress, then what is merely current. The precedence of Part 1 then
    /// arises on its own: under sustained load the touch points stop getting a turn while the
    /// progress still does, and nothing had to throttle either of them explicitly.
    /// </summary>
    private bool TryTake(out byte[] payload)
    {
        if (_state.Reader.TryRead(out payload!))
        {
            Interlocked.Add(ref _stateBytes, -payload.Length);
            return true;
        }

        return _progress.Reader.TryRead(out payload!) || _transient.Reader.TryRead(out payload!);
    }

    private static bool Offer(Channel<byte[]> queue, byte[] payload) => queue.Writer.TryWrite(payload);

    /// <summary>
    /// Bounded by count <b>and</b> by bytes, because one <c>SceneSnapshot</c> with twenty items
    /// weighs as much as a hundred small messages.
    /// <para>
    /// A failed write here is a deterministic condition, not a time window, and it means neither
    /// "drop" nor "wait": it means this connection can no longer be held consistent. It is closed,
    /// and the ordinary reconnect with its <c>Hello</c> and <c>SceneSnapshot</c> puts the truth
    /// back (Part 4).
    /// </para>
    /// </summary>
    private bool OfferState(byte[] payload)
    {
        if (Interlocked.Add(ref _stateBytes, payload.Length) <= _maxStateBytes
            && _state.Writer.TryWrite(payload))
        {
            return true;
        }

        Interlocked.Add(ref _stateBytes, -payload.Length);
        HubLog.StateQueueFull(_logger, _address, _state.Reader.Count, Interlocked.Read(ref _stateBytes));
        RequestClose();

        return false;
    }

    /// <summary>
    /// One write, under a time limit at the socket. A counterpart that holds the connection open
    /// and takes nothing off it would otherwise fill the queue before anyone noticed - the
    /// heartbeat catches it eventually, this catches it sooner and caps the memory meanwhile
    /// (Part 4).
    /// <para>
    /// The limit cancels the send rather than merely stopping the wait, which aborts the socket.
    /// That is the intent: a peer that has not accepted a message in ten seconds is gone.
    /// </para>
    /// </summary>
    private async Task<bool> WriteAsync(byte[] payload, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_writeTimeout, _time);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await _socket
                .SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, attempt.Token)
                .ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            HubLog.WriteTimedOut(_logger, _address, _writeTimeout);
            RequestClose();

            return false;
        }
        catch (WebSocketException exception)
        {
            HubLog.SendFailed(_logger, exception, _address);
            RequestClose();

            return false;
        }
    }
}
