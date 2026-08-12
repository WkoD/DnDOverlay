using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>
/// Whether the other end is still there - the heartbeat and the clone probe, out of one
/// measurement.
/// <para>
/// Both ask the same question and would otherwise be answered twice in two different ways. What
/// they do with the answer differs: the heartbeat wants to know whether <b>anything</b> has been
/// heard lately, because a display that is sending is demonstrably alive; the probe wants a
/// <c>Pong</c> to <b>its</b> <c>Ping</c>, because it has to tell one connection from another
/// (Part 4).
/// </para>
/// <para>
/// It runs from the moment the socket is accepted, the pairing wait included. Without that a dead
/// connection would stand in the device list as an open request, and TCP alone would not notice
/// for hours - which is exactly the promise "what is in the list is knocking right now" is made
/// of.
/// </para>
/// </summary>
internal sealed class Liveness
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _silence;
    private readonly Func<ProtocolMessage, bool> _send;

    private long _lastHeard;
    private long _pingSentAt;
    private long _roundTripMs = -1;
    private TaskCompletionSource _pong = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Liveness(TimeProvider time, HubOptions options, Func<ProtocolMessage, bool> send)
    {
        _time = time;
        _interval = options.HeartbeatInterval;
        _silence = options.SilenceBeforeDead;
        _send = send;
        _lastHeard = time.GetTimestamp();
    }

    /// <summary>
    /// The last round trip that was actually measured, or <see langword="null"/> until a
    /// <c>Pong</c> has answered a <c>Ping</c> of ours.
    /// <para>
    /// It is read from here rather than worked out a second time, and that is the same reason the
    /// number travels back in the <c>Ping</c>: both sides are to show ONE measurement instead of
    /// each making its own (Part 4).
    /// </para>
    /// </summary>
    internal TimeSpan? RoundTrip =>
        Volatile.Read(ref _roundTripMs) is var measured && measured < 0
            ? null
            : TimeSpan.FromMilliseconds(measured);

    /// <summary>Called for every message that arrives. Anything at all counts as a sign of life.</summary>
    internal void Note() => Volatile.Write(ref _lastHeard, _time.GetTimestamp());

    /// <summary>
    /// Called when a <c>Pong</c> arrives. It is answered where it is heard rather than passed on:
    /// it says nothing about the session, it says this socket is alive.
    /// </summary>
    internal void NotePong()
    {
        var sent = Interlocked.Exchange(ref _pingSentAt, 0);

        if (sent != 0)
        {
            Volatile.Write(ref _roundTripMs, (long)_time.GetElapsedTime(sent).TotalMilliseconds);
        }

        Note();
        Interlocked.Exchange(ref _pong, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();
    }

    /// <summary>
    /// Asks whether the other end is still there and waits a moment for the answer.
    /// <para>
    /// This is what tells a clone from a crashed display coming straight back - the two are
    /// indistinguishable from outside. Silence means it was the same machine and this connection
    /// is replaced; an answer means there are two of them, and the DM decides. Decided on an
    /// ANSWER, not on a deadline (Part 4).
    /// </para>
    /// </summary>
    internal async Task<bool> ProbeAsync(TimeSpan grace, CancellationToken cancellationToken)
    {
        // Captured before the ping goes out, or an answer that comes straight back would resolve
        // a slot nobody is watching any more.
        var answered = Volatile.Read(ref _pong).Task;

        if (!Ping())
        {
            return false;
        }

        try
        {
            await answered.WaitAsync(grace, _time, cancellationToken).ConfigureAwait(false);

            return true;
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

    /// <summary>
    /// Pings on a beat and returns once the other end has been silent for too long. The caller
    /// ends the connection; this only reports.
    /// <para>
    /// A deadline on silence rather than a count of unanswered pings, and the difference is not
    /// cosmetic: a device that is busy sending is alive whether or not a <c>Pong</c> happened to
    /// cross the wire, and the number that matters is the one Part 4 names - the longest Wi-Fi
    /// dropout that should <b>not</b> count as a disconnection.
    /// </para>
    /// </summary>
    internal async Task WatchAsync(CancellationToken cancellationToken)
    {
        using var beat = new PeriodicTimer(_interval, _time);

        try
        {
            while (await beat.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_time.GetElapsedTime(Volatile.Read(ref _lastHeard)) > _silence)
                {
                    return;
                }

                _ = Ping();
            }
        }
        catch (OperationCanceledException)
        {
            // The connection ended for some other reason first. Nothing to report.
        }
    }

    /// <summary>
    /// The ping carries the last round trip the control measured, so both sides show the same
    /// number instead of each working one out its own way (Part 4).
    /// </summary>
    private bool Ping()
    {
        Interlocked.Exchange(ref _pingSentAt, _time.GetTimestamp());

        var measured = Volatile.Read(ref _roundTripMs);

        return _send(new PingMessage(measured < 0 ? null : measured));
    }
}
