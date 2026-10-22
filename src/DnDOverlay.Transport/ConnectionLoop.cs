using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport;

/// <summary>
/// What one connection amounted to - and with it, how long to wait before the next.
/// <para>
/// Three values rather than the "did it reach a control" it used to be, because a control that
/// answers can answer three quite different things, and only one of them is worth trying again
/// straight away.
/// </para>
/// </summary>
public enum Attempt
{
    /// <summary>
    /// Nobody answered - or a control answered <b>not now</b>. A limit that was reached is a state
    /// of the hub and passes on its own, so the growing wait is exactly right for it.
    /// </summary>
    Fruitless,

    /// <summary>
    /// It worked, or the device just changed the question it is asking: a clone that was told its
    /// identity collides has made itself a fresh one, so the next attempt is a new question and
    /// not a repetition.
    /// </summary>
    Worthwhile,

    /// <summary>A control said no. Only a person takes that back.</summary>
    Refused,
}

/// <summary>Where to knock. From the beacon's sender, never from what the beacon says about itself.</summary>
public sealed record HubAddress(string Host, int Port);

/// <summary>
/// Finding a control and staying with it, for as long as the display runs.
/// <para>
/// <b>Why this is a type of its own and not a method of the application:</b> it used to live in
/// the display's <c>App.xaml.cs</c>, where no test runs, and that is where a fault got through in
/// M1b - a display that lost its connection never connected again. What was tested then was the
/// backoff and ONE connection, not what turns the two into a reconnect. Here the loop is handed
/// what it needs - a way to listen, a way to run one connection, a clock - and every decision it
/// takes can be checked without a network or a window. The rescue mark and the takeover window of
/// M5a hang on this loop; a second silent failure there would be the expensive one.
/// </para>
/// <para>
/// A configured host is tried FIRST and is not the only way in: it is a preferred address, not an
/// exclusive one, because it changes when the Surface moves between Wi-Fi and its dock. So an
/// attempt that fails hands over to discovery, and a connection that worked hands back (Part 4).
/// </para>
/// </summary>
public sealed class ConnectionLoop
{
    /// <summary>
    /// How long discovery is given when a host is configured. Without the bound a control that was
    /// merely restarting would never be tried again on the address that used to work.
    /// </summary>
    public static readonly TimeSpan PreferredHostPatience = TimeSpan.FromSeconds(5);

    private readonly ILogger<ConnectionLoop> _logger;
    private readonly TimeProvider _time;
    private readonly ReconnectBackoff _backoff;

    public ConnectionLoop(ILogger<ConnectionLoop> logger, TimeProvider time, ReconnectBackoff backoff)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(backoff);

        _logger = logger;
        _time = time;
        _backoff = backoff;
    }

    /// <summary>Runs until <paramref name="cancellationToken"/> says the application is going away.</summary>
    /// <param name="preferred">A configured host, or <see langword="null"/> to rely on discovery alone.</param>
    /// <param name="listen">
    /// Waits for a beacon of the right control and answers with it, or with <see langword="null"/>
    /// once the token it is given is cancelled. Handed in rather than built here because which
    /// control is the right one changes with a pairing, and only the caller knows when.
    /// </param>
    /// <param name="connect">Runs one connection to its end and says what it amounted to.</param>
    public async Task RunAsync(
        HubAddress? preferred,
        Func<CancellationToken, Task<Sighting?>> listen,
        Func<HubAddress, CancellationToken, Task<Attempt>> connect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listen);
        ArgumentNullException.ThrowIfNull(connect);

        var tryPreferred = preferred is not null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var target = tryPreferred
                    ? preferred
                    : Found(await FindAsync(listen, preferred is not null, cancellationToken).ConfigureAwait(false));

                // Only the preferred address survives one failure without being earned again.
                tryPreferred = false;

                if (target is null)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (preferred is not null)
                    {
                        // <b>The bound is over and nobody spoke: back to the configured host.</b>
                        // That is what the bound is for. Until 17.10.2026 this was a bare
                        // "go round again", so after one failure the host was never tried again
                        // and the display searched in five-second slices for ever - on exactly the
                        // network where a host had been set because discovery does not get through.
                        // The search itself was the wait.
                        tryPreferred = true;
                        continue;
                    }

                    // <b>The search ended without being asked to</b> - the port could not be had,
                    // or there is no network. Waited after like a failed connection; answered
                    // with "go round again", the loop spun as fast as the listener could fail.
                    await WaitAsync(_backoff.Next(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var attempt = await ConnectAsync(connect, target, cancellationToken).ConfigureAwait(false);

                if (attempt is Attempt.Worthwhile)
                {
                    // It worked once, so next time it is worth starting from the top again - both
                    // the address and the waiting.
                    _backoff.Succeeded();
                    tryPreferred = preferred is not null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // A refusal steps out of the growing wait entirely. It used to count as "reached"
                // and therefore RESET the wait to one second, so a rejected device knocked once a
                // second for as long as it ran - the tightest loop the code can produce, at the one
                // moment where nothing is going to change without a person (Part 4).
                await WaitAsync(
                    attempt is Attempt.Refused ? _backoff.Refused() : _backoff.Next(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is going away; nothing to say.
        }
    }

    /// <summary>
    /// One connection, and whatever it throws is a failed attempt rather than the end of the loop.
    /// <para>
    /// <b>The loop outlives the connection it runs.</b> A fault out of one branch of the display's
    /// message handling used to leave <see cref="RunAsync"/> altogether, and the display never
    /// looked for a control again until somebody restarted it - the M1b symptom this type exists
    /// against, reached by another door (review of M5). Only the application going away ends the
    /// loop; everything else is written down and waited after like a control that did not answer.
    /// </para>
    /// </summary>
    private async Task<Attempt> ConnectAsync(
        Func<HubAddress, CancellationToken, Task<Attempt>> connect,
        HubAddress target,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connect(target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            TransportLog.ConnectionFailed(_logger, exception, new UriBuilder(Uri.UriSchemeWs, target.Host, target.Port).Uri);

            return Attempt.Fruitless;
        }
    }

    /// <summary>Every wait says so - the hand-runs read 1042 to know the loop came round at all.</summary>
    private Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        TransportLog.RetryingIn(_logger, delay);

        return Task.Delay(delay, _time, cancellationToken);
    }

    private static HubAddress? Found(Sighting? sighting) =>
        sighting is null ? null : new HubAddress(sighting.Host, sighting.Beacon.Port);

    /// <summary>
    /// Waits for a control to announce itself - unbounded while nothing else is known, for
    /// <see cref="PreferredHostPatience"/> when a host was configured.
    /// </summary>
    private async Task<Sighting?> FindAsync(
        Func<CancellationToken, Task<Sighting?>> listen,
        bool hasPreferred,
        CancellationToken cancellationToken)
    {
        // On the clock that was handed in, so the bound can be checked without waiting for it.
        using var patience = hasPreferred ? new CancellationTokenSource(PreferredHostPatience, _time) : null;
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            patience?.Token ?? CancellationToken.None);

        return await listen(listening.Token).ConfigureAwait(false);
    }
}
