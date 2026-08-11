using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
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

    private ILogger<App> _logger = null!;
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

        using var loggers = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddDebug()
            .AddSimpleConsole());

        _logger = loggers.CreateLogger<App>();
        _device = new DeviceId(Guid.NewGuid());

        _http = new HttpClient();
        _assets = new AssetClient(_http);
        _hubHttp = new Uri($"http://{options.Host}:{options.Port}/");

        var monitors = ScreenEnumeration.Enumerate(options.DeviceName);

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

        var hello = new HelloMessage(
            _device,
            options.DeviceName,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Protocol.Version,
            [.. monitors.Select(monitor => monitor.Screen)]);

        _ = ConnectAsync(options, hello, loggers);
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
    }

    private async Task ConnectAsync(DisplayOptions options, HelloMessage hello, ILoggerFactory loggers)
    {
        var inbox = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        var client = new DisplayClient(loggers.CreateLogger<DisplayClient>());
        var hubUri = new Uri($"ws://{options.Host}:{options.Port}{Protocol.DisplayPath}");

        var pump = Task.Run(() => client.RunAsync(hubUri, hello, inbox.Writer, _shutdown.Token));

        await foreach (var message in inbox.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            await HandleAsync(message).ConfigureAwait(false);
        }

        await pump.ConfigureAwait(false);
    }

    private async Task HandleAsync(ProtocolMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                _assetPath = welcome.AssetPath;
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
