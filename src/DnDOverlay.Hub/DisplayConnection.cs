using System.Net.WebSockets;
using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// One connected display, and the single loop that writes to its socket.
/// <para>
/// M1a has ONE queue. Part 4 calls for three - state, progress, transient - with their own
/// capacities and their own behaviour when full, and that is M1b. What is already true here is
/// the shape it needs: exactly one writer per socket, fed through a channel, so adding the
/// other two is adding queues rather than rewriting the send path.
/// </para>
/// </summary>
public sealed class DisplayConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private readonly Channel<ProtocolMessage> _outgoing =
        Channel.CreateBounded<ProtocolMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private TaskCompletionSource<bool>? _pong;

    public DisplayConnection(DeviceId device, WebSocket socket, IReadOnlyList<ScreenId> screens, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(screens);

        Device = device;
        _socket = socket;
        _logger = logger;
        Screens = screens.Select(screen => new ScreenRef(device, screen)).ToList();
    }

    public DeviceId Device { get; }

    /// <summary>The screens this connection is responsible for - the hub addresses per connection.</summary>
    public IReadOnlyList<ScreenRef> Screens { get; }

    /// <summary>Fires when this connection is being displaced by a newer one for the same device.</summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>
    /// Asks this connection whether it is still there, and waits a moment for the answer.
    /// <para>
    /// This is the probe that tells a clone from a crashed display coming straight back - the two
    /// are indistinguishable from the outside. Silence means it was the same machine and this
    /// connection gets replaced; an answer means there are two of them, and the DM decides
    /// (Part 4).
    /// </para>
    /// </summary>
    public async Task<bool> ProbeAsync(TimeSpan grace, TimeProvider time, CancellationToken cancellationToken)
    {
        var pong = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pong, pong);

        if (!TrySend(new PingMessage()))
        {
            return false;
        }

        try
        {
            return await pong.Task.WaitAsync(grace, time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Called by the read loop when a <c>Pong</c> comes in.</summary>
    public void NotePong() => Interlocked.Exchange(ref _pong, null)?.TrySetResult(true);

    /// <summary>
    /// Ends this connection because a newer one took over the device. The endpoint that owns it
    /// notices through <see cref="Closing"/> and cleans up - nobody reaches into another
    /// endpoint's socket.
    /// </summary>
    public void RequestClose()
    {
        if (!_closing.IsCancellationRequested)
        {
            _closing.Cancel();
        }
    }

    /// <summary>
    /// Queues a message. Returns <see langword="false"/> when the queue is closed, which happens
    /// once the connection is on its way out.
    /// </summary>
    public bool TrySend(ProtocolMessage message) => _outgoing.Writer.TryWrite(message);

    /// <summary>Writes queued messages until the connection ends.</summary>
    public async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _socket.SendAsync(
                    ProtocolJson.Serialise(message),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down or the connection went away - both are ordinary.
        }
        catch (WebSocketException exception)
        {
            HubLog.SendFailed(_logger, exception, Device);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _outgoing.Writer.TryComplete();
        _closing.Dispose();

        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    statusDescription: null,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Closing a socket the other end already dropped is not worth a line.
            }
        }
    }
}
