using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Platform.Windows;
using DnDOverlay.Transport;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// Entry point of the display application.
/// <para>
/// M1a is the running thread: find the screens, put an overlay on each, connect to a host that
/// was configured, and draw what arrives. The tray icon, the silent start with its state
/// takeover, pairing and discovery are M1b - which is why this build still shows its overlays
/// straight away instead of waiting for a control to tell it to.
/// </para>
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private readonly Dictionary<ScreenId, OverlayWindow> _windows = [];
    private readonly Dictionary<ScreenRef, SceneState> _scenes = [];
    private readonly Dictionary<ScreenId, ScreenContext> _contexts = [];
    private readonly Dictionary<AssetId, ImageSource> _images = [];
    private readonly CancellationTokenSource _shutdown = new();

    private readonly ISecretStore _secrets = new WindowsSecretStore();

    /// <summary>
    /// Made once and kept while this device is unpaired. It belongs to the REQUEST, not to the
    /// connection attempt: the DM walks over to the table to compare it, and a number that
    /// changed on the way would be worse than none (Part 4).
    /// </summary>
    private readonly string _pairingCode = PairingCodes.Create();

    private ILogger<App> _logger = null!;
    private ILoggerFactory? _loggers;
    private ProcessLog? _log;
    private LogForwarding? _forwarding;
    private ConfigurationFile<DisplayConfiguration>? _configuration;
    private DisplayConfiguration _settings = new();
    private DataRoot _dataRoot;
    private HttpClient _http = null!;
    private AssetClient _assets = null!;
    private Uri _hubHttp = null!;
    private string _assetPath = Protocol.AssetPath;
    private DeviceId _device;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var options = DisplayOptions.Parse(e.Args);

        // Handed in, never fetched from inside a library (rule 10). From here every further
        // place - display.json, the image store, the logs - is derived from this one value, and
        // --data moves all of them at once (Part 9).
        _dataRoot = WindowsDataRoot.Resolve(options.DataRoot);

        // The clock is handed in too (rule 10) - the debounce below runs on it, which is what
        // makes "one write for twenty changes" a test rather than a stopwatch.
        _configuration = new ConfigurationFile<DisplayConfiguration>(
            _dataRoot.DisplayConfiguration,
            ConfigurationJsonContext.Default.DisplayConfiguration,
            TimeProvider.System);

        var loaded = _configuration.Load(() => new DisplayConfiguration());
        _settings = loaded.Value;

        // Written back at once when it did not exist: from here the DeviceId has to survive
        // every restart, or this machine would introduce itself as a new device each morning
        // and the DM would collect card corpses in the device list (Part 4, Part 7).
        if (loaded.Outcome is not ConfigurationOutcome.Loaded)
        {
            _configuration.Save(loaded.Value);
            _configuration.Flush();
        }

        // The command line wins for this session, the stored value fills the gap. Neither
        // overwrites the file: what --host says is a start argument, not a change of
        // configuration (Part 9).
        //
        // Null now means "nobody said", and since M1b that is a real answer rather than a gap to
        // fill: discovery finds the control by itself, so falling back to "localhost" would have
        // been a guess that quietly beats looking (Part 4).
        var host = options.Host ?? loaded.Value.Host;
        var deviceName = options.DeviceName ?? loaded.Value.DeviceName ?? Environment.MachineName;

        // One provider, registered once and never taken out - ILoggerFactory has AddProvider and
        // no counterpart, so the level a display is raised to from the far side of the house has
        // to live INSIDE it (Part 6, Part 8). The file is on from the start and not switched on
        // when it is wanted: a log that has to be turned on cannot record what happened before it
        // was turned on, and a display PC's most valuable failures are its startup failures.
        _log = new ProcessLog(
            LogIdentity.Of(typeof(App).Assembly, Protocol.Version),
            _dataRoot.Logs,
            LogFileLimits.Display,
            TimeProvider.System);

        // Held in a field, not in a using: the connection loop below outlives OnStartup, and a
        // factory disposed at the end of this method would take the log file with it.
        _loggers = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(_log)
            .AddDebug()
            .AddSimpleConsole());

        _logger = _loggers.CreateLogger<App>();

        // Warning by default: the file keeps everything this device produces, the wire carries
        // what is worth the DM's attention. Settable per device from the control in M1b's next
        // step, over ConfigUpdate (Part 6).
        _forwarding = new LogForwarding(_log, LogLevel.Warning);

        _device = new DeviceId(loaded.Value.DeviceId);

        _http = new HttpClient();
        _assets = new AssetClient(_http);

        DisplayLog.DataRootChosen(_logger, _dataRoot.Path);
        Report(_logger, loaded);

        var monitors = Screens.Enumerate(deviceName);

        if (monitors.Count == 0)
        {
            DisplayLog.NoScreens(_logger);
            Shutdown();
            return;
        }

        foreach (var monitor in monitors)
        {
            DisplayLog.ScreenFound(
                _logger,
                monitor.Screen.ScreenId,
                monitor.Screen.Label,
                monitor.Screen.Size,
                monitor.Screen.Dpi);

            _contexts[monitor.Screen.ScreenId] = ScreenContext.Default(monitor.Screen.Size, monitor.Screen.Dpi);

            var window = new OverlayWindow(monitor, options.Windowed);
            _windows[monitor.Screen.ScreenId] = window;
            window.Show();

            DisplayLog.OverlayOpened(
                _logger,
                monitor.Screen.ScreenId,
                options.Windowed ? "windowed" : "overlay");
        }

        // The token travels with the control it belongs to: one without the other would be
        // offered to the first hub that answers (Part 4). A stored token that does not decrypt is
        // simply absent - the device pairs again, which is what TryRead returning a value instead
        // of throwing is for.
        _ = RunAsync(host, options.Port, deviceName, [.. monitors.Select(monitor => monitor.Screen)], _loggers);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();

        base.OnExit(e);
    }

    public void Dispose()
    {
        if (!_shutdown.IsCancellationRequested)
        {
            _shutdown.Cancel();
        }

        _shutdown.Dispose();
        _http?.Dispose();

        // Anything outstanding goes to disk before the process does (Part 6).
        _configuration?.Flush();

        // The log needs no flushing - it writes through - so this only closes handles, and the
        // factory has to go before the provider it holds.
        _loggers?.Dispose();
        _log?.Dispose();
    }

    /// <summary>
    /// Says what reading display.json did.
    /// <para>
    /// A replacement is a warning and not a note: it costs the DeviceId, the token and the
    /// screen names, so this machine reappears in the control as a NEW, unpaired device. That is
    /// the accepted price for starting anyway - and it is healed with "reassign device", which
    /// carries the old settings over (Part 6, Part 7). Nobody finds that path without being told
    /// what happened.
    /// </para>
    /// </summary>
    private static void Report(ILogger logger, ConfigurationLoad<DisplayConfiguration> loaded)
    {
        switch (loaded.Outcome)
        {
            case ConfigurationOutcome.Created:
                DisplayLog.ConfigurationCreated(logger, loaded.Value.DeviceId);
                break;

            case ConfigurationOutcome.Replaced:
                DisplayLog.ConfigurationReplaced(logger, loaded.SetAside ?? "(not kept)");
                break;

            case ConfigurationOutcome.Loaded:
            default:
                break;
        }
    }

    /// <summary>
    /// Finding a control and staying with it, for as long as this application runs.
    /// <para>
    /// A configured host is tried FIRST and is not the only way in: it is a preferred address, not
    /// an exclusive one, because it changes when the Surface moves between Wi-Fi and its dock. So
    /// an attempt that fails hands over to discovery, and a connection that worked hands back
    /// (Part 4).
    /// </para>
    /// </summary>
    private async Task RunAsync(
        string? preferred,
        int port,
        string deviceName,
        IReadOnlyList<ScreenInfo> screens,
        ILoggerFactory loggers)
    {
        var client = new DisplayClient(loggers.CreateLogger<DisplayClient>());
        var discovery = new DiscoveryListener(loggers.CreateLogger<DiscoveryListener>());
        var backoff = new ReconnectBackoff();
        var tryPreferred = preferred is not null;

        while (!_shutdown.IsCancellationRequested)
        {
            var target = tryPreferred
                ? new Target(preferred!, port)
                : Found(await FindAsync(discovery, preferred is not null).ConfigureAwait(false));

            // Only the preferred address survives one failure without being earned again.
            tryPreferred = false;

            if (target is null)
            {
                continue;
            }

            var reached = await ConnectAsync(client, target, deviceName, screens).ConfigureAwait(false);

            if (reached)
            {
                // It worked once, so next time it is worth starting from the top again - both the
                // address and the waiting.
                backoff.Succeeded();
                tryPreferred = preferred is not null;
            }

            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            var delay = backoff.Next();

            DisplayLog.RetryingIn(_logger, delay);
            await Task.Delay(delay, _shutdown.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Where to knock. From the beacon's sender, never from what the beacon says about itself.</summary>
    private sealed record Target(string Host, int Port);

    private static Target? Found(Sighting? sighting) =>
        sighting is null ? null : new Target(sighting.Host, sighting.Beacon.Port);

    /// <summary>
    /// Waits for a control to announce itself - unbounded while nothing else is known, briefly
    /// when a host was configured. Without that bound a control that was merely restarting would
    /// never be tried again on the address that used to work.
    /// </summary>
    private async Task<Sighting?> FindAsync(DiscoveryListener discovery, bool hasPreferred)
    {
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        if (hasPreferred)
        {
            listening.CancelAfter(TimeSpan.FromSeconds(5));
        }

        return await discovery.ListenAsync(_settings.ControlId, listening.Token).ConfigureAwait(false);
    }

    /// <summary>Runs one connection to the end. True when it actually reached a control.</summary>
    private async Task<bool> ConnectAsync(
        DisplayClient client,
        Target target,
        string deviceName,
        IReadOnlyList<ScreenInfo> screens)
    {
        var inbox = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        // Every attempt builds its own Hello. After a pairing this device has a token where it had
        // a code a moment ago - a message built once at startup would go on introducing this
        // machine as a stranger for as long as it runs.
        var hello = Introduction(deviceName, screens);

        _hubHttp = new Uri($"http://{target.Host}:{target.Port}/");

        // Bounded, and written to with a wait rather than a drop: a stalled socket must slow the
        // forwarding down, not lose entries. What is then left over piles up in the ring buffer,
        // which is bounded in its turn and says how much it lost.
        var outbox = Channel.CreateBounded<ProtocolMessage>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
        });

        var hubUri = new Uri($"ws://{target.Host}:{target.Port}{Protocol.DisplayPath}");
        var pump = Task.Run(() => client.RunAsync(hubUri, hello, inbox.Writer, outbox, _shutdown.Token));

        // Runs for the length of this connection; the mark it reads from survives it, so whatever
        // came up while there was none goes out now (Part 8).
        var forwarding = Task.Run(() => _forwarding!.RunAsync(outbox.Writer, _shutdown.Token));

        var reached = false;

        await foreach (var message in inbox.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            reached |= message is WelcomeMessage or PairingPendingMessage or RejectedMessage;

            await HandleAsync(message).ConfigureAwait(false);
        }

        outbox.Writer.TryComplete();

        await pump.ConfigureAwait(false);
        await forwarding.ConfigureAwait(false);

        return reached;
    }

    /// <summary>
    /// Either the token or the pairing code, never both: what a device brings is what decides how
    /// the hub treats it (Part 4).
    /// </summary>
    private HelloMessage Introduction(string deviceName, IReadOnlyList<ScreenInfo> screens)
    {
        string? token = null;

        // The token travels with the control it belongs to: one without the other would be
        // offered to the first hub that answers. A stored token that does not decrypt is simply
        // absent - the device pairs again.
        if (_settings.ControlId is not null && DeviceTokens.TryRead(_secrets, _settings.Token, out var stored))
        {
            token = stored;
        }

        return new HelloMessage(
            _device,
            deviceName,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Protocol.Version,
            screens,
            token,
            token is null ? _pairingCode : null);
    }

    private async Task HandleAsync(ProtocolMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                _assetPath = welcome.AssetPath;
                Remember(welcome);
                break;

            case PairingPendingMessage pending:
                // In M5b this is where the setup screen goes down: name, address and code, large
                // enough to read from two metres. Until then the tray - which is also M6 - has to
                // make do with the log (Part 6).
                DisplayLog.PairingPending(_logger, pending.PairingCode);
                break;

            case RejectedMessage rejected:
                Refused(rejected.Reason);
                break;

            case SceneSnapshotMessage snapshot:
                _scenes[snapshot.Screen] = snapshot.Scene;
                await EnsureImagesAsync(snapshot.Scene).ConfigureAwait(false);
                await RenderAsync(snapshot.Screen).ConfigureAwait(false);
                break;

            case ScenePatchMessage patch:
                await ApplyAsync(patch.Patch).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Keeps a freshly issued token, together with the control it belongs to. Written straight
    /// through the debounce: it is the one value whose loss would cost the pairing (Part 6).
    /// </summary>
    private void Remember(WelcomeMessage welcome)
    {
        if (welcome.Token is null || _configuration is null)
        {
            return;
        }

        _settings = _settings with
        {
            ControlId = welcome.ControlId,
            Token = DeviceTokens.Store(_secrets, welcome.Token),
        };

        _configuration.Save(_settings);
        _configuration.Flush();

        DisplayLog.Paired(_logger, welcome.ControlId);
    }

    /// <summary>
    /// The three ways a control can turn this device away, and they are told apart on purpose.
    /// </summary>
    private void Refused(RejectionReason reason)
    {
        switch (reason)
        {
            case RejectionReason.DuplicateDevice:
                // Cloning a disk is the usual way to set up a second display PC. The control only
                // says the identity collides; the fresh one is made HERE, which keeps the rule
                // that every device creates its own (Part 3, Part 7).
                _settings = _settings with { DeviceId = Guid.NewGuid(), ControlId = null, Token = null };
                _configuration?.Save(_settings);
                _configuration?.Flush();

                DisplayLog.FreshIdentityTaken(_logger, _settings.DeviceId);
                break;

            case RejectionReason.InvalidToken:
                // The binding is NOT dropped here. The beacon is unauthenticated, so a forged
                // control answering every Hello with "unknown token" would unbind every display
                // in the house and then adopt them. It takes a tap at the device, and that tap is
                // the hurdle an attacker on the network cannot take (Part 4).
                DisplayLog.TokenUnknown(_logger);
                break;

            case RejectionReason.Denied:
            case RejectionReason.LimitExceeded:
            default:
                DisplayLog.PairingRefused(_logger, reason.ToString());
                break;
        }
    }

    private async Task ApplyAsync(ScenePatch patch)
    {
        foreach (var op in patch.Ops)
        {
            // A screen this device does not have is discarded and logged. It stays as a net
            // rather than as a division of labour: it covers the window between a hot-plug and
            // the next ScreensChanged, in which both sides briefly believe different things
            // (Part 4).
            if (!_contexts.TryGetValue(op.Screen.Screen, out var context))
            {
                DisplayLog.UnknownScreenDiscarded(_logger, op.Screen.Screen);
                continue;
            }

            var scene = _scenes.TryGetValue(op.Screen, out var known) ? known : SceneState.Empty;

            _scenes[op.Screen] = SceneReducer.Apply(scene, op.Op, context);

            await EnsureImagesAsync(_scenes[op.Screen]).ConfigureAwait(false);
            await RenderAsync(op.Screen).ConfigureAwait(false);
        }
    }

    /// <summary>Fetches and decodes whatever the scene needs and this device does not have yet.</summary>
    private async Task EnsureImagesAsync(SceneState scene)
    {
        foreach (var item in scene.Items.OfType<ImageItem>())
        {
            if (_images.ContainsKey(item.AssetId))
            {
                continue;
            }

            try
            {
                var bytes = await _assets
                    .GetAsync(_hubHttp, _assetPath, item.AssetId, _shutdown.Token)
                    .ConfigureAwait(false);

                var decoded = Decode(bytes);

                _images[item.AssetId] = decoded;
                DisplayLog.AssetDecoded(_logger, item.AssetId, decoded.PixelWidth, decoded.PixelHeight);
            }
            catch (HttpRequestException exception)
            {
                DisplayLog.AssetFailed(_logger, exception, item.AssetId);
            }
        }
    }

    /// <summary>
    /// Decoding happens here, in the application, with WIC - never in Transport. A decoded
    /// bitmap is uncompressed memory, width × height × 4 bytes, and the file size says nothing
    /// about it: a 6000×4000 photo is 96 MB in memory even when the JPEG weighs 5 MB (Part 6).
    /// The stepped decoding that follows from that is M2's business.
    /// </summary>
    private static BitmapImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private Task RenderAsync(ScreenRef screen)
    {
        if (!_windows.TryGetValue(screen.Screen, out var window)
            || !_contexts.TryGetValue(screen.Screen, out var context))
        {
            return Task.CompletedTask;
        }

        var scene = _scenes.TryGetValue(screen, out var known) ? known : SceneState.Empty;

        return window.Dispatcher.InvokeAsync(() => window.Render(scene, context, _images)).Task;
    }
}
