using System.Net.WebSockets;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<SceneStore>();
        services.AddSingleton<ScreenCatalog>();
        services.AddSingleton<DisplayConnections>();
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
    /// The display endpoint: take the <c>Hello</c>, answer with <c>Welcome</c>, hand over the
    /// scenes of this device's screens, then keep the socket alive.
    /// <para>
    /// M1a has no token and no pairing - that is M1b, and the whole state machine from Part 4
    /// goes in there. What already holds is the order: the display learns the state of ITS
    /// screens before anything else can arrive.
    /// </para>
    /// </summary>
    private static async Task HandleDisplayAsync(
        HttpContext context,
        SceneStore scenes,
        ScreenCatalog catalog,
        DisplayConnections connections,
        IOptions<HubOptions> options,
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

        var opening = await WebSocketMessages
            .ReceiveAsync(socket, MaxMessageBytes, cancellationToken)
            .ConfigureAwait(false);

        if (opening is null || ProtocolJson.Parse(opening) is not HelloMessage hello)
        {
            HubLog.DisplayWithoutHello(logger);
            return;
        }

        catalog.Report(hello.DeviceId, hello.Screens);

        // A differing protocol version rejects nothing, in either direction. The control is the
        // path along which a display gets updated, so rejecting it would cut the one wire at the
        // moment it is needed (Part 4).
        if (hello.ProtocolVersion != Protocol.Version)
        {
            HubLog.ProtocolVersionDiffers(logger, hello.DeviceId, hello.ProtocolVersion, Protocol.Version);
        }

        var screenIds = hello.Screens.Select(screen => screen.ScreenId).ToList();
        await using var connection = new DisplayConnection(hello.DeviceId, socket, screenIds, logger);

        connections.Add(connection);
        HubLog.DisplayConnected(logger, hello.DeviceId, hello.Name, screenIds.Count);

        try
        {
            connection.TrySend(new WelcomeMessage(options.Value.ControlId, Protocol.AssetPath));

            foreach (var screen in connection.Screens)
            {
                connection.TrySend(new SceneSnapshotMessage(screen, scenes.Get(screen)));
            }

            var pump = connection.PumpAsync(cancellationToken);

            // M1a expects nothing from the display beyond the Hello. Reading anyway is what
            // notices the close handshake instead of leaving a dead socket in the registry.
            while (!cancellationToken.IsCancellationRequested)
            {
                var incoming = await WebSocketMessages
                    .ReceiveAsync(socket, MaxMessageBytes, cancellationToken)
                    .ConfigureAwait(false);

                if (incoming is null)
                {
                    break;
                }

                HubLog.UnhandledMessageIgnored(logger, hello.DeviceId);
            }

            await pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a client that went away.
        }
        catch (WebSocketException exception)
        {
            HubLog.SendFailed(logger, exception, hello.DeviceId);
        }
        finally
        {
            connections.Remove(hello.DeviceId);
            HubLog.DisplayDisconnected(logger, hello.DeviceId);
        }
    }
}
