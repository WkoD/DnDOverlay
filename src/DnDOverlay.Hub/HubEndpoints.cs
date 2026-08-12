using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub;

/// <summary>
/// Wiring the hub into a host. It is a LIBRARY, not a process: the control hosts it, and there
/// is no separate service to install (Part 9).
/// </summary>
public static class HubEndpoints
{
    /// <summary>A ceiling for one incoming message. The full limits table follows in M1b.</summary>
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>Registers everything the hub owns.</summary>
    public static IServiceCollection AddDnDOverlayHub(
        this IServiceCollection services,
        Action<HubOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Handed in, never fetched (rule 10) - the clone probe waits on it, which is what makes
        // "one second of silence" a test rather than a stopwatch. TryAdd, so an application that
        // brings its own clock keeps it.
        services.TryAddSingleton(TimeProvider.System);

        // A hosted service, so it announces the control before the surface stands (rule 5).
        services.AddHostedService<DiscoveryBeacon>();

        services.AddSingleton<SceneStore>();
        services.AddSingleton<ScreenCatalog>();
        services.AddSingleton<DisplayConnections>();
        services.AddSingleton<PairingDirectory>();
        services.AddSingleton<SessionApi>();
        services.AddSingleton<ISessionApi>(provider => provider.GetRequiredService<SessionApi>());

        return services;
    }

    /// <summary>Maps the three endpoints M1a needs.</summary>
    public static IEndpointRouteBuilder MapDnDOverlayHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Reachability probe, deliberately without a token - and it gives away nothing but
        // "running". Versions, device lists and names belong behind the token, or a diagnostic
        // endpoint becomes a convenient reconnaissance source for anyone on the network
        // (Part 4).
        endpoints.MapGet(Protocol.HealthPath, () => Results.Text("running"));

        endpoints.MapGet($"{Protocol.AssetPath}/{{id}}", ServeAssetAsync);
        endpoints.Map(Protocol.DisplayPath, HandleDisplayAsync);

        return endpoints;
    }

    /// <summary>
    /// Serves the bytes of one asset. The identifier is validated BEFORE it touches anything
    /// resembling a path: without that check <c>GET /assets/..%5C..%5Cwindows%5C…</c> is the
    /// classic way to read arbitrary files off the DM's machine - with a valid token, so from
    /// any paired device (Part 4, Part 5).
    /// </summary>
    private static async Task ServeAssetAsync(HttpContext context, string id, IAssetSource assets)
    {
        var assetId = new AssetId(id);

        if (!assetId.IsWellFormed)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!assets.TryOpen(assetId, out var data, out var contentType))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (data.ConfigureAwait(false))
        {
            context.Response.ContentType = contentType;
            await data.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The display endpoint: take the <c>Hello</c>, decide whether this device may be here, and
    /// only then hand over the scenes of its screens.
    /// <para>
    /// Everything is read through ONE loop into a channel. That is not tidiness: while a pairing
    /// request is waiting, somebody has to notice that the device went away - and two concurrent
    /// receives on one socket are not allowed, while cancelling a pending receive aborts the
    /// socket. One reader, one channel, and the waiting phase simply watches for the channel to
    /// complete.
    /// </para>
    /// </summary>
    private static async Task HandleDisplayAsync(
        HttpContext context,
        SceneStore scenes,
        ScreenCatalog catalog,
        DisplayConnections connections,
        PairingDirectory pairing,
        IOptions<HubOptions> options,
        TimeProvider time,
        ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(HubEndpoints).FullName!);

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var cancellationToken = context.RequestAborted;
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var inbox = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // The read loop runs on the connection's OWN lifetime, not on the request's. Without
        // that, deciding to end this connection would not end it: the loop would still be sitting
        // in ReceiveAsync, the handler would wait for it, and a refused device would keep a socket
        // that nobody intends to serve. It is the same trap on both paths - a refusal and a
        // connection displaced by a newer one.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reading = ReadIntoAsync(socket, inbox.Writer, logger, lifetime.Token);

        try
        {
            if (await NextAsync(inbox.Reader, lifetime.Token).ConfigureAwait(false) is not HelloMessage hello)
            {
                HubLog.DisplayWithoutHello(logger);
                return;
            }

            catalog.Report(hello.DeviceId, hello.Screens);

            // A differing protocol version rejects nothing, in either direction. The control is
            // the path along which a display gets updated, so rejecting it would cut the one wire
            // at the moment it is needed (Part 4).
            if (hello.ProtocolVersion != Protocol.Version)
            {
                HubLog.ProtocolVersionDiffers(logger, hello.DeviceId, hello.ProtocolVersion, Protocol.Version);
            }

            var admitted = await AdmitAsync(
                hello,
                address,
                socket,
                inbox.Reader,
                connections,
                pairing,
                options.Value,
                time,
                logger,
                lifetime.Token).ConfigureAwait(false);

            if (admitted is null)
            {
                return;
            }

            await ServeAsync(
                hello,
                admitted,
                socket,
                inbox.Reader,
                scenes,
                connections,
                options.Value,
                logger,
                lifetime).ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await reading.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The state machine of Part 4, at the one place it touches a socket. Returns the device that
    /// may stay, or <see langword="null"/> when this connection has been told why it may not.
    /// </summary>
    private static async Task<PairedDevice?> AdmitAsync(
        HelloMessage hello,
        string address,
        WebSocket socket,
        ChannelReader<ProtocolMessage> inbox,
        DisplayConnections connections,
        PairingDirectory pairing,
        HubOptions options,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (connections.Count >= options.MaxConnections)
        {
            HubLog.LimitReached(logger, hello.Name, address, "too many connections");
            await Refuse(socket, RejectionReason.LimitExceeded, cancellationToken).ConfigureAwait(false);
            return null;
        }

        switch (pairing.Consider(hello, address))
        {
            case Admission.Admitted admission when admission.Device.Role != PairingRole.Display:
                // A control token at the display endpoint, or the other way round. The role sits
                // in our own entry, so this is read rather than believed (Part 4).
                HubLog.TokenRefused(logger, hello.DeviceId, hello.Name);
                await Refuse(socket, RejectionReason.InvalidToken, cancellationToken).ConfigureAwait(false);
                return null;

            case Admission.Admitted admission:
                return await SettleIdentityAsync(
                    hello, address, admission.Device, socket, inbox, connections, pairing,
                    options, time, logger, cancellationToken).ConfigureAwait(false);

            case Admission.Refused refused:
                Report(logger, hello, address, refused.Reason, pairing.AcceptNewDevices);
                await Refuse(socket, refused.Reason, cancellationToken).ConfigureAwait(false);
                return null;

            case Admission.Waiting waiting:
                if (waiting.IsNew)
                {
                    HubLog.PairingRequested(
                        logger,
                        hello.DeviceId,
                        hello.Name,
                        address,
                        waiting.Request.Snapshot.PairingCode);
                }

                return await WaitForDecisionAsync(
                    waiting.Request, socket, inbox, pairing, logger, cancellationToken).ConfigureAwait(false);

            default:
                return null;
        }
    }

    /// <summary>
    /// A valid token whose device is already connected. Two things look identical from here - a
    /// crashed display coming straight back, and a second machine cloned from the first disk - so
    /// the connection that is already there is asked, and it is its ANSWER that decides
    /// (Part 4).
    /// </summary>
    private static async Task<PairedDevice?> SettleIdentityAsync(
        HelloMessage hello,
        string address,
        PairedDevice device,
        WebSocket socket,
        ChannelReader<ProtocolMessage> inbox,
        DisplayConnections connections,
        PairingDirectory pairing,
        HubOptions options,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!connections.TryGet(hello.DeviceId, out var existing))
        {
            return device;
        }

        if (!await existing.ProbeAsync(options.CloneProbe, time, cancellationToken).ConfigureAwait(false))
        {
            existing.RequestClose();
            HubLog.ConnectionReplaced(logger, hello.DeviceId);

            return device;
        }

        HubLog.CloneDetected(logger, hello.DeviceId, hello.Name);

        // Not turned away: cloning a disk is the usual way to set up a second display PC, and a
        // dead end there could only be left by hand-editing display.json on a machine without a
        // keyboard (Part 4, Part 7).
        return await WaitForDecisionAsync(
            pairing.NoteClone(hello, address), socket, inbox, pairing, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the DM, and for nothing else. There is no timer here: the request stands as long
    /// as the connection stands and vanishes with it, which is what keeps the device list showing
    /// only what is knocking right now (Part 4).
    /// </summary>
    private static async Task<PairedDevice?> WaitForDecisionAsync(
        PendingRequest request,
        WebSocket socket,
        ChannelReader<ProtocolMessage> inbox,
        PairingDirectory pairing,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await WebSocketMessages
            .SendAsync(socket, new PairingPendingMessage(request.Snapshot.PairingCode), cancellationToken)
            .ConfigureAwait(false);

        // Whichever comes first: the DM decides, or the device goes away and the read loop closes
        // the channel.
        var gone = inbox.Completion;
        var settled = await Task.WhenAny(request.Decision, gone).ConfigureAwait(false);

        if (settled != request.Decision)
        {
            pairing.Withdraw(request);
            HubLog.PairingWithdrawn(logger, request.Snapshot.Device);

            return null;
        }

        switch (await request.Decision.ConfigureAwait(false))
        {
            case PairingDecision.Approved approved:
                HubLog.PairingApproved(
                    logger,
                    approved.Device.Device,
                    approved.Device.Name,
                    approved.Device.Role);

                return approved.Device;

            case PairingDecision.Refused refused:
                if (refused.Reason == RejectionReason.DuplicateDevice)
                {
                    HubLog.FreshIdentityRequested(logger, request.Snapshot.Device, request.Snapshot.Name);
                }
                else
                {
                    HubLog.PairingDenied(logger, request.Snapshot.Device, request.Snapshot.Name);
                }

                await Refuse(socket, refused.Reason, cancellationToken).ConfigureAwait(false);

                return null;

            default:
                return null;
        }
    }

    /// <summary>The part M1a already had: hand over the scenes and keep the socket alive.</summary>
    private static async Task ServeAsync(
        HelloMessage hello,
        PairedDevice device,
        WebSocket socket,
        ChannelReader<ProtocolMessage> inbox,
        SceneStore scenes,
        DisplayConnections connections,
        HubOptions options,
        ILogger logger,
        CancellationTokenSource lifetime)
    {
        var screenIds = hello.Screens.Select(screen => screen.ScreenId).ToList();
        await using var connection = new DisplayConnection(device.Device, socket, screenIds, logger);

        // Being displaced by a newer connection ends THIS one, read loop included. Anything less
        // and the old handler would sit on a socket the hub has already given up on.
        using var displaced = connection.Closing.Register(() => lifetime.Cancel());

        connections.Add(connection);
        HubLog.DisplayConnected(logger, device.Device, device.Name, screenIds.Count);

        try
        {
            // A freshly issued token travels exactly once, in the answer to the pairing the DM
            // just allowed. By the time it goes out the control has already written it to disk -
            // ApprovePairingAsync is called after the file, not before (Part 7).
            connection.TrySend(new WelcomeMessage(
                options.ControlId,
                Protocol.AssetPath,
                hello.Token is null ? device.Token : null));

            foreach (var screen in connection.Screens)
            {
                connection.TrySend(new SceneSnapshotMessage(screen, scenes.Get(screen)));
            }

            var pump = connection.PumpAsync(lifetime.Token);

            await foreach (var message in inbox.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                switch (message)
                {
                    case PongMessage:
                        connection.NotePong();
                        break;

                    default:
                        HubLog.UnhandledMessageIgnored(logger, device.Device);
                        break;
                }
            }

            await pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, a client that went away, or a newer connection for the same device.
        }
        catch (WebSocketException exception)
        {
            HubLog.SendFailed(logger, exception, device.Device);
        }
        finally
        {
            // By instance, never by device: on a fast reconnect the new connection has already
            // registered under the same DeviceId when this one finishes tidying up.
            connections.Remove(connection);
            HubLog.DisplayDisconnected(logger, device.Device);
        }
    }

    private static void Report(
        ILogger logger,
        HelloMessage hello,
        string address,
        RejectionReason reason,
        bool acceptingNewDevices)
    {
        switch (reason)
        {
            case RejectionReason.InvalidToken:
                HubLog.TokenRefused(logger, hello.DeviceId, hello.Name);
                break;

            case RejectionReason.LimitExceeded:
                HubLog.LimitReached(logger, hello.Name, address, "too many pairing attempts");
                break;

            case RejectionReason.Denied when !acceptingNewDevices:
                HubLog.NewDevicesBlocked(logger, hello.Name, address);
                break;

            default:
                HubLog.PairingDenied(logger, hello.DeviceId, hello.Name);
                break;
        }
    }

    /// <summary>
    /// Says why, and then closes properly rather than dropping the socket. The device is meant to
    /// act on the reason - take a fresh identity, ask at the device, wait five minutes - and an
    /// aborted connection would leave it guessing which of those it was (Part 4).
    /// </summary>
    private static async Task Refuse(WebSocket socket, RejectionReason reason, CancellationToken cancellationToken)
    {
        await WebSocketMessages.SendAsync(socket, new RejectedMessage(reason), cancellationToken).ConfigureAwait(false);
        await WebSocketMessages.CloseAsync(socket, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProtocolMessage?> NextAsync(
        ChannelReader<ProtocolMessage> inbox,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inbox.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The one reader of this socket. It completes the channel on the way out, so everybody
    /// waiting on this connection - the pairing above included - learns that the device is gone.
    /// </summary>
    private static async Task ReadIntoAsync(
        WebSocket socket,
        ChannelWriter<ProtocolMessage> inbox,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await WebSocketMessages
                    .ReceiveAsync(socket, MaxMessageBytes, cancellationToken)
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
                catch (JsonException)
                {
                    // An unknown type is ignored and logged, never fatal - that is what lets an
                    // older display face a newer control at all (rule 7).
                    HubLog.MessageIgnored(logger);
                    continue;
                }

                if (message is not null)
                {
                    await inbox.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (WebSocketException)
        {
            // The other end went away mid-message; the channel completing says the rest.
        }
        catch (InvalidOperationException)
        {
            // Over the size ceiling. The connection ends, which is the point of the ceiling.
        }
        finally
        {
            inbox.TryComplete();
        }
    }
}
