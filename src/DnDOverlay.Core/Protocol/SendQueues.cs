using System.Net.WebSockets;
using System.Threading.Channels;

namespace DnDOverlay.Core.Protocol;

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
/// <b>It sits in Core and serves both ends of the wire.</b> Until M3 the hub owned it and the
/// display had a single unbounded channel - and touch points and gestures run on exactly that
/// one. A second version of the discard rules would be the seam of M2 with another name
/// (<c>WebSocketFraming</c> stood twice, and the ends drifted until one stopped sending a token).
/// The classes belong to the protocol, so the queues do too (M3, siting question 2).
/// </para>
/// <para>
/// It owns the socket, and that is deliberate. "Exactly one writer per socket" is then a property
/// of the construction rather than a rule somebody has to keep - and it has to be kept from the
/// moment the socket is accepted, because the heartbeat runs during the pairing wait as well
/// (Part 4). Two concurrent sends on one WebSocket are not allowed.
/// </para>
/// </summary>
public sealed class SendQueues : IMessageSink, IDisposable
{
    private readonly WebSocket _socket;
    private readonly TimeProvider _time;
    private readonly ISendReport _report;
    private readonly TimeSpan _writeTimeout;
    private readonly long _maxStateBytes;

    private readonly Channel<byte[]> _state;
    private readonly Channel<byte[]> _progress;

    /// <summary>
    /// Rank 4, one slot per kind. It holds MESSAGES where the other two hold bytes, and it has to:
    /// replacement may mean merging, and a merge needs the content. It is also the only tier whose
    /// item changes between being queued and being written - a trail's ages are relative to the
    /// moment it goes out, so the wait is charged to it here rather than guessed at the far end.
    /// </summary>
    private readonly TransientSlots<ProtocolMessage> _transient;

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

    /// <summary>
    /// Set the moment this is disposed, so a sender that races the end of a connection is told
    /// <b>no</b> instead of being thrown at.
    /// <para>
    /// It is an ordinary race and not a fault: the socket is disposed where it was opened, while a
    /// hand on the table, the log forwarder and the two reporters are all still running - they stop
    /// when the application notices the connection has ended, which is a continuation later. Until
    /// M3c this end wrote into a channel, and a completed channel simply refuses; the move to these
    /// queues turned that quiet refusal into an <c>ObjectDisposedException</c> on the UI thread and
    /// in the forwarder, where it would have ended the reconnect loop for good.
    /// </para>
    /// </summary>
    private volatile bool _disposed;

    /// <param name="report">
    /// Where the three things that can go wrong on a socket are said. It is handed in rather than
    /// logged from here, because the event identifiers belong to the process that owns the
    /// connection: the hub names the address it was talking to, the display names the hub (Part 8).
    /// </param>
    public SendQueues(WebSocket socket, SendLimits limits, ISendReport report, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(time);

        _socket = socket;
        _time = time;
        _report = report;
        _writeTimeout = limits.WriteTimeout;
        _maxStateBytes = limits.MaxStateBytes;

        // Wait, so that a full state queue fails the TryWrite instead of blocking the caller.
        // Blocking would carry the slowest device's backlog into the hub's own command path.
        _state = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(limits.MaxStateMessages)
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

        _transient = new TransientSlots<ProtocolMessage>(limits.MaxTransientSlots, time);
    }

    /// <summary>
    /// Fires when this connection is over: displaced by a newer one for the same device, silent
    /// for too long, not taking anything, or refusing to be written to at all. Whoever owns the
    /// socket watches this and unwinds - nobody reaches into another handler's socket.
    /// </summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>Completes once the pump has stopped, whether it drained or gave up.</summary>
    public Task Drained => _drained.Task;

    /// <summary>Queues a message in the class it belongs to.</summary>
    public bool TrySend(ProtocolMessage message) => TrySend(message, SendClasses.Of(message));

    /// <summary>
    /// Queues a message in a class chosen by the caller. Beyond the tests this is for a sender
    /// that knows something the classification cannot - not for overriding it.
    /// </summary>
    public bool TrySend(ProtocolMessage message, SendClass @class)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Refused rather than queued: after disposal nothing writes this socket any more, so
        // saying yes would be a lie - and the log forwarder believes it and moves its mark on.
        if (_disposed)
        {
            return false;
        }

        // Rank 4 keeps the message rather than its bytes: it may still be merged with the next
        // one, and serialising something that is about to be replaced is work spent on a payload
        // nobody will see.
        var queued = @class switch
        {
            SendClass.Transient => _transient.Offer(message),
            SendClass.Progress => _progress.Writer.TryWrite(ProtocolJson.Serialise(message)),
            _ => OfferState(ProtocolJson.Serialise(message)),
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
    public void Finish(ProtocolMessage last)
    {
        _ = TrySend(last, SendClass.State);
        _finishing = true;
        _work.Release();
    }

    /// <summary>Ends this connection. What happens next belongs to whoever owns the socket.</summary>
    public void RequestClose()
    {
        try
        {
            if (!_closing.IsCancellationRequested)
            {
                _closing.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed while this was deciding. The connection is over either way, which is what
            // the caller was asking for.
        }
    }

    /// <summary>The single writer. Everything that goes onto this socket comes through here.</summary>
    public async Task PumpAsync(CancellationToken cancellationToken)
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
        // The flag first, and before anything is torn down: from here every sender is answered
        // rather than thrown at.
        _disposed = true;

        _closing.Dispose();

        // _work is deliberately NOT disposed. A SemaphoreSlim only holds anything worth releasing
        // once its wait handle has been asked for, and this one never is - whereas disposing it
        // would make Release throw at exactly the senders this flag exists to answer.
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

        if (_progress.Reader.TryRead(out payload!))
        {
            return true;
        }

        // Serialised here rather than where it was queued, because here is where its ages are
        // finally true: the wait in the slot is part of how old every point in it is.
        if (_transient.TryTake(out var message))
        {
            payload = ProtocolJson.Serialise(message!);
            return true;
        }

        payload = [];

        return false;
    }

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
        _report.StateQueueFull(_state.Reader.Count, Interlocked.Read(ref _stateBytes));
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
            _report.WriteTimedOut(_writeTimeout);
            RequestClose();

            return false;
        }
        catch (WebSocketException exception)
        {
            _report.SendFailed(exception);
            RequestClose();

            return false;
        }
    }
}

/// <summary>
/// Somewhere a message can be handed to be sent, without the caller knowing what carries it.
/// <para>
/// It exists for the two senders that have no business knowing about a socket - the log forwarder
/// and the progress reporter both run for the length of one connection and simply want their line
/// on the wire. Handing them the queues themselves would tie their tests to a WebSocket.
/// </para>
/// </summary>
public interface IMessageSink
{
    /// <summary>
    /// Queues a message in the class it belongs to. <see langword="false"/> means it was not
    /// taken - for a state message that is the connection ending, for a transient one it is
    /// ordinary.
    /// </summary>
    bool TrySend(ProtocolMessage message);
}

/// <summary>
/// What the three queues in front of one socket may hold, and how long one write may take.
/// </summary>
/// <param name="MaxStateMessages">
/// The state queue, by count. Generous enough for a scene with many items arriving in one go;
/// beyond that the counterpart is demonstrably taking nothing off the socket (Part 4).
/// </param>
/// <param name="MaxStateBytes">
/// And by bytes, because one <c>SceneSnapshot</c> with twenty items weighs as much as a hundred
/// small messages. Either ceiling ends the connection on its own.
/// </param>
/// <param name="MaxTransientSlots">
/// How many kinds of transient may wait at once - touch points per screen, and from M5 the
/// diagnostics and the window list. Part 4 gives each of them a capacity of one, so the tier is
/// bounded by how many kinds this build has rather than by a queue length.
/// </param>
/// <param name="WriteTimeout">
/// How long one write may take before the counterpart counts as gone. Longer than any Wi-Fi
/// dropout worth keeping a connection for, shorter than a DM's patience (Part 4).
/// </param>
public sealed record SendLimits(
    int MaxStateMessages,
    long MaxStateBytes,
    int MaxTransientSlots,
    TimeSpan WriteTimeout);

/// <summary>
/// The three things that can go wrong on a socket, said by whoever owns the connection.
/// <para>
/// It is an interface rather than a logger because the event identifiers differ by process: the
/// hub names the address at the far end, the display names the hub it was talking to, and both
/// numbers are in their own catalogue (Part 8).
/// </para>
/// </summary>
public interface ISendReport
{
    /// <summary>The state queue could take no more, so the connection is ending.</summary>
    void StateQueueFull(int queued, long bytes);

    /// <summary>One write did not finish inside the limit; the peer is treated as gone.</summary>
    void WriteTimedOut(TimeSpan limit);

    /// <summary>The socket refused a write outright.</summary>
    void SendFailed(WebSocketException exception);
}
