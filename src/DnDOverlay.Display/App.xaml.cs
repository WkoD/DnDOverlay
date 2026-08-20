using System.Collections.Concurrent;
using System.Diagnostics;
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
using DnDOverlay.Rendering.Windows;
using DnDOverlay.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

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

    /// <summary>
    /// The names the DM gave, apart from the hardware. <c>_monitors</c> holds what Windows says
    /// and its <c>Label</c> is therefore always the default one; laying the custom name over it
    /// here keeps the default available for the moment a name is taken away again (Part 6).
    /// </summary>
    private readonly Dictionary<ScreenId, string> _names = [];
    private readonly Dictionary<ScreenRef, SceneState> _scenes = [];
    private readonly Dictionary<ScreenId, ScreenContext> _contexts = [];
    private readonly Dictionary<ScreenId, MonitorInfo> _monitors = [];

    /// <summary>
    /// The decoded pictures. <b>Concurrent, and that is not belt and braces:</b> decoding runs on
    /// the connection task while the ring redraws on the UI thread beside it, so this table is
    /// written and read at the same time by construction. A plain dictionary torn mid-resize is a
    /// hang on the UI thread with nothing pointing at the cause.
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, ImageSource> _images = new();

    /// <summary>
    /// The delivered bytes of the pictures that MOVE, and only of those.
    /// <para>
    /// Measured rather than foreseen: the animation reads a GIF's frames a second time, from the
    /// source - and <c>PictureDecoder</c> has let its stream go by then, which is right for a still
    /// picture. So a moving picture needs its bytes kept, and the price is paid only where it buys
    /// something.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, byte[]> _moving = new();

    /// <summary>
    /// How often the load readings go out - 4 Hz, the upper end of Part 4's "2 to 4". A ring is
    /// meant to fill visibly rather than jump; below that it looks stuck, above it the number
    /// flickers.
    /// </summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How each screen stands right now. Everything starts <see cref="ScreenState.Inactive"/>
    /// and stays that way until a control says otherwise - the silent start (Part 3).
    /// </summary>
    private readonly Dictionary<ScreenId, ScreenCommand> _states = [];

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
    private WakeLock? _wake;
    private ProcessLog? _log;
    private LogForwarding? _forwarding;
    private ConfigurationFile<DisplayConfiguration>? _configuration;
    private DisplayConfiguration _settings = new();
    private DataRoot _dataRoot;
    private HttpClient _http = null!;
    private AssetClient _assets = null!;
    private AssetCache _cache = null!;
    private AssetLoader? _loader;
    private readonly AssetProgressTracker _progress = new();

    /// <summary>
    /// How often a running gesture is reported, per item. It lives here rather than per window
    /// because it is one budget for one wire, and a picture dragged from one screen to another in
    /// M4 must not start a second one.
    /// </summary>
    private readonly TransformThrottle _throttle = new();

    /// <summary>
    /// The frame-time source. One per process, because WPF composes every window on one render
    /// thread - see <see cref="FrameWatch"/>.
    /// </summary>
    private FrameWatch? _frames;

    /// <summary>
    /// Pictures whose next step is being decoded right now. Without it every drawing during a zoom
    /// would ask again while the first answer was still on its way.
    /// <para>
    /// Concurrent since the decode left the UI thread: the entry is made where the request is asked
    /// for and taken back where the answer arrives, and those are two different threads.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, byte> _sharpening = new();

    /// <summary>
    /// <b>One sharpening at a time in the whole process.</b> Twenty items drawn larger than they
    /// were decoded ask twenty times in one drawing, and off the UI thread that would be twenty
    /// decodes at once - each of them up to forty megabytes. It is also what Part 11 asks for in as
    /// many words: while a hand is on the table the number of parallel decodes drops to one, and a
    /// zoom is exactly that.
    /// </summary>
    private readonly SemaphoreSlim _sharpener = new(1, 1);

    /// <summary>
    /// Gestures this device has let go of and the hub has not answered yet, with the place they
    /// were let go at and the moment they were sent. It is what makes <c>3028</c> possible.
    /// <para>
    /// <b>A queue per item, not one slot</b>, and the first run of this line is why: it kept one
    /// entry per picture, a second push replaced the first, and the answer to the first was then
    /// measured against the second's place. It reported drifts of up to 918 DIP where the two ends
    /// in fact agreed - the instrument's own fault, and exactly the sort of number that would have
    /// been chased for an evening. Answers come back in the order they were sent, so the oldest
    /// entry is the one being answered.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<ItemId, ConcurrentQueue<(ItemTransform Where, long AtMs)>> _awaiting = new();

    /// <summary>Screens whose pictures are being fetched right now - one run each, no more.</summary>
    private readonly ConcurrentDictionary<ScreenId, byte> _fetching = new();

    /// <summary>Screens that were asked for again while their run was going.</summary>
    private readonly ConcurrentDictionary<ScreenId, byte> _refetch = new();

    /// <summary>Whether the ring loop is already running - there is exactly one for the process.</summary>
    private int _turning;
    private Uri _hubHttp = null!;
    private string _assetPath = Protocol.AssetPath;

    /// <summary>
    /// The token of the running session, decrypted, for the asset fetches - the stock has been
    /// behind it since M2 (Part 4). Held rather than read per call, because reading means DPAPI
    /// and that is not a thing to do per image; dropped when the pairing is reset.
    /// </summary>
    private string? _sessionToken;
    private DeviceId _device;
    private string _deviceName = string.Empty;
    private bool _windowed;

    /// <summary>
    /// The outgoing queue of the connection that stands right now, or <see langword="null"/>
    /// while there is none. A hot-plug while nothing is connected therefore reports nothing - and
    /// needs to report nothing, because the next <c>Hello</c> carries the inventory anyway.
    /// </summary>
    private ChannelWriter<ProtocolMessage>? _outbox;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Before anything can open a window, because WPF's default would end this process the
        // moment the LAST overlay closes - and a display with no window at all is not an ending
        // here, it is a documented state: every screen starts Inactive and puts nothing anywhere
        // (Part 3).
        //
        // Measured, not feared: maximising the control over the last two screens closed both
        // overlays and the display went away with them. The silent start escapes it only by
        // accident - WPF checks when a window CLOSES, and at startup none was ever open. Setting
        // all screens inactive from the control would have done the same thing.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        _deviceName = deviceName;

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

        // As early as there is somewhere to write to - and earlier here than anywhere, because a
        // display PC has nobody in front of it to notice that it went.
        LastWords.Listen(this, _logger);

        // Two knobs, not three: what the device WRITES - into the ring buffer and the file alike -
        // and what of it is worth the wire. Both are settable per device from the control, which
        // is the documented way to hunt a fault: raise this display to Debug from the far side of
        // the house (Part 6, Part 8).
        _log.Level = loaded.Value.Device.Level ?? LogLevel.Information;
        _forwarding = new LogForwarding(_log, loaded.Value.Device.ForwardAtLeast ?? LogLevel.Warning);

        // Nothing is held yet - it takes a connection as well, and there is none. The parameter is
        // set here so that a device which has never met a control still carries what it was told
        // at the table across a restart (Part 6).
        _wake = new WakeLock(Dispatcher, holding => DisplayLog.WakeLockChanged(_logger, holding))
        {
            Wanted = loaded.Value.Device.KeepAwake ?? true,
        };

        // The frame counter starts with the process, not with the first overlay: a device that
        // renders badly while nothing is on it yet is worth knowing about too, and the window is
        // thirty seconds either way (Part 10).
        _frames = new FrameWatch(_logger, PlayingScreens);

        // Said once, at the start: without it there is no way to tell whether forced software
        // rendering took - and that is how the first hand-run of 37a ended (checks/M3.md).
        var tier = RenderCapability.Tier >> 16;

        // <b>Read from the registry, not from the API</b>, and that is the correction the second
        // hand-run forced: RenderOptions.ProcessRenderMode stays "Default" while the machine renders
        // in software, because the switch that 37a asks for is DisableHWAcceleration and nothing in
        // WPF reports it back. The line said "mode Default" under forced software rendering, which
        // is exactly the question it was built to answer.
        var forced = Registry.GetValue(
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Avalon.Graphics", "DisableHWAcceleration", 0);

        var mode = forced is int and not 0
            ? "software (DisableHWAcceleration=1)"
            : RenderOptions.ProcessRenderMode.ToString();

        DisplayLog.RenderPath(_logger, tier, mode);

        _device = new DeviceId(loaded.Value.DeviceId);

        _http = new HttpClient();
        _assets = new AssetClient(_http);

        // The picture store, under the data root so --data moves it with everything else (Part 6).
        // In M2 it is bound to the session; emptying it on exit and adopting it after a crash are
        // M5a.
        _cache = new AssetCache(Path.Combine(_dataRoot.Path, "cache"));
        // The loader asks whether a hand is on the table before every picture: while one is,
        // downloads drop to one at a time. First rule of the order of precedence - the gesture
        // at the table beats new pictures (Part 1).
        _loader = new AssetLoader(
            _assets,
            _cache,
            _progress,
            busy: () => _windows.Values.Any(window => window.Holding));

        DisplayLog.DataRootChosen(_logger, _dataRoot.Path);
        Report(_logger, loaded);

        _windowed = options.Windowed;

        if (!Survey(deviceName))
        {
            DisplayLog.NoScreens(_logger);
            Shutdown();
            return;
        }

        // NOTHING is shown here, and that is the whole of the silent start: a display PC runs in
        // the autostart at every logon, not only on game nights. Coming up with the last state
        // set would make the application a trap - a frozen table nobody can explain, or an overlay
        // on the monitor the DM expressly gave back, permanently, because on an ordinary Tuesday
        // there is no control running to correct it (Part 3).
        //
        // It costs nothing: the scene lives in memory only, so after a start there is nothing to
        // show anyway. The way out is the Hello and the answer to it, and there is exactly one.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Every default written out on the first start, per screen, so whoever opens the file can
        // SEE what is settable instead of collecting the defaults from somewhere else (Part 6,
        // Part 9). Waiting for a control would leave a device that has never met one with an
        // empty list - and it is exactly that device the rule is written for.
        Remember();

        // The token travels with the control it belongs to: one without the other would be
        // offered to the first hub that answers (Part 4). A stored token that does not decrypt is
        // simply absent - the device pairs again, which is what TryRead returning a value instead
        // of throwing is for.
        // Fire and forget - but not forgotten. A faulted task nobody observes takes the whole
        // reconnect with it in silence: the device keeps its overlays, keeps showing what it shows,
        // and never looks for a control again, with not one line to say why. That is the worst
        // shape a fault can take on a machine nobody is sitting at (Part 1).
        _ = RunAsync(host, options.Port, deviceName, _loggers).ContinueWith(
            loop => DisplayLog.ConnectionLoopFailed(_logger, loop.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Reads the screens Windows currently offers, keeping what is already known about each of
    /// them. Returns <see langword="false"/> when there is nothing to play on.
    /// <para>
    /// Stored parameters are laid over the defaults here rather than when a control connects: a
    /// device that has never seen one has to be fully set up at the table and keep that across
    /// restarts, or local operation would be a sham (Part 4, Part 6).
    /// </para>
    /// </summary>
    private bool Survey(string deviceName)
    {
        var monitors = Screens.Enumerate(deviceName);

        if (monitors.Count == 0)
        {
            return false;
        }

        var stored = _settings.Screens.ToDictionary(screen => screen.ScreenId, screen => screen.Settings, StringComparer.Ordinal);

        _monitors.Clear();

        foreach (var monitor in monitors)
        {
            var id = monitor.Screen.ScreenId;

            DisplayLog.ScreenFound(_logger, id, monitor.Screen.Label, monitor.Screen.Size, monitor.Screen.Dpi);

            _monitors[id] = monitor;

            // Size and DPI are hardware facts and always win, whatever was stored about them.
            var context = ScreenContext.Default(monitor.Screen.Size, monitor.Screen.Dpi);

            _contexts[id] = stored.TryGetValue(id.Value, out var settings)
                ? settings.ApplyTo(context)
                : context;

            // A name given at the table has to survive a restart without a control, or naming
            // locally would be a sham - the same argument as for every other parameter (Part 6).
            if (settings?.CustomName is { Length: > 0 } custom)
            {
                _names[id] = custom;
            }

            _states.TryAdd(id, new ScreenCommand(ScreenState.Inactive));
        }

        return true;
    }

    /// <summary>
    /// A monitor was plugged, unplugged or re-resolved. The device reports which screens it HAS
    /// and knows no "unavailable" - working out what is missing is the control's business
    /// (Part 3).
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents fires on a thread of its own, and everything below touches windows.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnDisplaySettingsChanged(sender, e));
            return;
        }

        if (_shutdown.IsCancellationRequested || !Survey(_deviceName))
        {
            return;
        }

        // A screen that is gone takes its window with it; the rest keep theirs.
        foreach (var id in _windows.Keys.Where(id => !_monitors.ContainsKey(id)).ToList())
        {
            Close(id);
        }

        foreach (var id in _monitors.Keys)
        {
            Show(id);
        }

        var screens = Inventory();

        DisplayLog.ScreensReported(_logger, screens.Count);
        _outbox?.TryWrite(new ScreensChangedMessage(screens));
    }

    private IReadOnlyList<ScreenInfo> Inventory() =>
        [.. _monitors.Values.Select(Named)];

    /// <summary>
    /// One screen as it is CALLED: the custom name where there is one, the default from the
    /// hardware otherwise. <see cref="ScreenInfo.Label"/> always carries the effective name, so
    /// both sides show the same text without either of them deciding the rule for itself
    /// (Part 3, Part 6).
    /// </summary>
    /// <summary>
    /// Shows every overlay its own name, so the DM can tell which tile is which physical screen
    /// (Part 6).
    /// <para>
    /// Only screens that HAVE an overlay - an inactive one has no window to show it on, and
    /// putting one there to answer the question would break the promise that the DM gave that
    /// screen back to Windows (Part 3).
    /// </para>
    /// </summary>
    private void Identify()
    {
        foreach (var (screen, window) in _windows)
        {
            window.Identify(_monitors.TryGetValue(screen, out var monitor)
                ? Named(monitor).Label
                : screen.Value);
        }

        DisplayLog.ScreensIdentified(_logger, _windows.Count);
    }

    private ScreenInfo Named(MonitorInfo monitor) =>
        _names.TryGetValue(monitor.Screen.ScreenId, out var custom)
            ? monitor.Screen with { Label = custom, CustomName = custom }
            : monitor.Screen;

    /// <summary>
    /// Takes a <c>ConfigUpdate</c> from the control: the settings it changed, and how each screen
    /// is to stand.
    /// <para>
    /// A screen with a finding behaves exactly like <see cref="ScreenState.Inactive"/> - no
    /// window - and the wish stays untouched underneath. That is the whole reason findings are not
    /// states: when the finding falls away the window is simply back, with the state that stood
    /// there all along (Part 3).
    /// </para>
    /// </summary>
    private void Apply(ConfigUpdate update)
    {
        foreach (var screen in update.Screens)
        {
            if (screen.Settings is { IsEmpty: false } settings && _contexts.TryGetValue(screen.Screen, out var context))
            {
                _contexts[screen.Screen] = settings.ApplyTo(context);

                // Null means unchanged on the wire, so an EMPTY name is how one is taken away
                // again - and the default underneath is still there to fall back to.
                if (settings.CustomName is { Length: > 0 } name)
                {
                    _names[screen.Screen] = name;
                }
                else if (settings.CustomName is not null)
                {
                    _names.Remove(screen.Screen);
                }
            }

            if (screen.Command is { } command)
            {
                // Any arriving ConfigUpdate clears a local suppression as well: whoever has their
                // control back wants to play on, not switch every screen back by hand (Part 6).
                _states[screen.Screen] = command;
            }

            Show(screen.Screen);
        }

        if (update.Device is { } device)
        {
            _log!.Level = device.Level ?? _log.Level;
            _forwarding!.AtLeast = device.ForwardAtLeast ?? _forwarding.AtLeast;
            _wake!.Wanted = device.KeepAwake ?? _wake.Wanted;
        }

        Remember();
        DisplayLog.SettingsApplied(_logger, update.Screens.Count);
    }

    /// <summary>
    /// Writes what arrived into display.json, in FULL rather than as the delta it came as: on the
    /// wire a null means "unchanged", in the file it would mean nothing (Part 6).
    /// <para>
    /// The screen states are expressly not written. A state set from outside applies to the
    /// running session; the lasting wish lives in the control, which is what makes the silent
    /// start hold - an autostarting display PC must not come up with last night's arrangement
    /// (Part 3).
    /// </para>
    /// </summary>
    private void Remember()
    {
        if (_configuration is null)
        {
            return;
        }

        _settings = _settings with
        {
            Screens =
            [
                .. _contexts.Select(pair => new ScreenPreferences(
                    pair.Key.Value,
                    ScreenSettings.Of(pair.Value, _names.GetValueOrDefault(pair.Key)))),
            ],
            Device = new DeviceSettings(_log?.Level, _forwarding?.AtLeast, _wake?.Wanted),
        };

        _configuration.Save(_settings);
    }

    /// <summary>
    /// Puts a window down, takes one away, or leaves things as they are. It is the only place
    /// that decides whether a screen carries an overlay, so "suppressed behaves like inactive" is
    /// a property of the code rather than a rule somebody has to remember.
    /// </summary>
    private void Show(ScreenId screen)
    {
        // Written as one refusal rather than as a "wanted" flag, because everything below needs the
        // COMMAND and not only the answer: the state decides whether the screen takes gestures, and
        // a flag would have thrown exactly that away.
        if (!_states.TryGetValue(screen, out var command)
            || command.State == ScreenState.Inactive
            || command.Suppress is not null
            || !_monitors.TryGetValue(screen, out var monitor))
        {
            Close(screen);
            return;
        }

        if (_windows.TryGetValue(screen, out var standing))
        {
            // A state change that keeps the window. Enabled and Disabled differ only in whether
            // gestures do anything, so the window stays and is simply told - which is what makes
            // switching back take effect at once, with no rebuild and no flash (Part 11, step 37e).
            standing.State = command.State;

            // And it follows its monitor: a resolution change leaves the window on the old bounds
            // otherwise, which puts pictures outside the screen and stretches the rest (37c3).
            standing.Moved(monitor);

            return;
        }

        var window = new OverlayWindow(monitor, _windowed) { State = command.State };

        // Before Show, because the layout that gives the stage its size happens inside it. The
        // scene is normalised against the surface it is drawn on, so a surface that changes size
        // has to be drawn on again - otherwise the first drawing stands on a guess until the next
        // arrival happens to correct it.
        window.SurfaceChanged += () => Draw(new ScreenRef(_device, screen));

        window.Surveyed += (widthDip, heightDip, monitor) =>
        {
            var wide = (int)Math.Round(widthDip);
            var high = (int)Math.Round(heightDip);

            DisplayLog.SurfaceMeasured(_logger, screen, wide, high, monitor.Screen.Size, monitor.Screen.Dpi);
        };

        // What a hand at the table does, on its way to the hub. The window keeps the gesture, this
        // decides how often it is reported and puts it on the wire (Part 4).
        window.Transformed += reported => Report(screen, reported);
        window.Parked += (item, parked) =>
        {
            // A gesture that ended in the bar sends no binding transform, so its throttle entry
            // would otherwise stay behind for good.
            _throttle.Forget(item);
            _outbox?.TryWrite(new ItemParkedMessage(screen, item, parked));
        };

        // Zoomed past its step: the next one is decoded from the bytes already in the store, so a
        // sharper picture costs a decode and never a second download (Part 6).
        window.Sharpen += (asset, needed) => Sharpen(screen, asset, needed);

        // More pictures were ready than one pass hangs up. Background priority on purpose: input
        // goes first, which is the whole reason they are staggered (Part 1, order of precedence).
        // <b>Input priority, and the difference is measured</b> (hand-run of M3b, second run): at
        // Background priority the follow-up pass only runs when the machine has nothing else to do,
        // so 722 arriving pictures appeared as "first one, then nothing, then all at once" - the
        // passes were starved for the whole 24-second load. Input priority puts a hand at the table
        // first and a picture right after it, which is the order of precedence rather than an
        // absence of one.
        window.HandWaited += late => _frames?.HandWaited(late);

        window.MoreToShow += () => Dispatcher.InvokeAsync(
            () => Draw(new ScreenRef(_device, screen)),
            System.Windows.Threading.DispatcherPriority.Input);

        _windows[screen] = window;
        window.Show();

        DisplayLog.OverlayOpened(_logger, screen, _windowed ? "windowed" : "overlay");

        // Whatever was already known about this screen is drawn at once - a screen switched from
        // inactive to active has to stand there complete, not empty until the DM next touches it.
        // That is what makes preparing on an inactive screen work at all (Part 3).
        Draw(new ScreenRef(_device, screen));
    }

    private void Close(ScreenId screen)
    {
        if (!_windows.Remove(screen, out var window))
        {
            return;
        }

        window.Close();
        DisplayLog.OverlayClosed(_logger, screen);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();

        base.OnExit(e);
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (!_shutdown.IsCancellationRequested)
        {
            _shutdown.Cancel();
        }

        _shutdown.Dispose();
        _http?.Dispose();

        // Before the dispatcher stops - the request is held on the UI thread, and a thread that is
        // gone cannot be asked to let go.
        _wake?.Dispose();

        // The composition tick would otherwise keep a handler on a process that is going away.
        _frames?.Dispose();

        _sharpener.Dispose();

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

            var attempt = await ConnectAsync(client, target, deviceName).ConfigureAwait(false);

            if (attempt is Attempt.Worthwhile)
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

            // A refusal steps out of the growing wait entirely. It used to count as "reached" and
            // therefore RESET the wait to one second, so a rejected device knocked once a second
            // for as long as it ran - the tightest loop the code can produce, at the one moment
            // where nothing is going to change without a person (Part 4).
            var delay = attempt is Attempt.Refused ? backoff.Refused() : backoff.Next();

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

    /// <summary>
    /// What one connection amounted to - and with it, how long to wait before the next.
    /// <para>
    /// Three values rather than the "did it reach a control" it used to be, because a control that
    /// answers can answer three quite different things, and only one of them is worth trying again
    /// straight away.
    /// </para>
    /// </summary>
    private enum Attempt
    {
        /// <summary>
        /// Nobody answered - or a control answered <b>not now</b>. A limit that was reached is a
        /// state of the hub and passes on its own, so the growing wait is exactly right for it.
        /// </summary>
        Fruitless,

        /// <summary>
        /// It worked, or the device just changed the question it is asking: a clone that was told
        /// its identity collides has made itself a fresh one, so the next attempt is a new
        /// question and not a repetition.
        /// </summary>
        Worthwhile,

        /// <summary>A control said no. Only a person takes that back.</summary>
        Refused,
    }

    /// <summary>Runs one connection to the end, and says what it amounted to.</summary>
    private async Task<Attempt> ConnectAsync(DisplayClient client, Target target, string deviceName)
    {
        var inbox = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        // Every attempt builds its own Hello. After a pairing this device has a token where it had
        // a code a moment ago - a message built once at startup would go on introducing this
        // machine as a stranger for as long as it runs.
        var hello = Introduction(deviceName);

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

        // What a hot-plug and a locally changed setting reach the wire through, for as long as
        // this connection stands.
        _outbox = outbox.Writer;

        // Runs for the length of THIS CONNECTION, and the token has to say so - not the one that
        // means "the application is going away". The forwarder parks in a wait that only a new log
        // entry wakes; with the shutdown token it would still be parked there long after the socket
        // was gone, and the await below would hold this method for ever. That is not a leak, it is
        // the reconnect: RunAsync never comes round again, and the device sits with its overlays
        // and never looks for a control any more.
        using var connected = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        // The mark it reads from survives the connection, so whatever came up while there was none
        // goes out with the next one (Part 8).
        var forwarding = Task.Run(() => _forwarding!.RunAsync(outbox.Writer, connected.Token));

        // Same lifetime as the forwarder, and for the same reason: it belongs to THIS connection.
        var reporting = Task.Run(() => ReportProgressAsync(outbox.Writer, connected.Token));

        var attempt = Attempt.Fruitless;

        await foreach (var message in inbox.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            attempt = message switch
            {
                WelcomeMessage or PairingPendingMessage => Attempt.Worthwhile,
                RejectedMessage refusal => refusal.Reason switch
                {
                    RejectionReason.DuplicateDevice => Attempt.Worthwhile,
                    RejectionReason.LimitExceeded => Attempt.Fruitless,
                    _ => Attempt.Refused,
                },
                _ => attempt,
            };

            // Something arriving is the proof the socket stands, and it is the only one to be had
            // here: the task above returns long before ClientWebSocket has finished connecting.
            // Repeating it costs nothing - the lock only acts when the answer changes.
            _wake!.Connected = true;

            await HandleAsync(message).ConfigureAwait(false);
        }

        // Let go on the way out, not on the way back in: from here the screens may go dark, which
        // is the right answer for a table nobody is playing on any more.
        _wake!.Connected = false;

        _outbox = null;
        outbox.Writer.TryComplete();

        // Completing the channel does not reach the forwarder - it is waiting on the ring buffer,
        // not on the channel - so the connection has to say out loud that it is over.
        await connected.CancelAsync().ConfigureAwait(false);

        await pump.ConfigureAwait(false);
        await forwarding.ConfigureAwait(false);

        return attempt;
    }

    /// <summary>
    /// Reports a locally changed setting back to the control. The same mechanism in the other
    /// direction, not a second one - and it is what keeps local operation from being a sham: a
    /// device changed at the table must not have its value quietly overwritten by the next
    /// <c>ConfigUpdate</c> (Part 4, Part 6).
    /// <para>
    /// Nothing in M1b triggers it yet, because the tray window that would is M6. What exists is
    /// the path - and the baseline the <c>Hello</c> carries, which is the half that works without
    /// any surface at all.
    /// </para>
    /// </summary>
    internal void Report(ConfigUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        Remember();
        _outbox?.TryWrite(new ConfigUpdateMessage(update));
    }

    /// <summary>
    /// The token decides how the hub treats this device - but the pairing code travels ALWAYS,
    /// and that is a change worth its sentence.
    /// <para>
    /// A token this control does not know is a pairing request now rather than a rejection
    /// (Part 4), and a request the DM cannot check against the table would be the worst of both:
    /// he would be allowing a device by its name, which is exactly what an impostor would supply.
    /// The code is no secret - it stands large on the display - so carrying it costs nothing and
    /// keeps the comparison possible in the one case that needs it most.
    /// </para>
    /// </summary>
    private HelloMessage Introduction(string deviceName)
    {
        string? token = null;

        // The token travels with the control it belongs to: one without the other would be
        // offered to the first hub that answers. A stored token that does not decrypt is simply
        // absent - the device pairs again.
        if (_settings.ControlId is not null && DeviceTokens.TryRead(_secrets, _settings.Token, out var stored))
        {
            token = stored;
        }

        // Kept for the asset fetches. The Welcome may hand out a fresh one right after this, and
        // the handler below replaces it - the picture path and the socket must never disagree
        // about which token is current.
        _sessionToken = token;

        return new HelloMessage(
            _device,
            deviceName,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Protocol.Version,
            Inventory(),
            token,
            _pairingCode,

            // The baseline of the two-sided configuration: the FULL effective set, so the control
            // can take it over and only then send what it changed while this device was away. The
            // screen states are expressly not in here - a display never reports how it stands,
            // only how it is set (Part 3, Part 4).
            new ConfigUpdate(
                [
                    .. _contexts.Select(pair => new ScreenConfigUpdate(
                        pair.Key,
                        ScreenSettings.Of(pair.Value, _names.GetValueOrDefault(pair.Key)))),
                ],
                new DeviceSettings(_log?.Level, _forwarding?.AtLeast, _wake?.Wanted)),

            // What this device still has on its screens. A control that has just restarted takes
            // it over, and only where it has no scene of its own - which is the one exception to
            // "the hub is authoritative", and it is kept this narrow on purpose (Part 4).
            [.. _scenes.Where(pair => pair.Key.Device == _device)
                .Select(pair => new ScreenScene(pair.Key.Screen, pair.Value))]);
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
                // The same order as a patch, and for the same reason: the arrangement stands and is
                // drawn at once, the pictures come after. A snapshot is the reconnect case, so it
                // is exactly the moment somebody has a hand on the table and is waiting to be
                // answered again.
                _scenes[snapshot.Screen] = snapshot.Scene;
                await RenderAsync(snapshot.Screen).ConfigureAwait(false);
                Fetch(snapshot.Screen);
                break;

            case ScenePatchMessage patch:
                await ApplyAsync(patch.Patch).ConfigureAwait(false);
                break;

            case ConfigUpdateMessage update:
                // Onto the UI thread: this opens and closes windows, and everything here arrives
                // on the connection task.
                await Dispatcher.InvokeAsync(() => Apply(update.Update));
                break;

            case IdentifyScreensMessage:
                await Dispatcher.InvokeAsync(Identify);
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

        // The asset fetches use it too, and they start as soon as the first scene arrives - which
        // is right after this. Setting it only on the next start-up would leave a freshly paired
        // device fetching with the token it no longer has (Part 4).
        _sessionToken = welcome.Token;

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

    /// <summary>
    /// Applies one patch, screen by screen rather than operation by operation.
    /// <para>
    /// The grouping is not tidiness. Two questions are asked of a WHOLE patch and cannot be asked of
    /// a single operation: what has just <see cref="Arrival">arrived</see> - which depends on whether
    /// the patch does nothing but add - and how often a screen is redrawn, which for a load of twenty
    /// items is the difference between one drawing and twenty.
    /// </para>
    /// </summary>
    private async Task ApplyAsync(ScenePatch patch)
    {
        foreach (var group in patch.Ops.GroupBy(op => op.Screen))
        {
            // A screen this device does not have is discarded and logged. It stays as a net
            // rather than as a division of labour: it covers the window between a hot-plug and
            // the next ScreensChanged, in which both sides briefly believe different things
            // (Part 4).
            if (!_contexts.TryGetValue(group.Key.Screen, out var context))
            {
                DisplayLog.UnknownScreenDiscarded(_logger, group.Key.Screen);
                continue;
            }

            var ops = group.Select(op => op.Op).ToList();
            var scene = _scenes.TryGetValue(group.Key, out var known) ? known : SceneState.Empty;

            // Asked BEFORE the operations are applied, because "was this screen already occupied?"
            // has no answer afterwards.
            var arrived = Arrival.Marked(scene, ops);

            foreach (var op in ops)
            {
                scene = SceneReducer.Apply(scene, op, context);

                if (op is TransformItem moved)
                {
                    Confirmed(moved, context, scene);
                }

                if (op is RemoveItem removed)
                {
                    // An item that goes away while a hand was on it leaves an entry in the throttle
                    // that nothing would ever clear again - its binding report is the one that never
                    // comes (Part 4, conflict rule 4).
                    _throttle.Forget(removed.Item);
                }
            }

            _scenes[group.Key] = scene;

            // <b>The state lands and is drawn; the pictures follow.</b> This used to wait here for
            // the whole load - fetch and decode, about three seconds a picture on the table's own
            // machine - and nothing else was read from the socket in the meantime, not even the
            // hub's answer to a gesture somebody had just finished. Measured (M3b, sixth Pro 4 run,
            // 3028): a released gesture came back after 0 ms once the table was quiet and after
            // 2203 to 8141 ms while pictures were still arriving, and every one of those late
            // answers carried a depth the hub had worked out seconds after the grab - so a picture
            // somebody had long since moved was lifted over pictures that had not existed when it
            // was taken hold of. Three separate complaints from the table came back to this one
            // await.
            //
            // A picture whose place already stands must not be disturbed again because OTHER
            // pictures are still coming: what a player sees, a player takes to be finished.
            await RenderAsync(group.Key).ConfigureAwait(false);

            Fetch(group.Key);

            foreach (var item in arrived)
            {
                await Dispatcher
                    .InvokeAsync(
                        () => Flash(group.Key.Screen, item),
                        System.Windows.Threading.DispatcherPriority.Input)
                    .Task
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Lights a newly arrived picture up. On the UI thread, and after the drawing - the place has to
    /// exist before anything can be laid over it.
    /// </summary>
    private void Flash(ScreenId screen, ItemId item)
    {
        if (_windows.TryGetValue(screen, out var window))
        {
            window.Flash(item);
        }
    }

    /// <summary>
    /// Asks for whatever the current scene of this screen is still missing, off the receive loop.
    /// <para>
    /// <b>One run at a time per screen, and a second ask while one is going does not queue a second
    /// run</b> - it marks the running one to look again when it is done. Queueing would give twenty
    /// arriving pictures twenty runs of one picture each, which is exactly the shape that kept the
    /// loader's three-at-a-time from ever engaging (<c>Peak=1</c> in every run of every hand-run so
    /// far). Looking again instead means the next round sees everything that is still missing and
    /// fetches it together.
    /// </para>
    /// </summary>
    private void Fetch(ScreenRef screen)
    {
        if (!_fetching.TryAdd(screen.Screen, 0))
        {
            _refetch[screen.Screen] = 0;

            return;
        }

        _ = Task.Run(() => FetchAsync(screen), _shutdown.Token);
    }

    private async Task FetchAsync(ScreenRef screen)
    {
        try
        {
            do
            {
                _ = _refetch.TryRemove(screen.Screen, out _);

                var scene = _scenes.TryGetValue(screen, out var known) ? known : SceneState.Empty;

                // Here rather than on the receive loop: it walks the store on disk, and on the
                // table's own machine that is not free.
                LetGoOfWhatNoSceneNeeds();

                await EnsureImagesAsync(scene, screen.Screen).ConfigureAwait(false);
                await RenderAsync(screen).ConfigureAwait(false);
            }
            while (_refetch.ContainsKey(screen.Screen));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _ = _fetching.TryRemove(screen.Screen, out _);

            // Asked between the last look and letting go of the mark, which is a window of a few
            // instructions and therefore certain to happen eventually. Without this the ask would
            // be dropped and a picture would never arrive.
            if (_refetch.ContainsKey(screen.Screen))
            {
                Fetch(screen);
            }
        }
    }

    /// <summary>
    /// Fetches and decodes whatever the scene needs and this device does not have yet.
    /// <para>
    /// Everything up to the bytes belongs to <see cref="AssetLoader"/>: order, the three at a time,
    /// the store, the hash check and the progress readings, all of it under test. What is left here
    /// is the part that genuinely needs a window - decoding, and putting the result where the
    /// drawing finds it.
    /// </para>
    /// </summary>
    private async Task EnsureImagesAsync(SceneState scene, ScreenId screen)
    {
        // The base step, in physical pixels of THIS screen: a picture is decoded to the screen's
        // longer edge and no further, because the pixels above that are memory nobody can see
        // (Part 6). Zooming past the step asks for the next one, later and once (Sharpen).
        var edge = _contexts.TryGetValue(screen, out var context)
            ? Math.Max(context.Size.Width, context.Size.Height)
            : 0;

        var wanted = new List<AssetWanted>();

        if (scene.Background is { } background && !_images.ContainsKey(background.AssetId))
        {
            wanted.Add(new AssetWanted(background.AssetId, background.Meta, IsBackground: true));
        }

        wanted.AddRange(scene.Items
            .OfType<ImageItem>()
            .Where(item => !_images.ContainsKey(item.AssetId))
            .Select(item => new AssetWanted(item.AssetId, item.Meta, IsParked: item.Parked)));

        if (wanted.Count == 0)
        {
            return;
        }

        var arrivals = Channel.CreateUnbounded<AssetArrived>();

        var loading = _loader!
            .LoadAsync(_hubHttp, _assetPath, wanted, _sessionToken!, arrivals.Writer, _shutdown.Token);

        _ = loading.ContinueWith(_ => arrivals.Writer.TryComplete(), TaskScheduler.Default);

        // Redrawn on every arrival AND while the readings change, because the ring lives on the
        // ungoverned layer: it has to keep turning while the rest of the scene sits still.
        //
        // <b>Started, not waited for.</b> Measured at the table (hand-run of M3b, step 0.5): the run
        // used to end with the loop, and the loop ends a quarter of a second after the last picture
        // is done, because that is its beat. With one patch per picture that quarter second sat
        // between EVERY two pictures - 796 runs whose fetching added up to six seconds took six
        // minutes. The rings are decoration on a layer nobody waits for, and a load run that waits
        // for them pays their beat for every picture it brings.
        _ = Task.Run(() => TurnRingsAsync(_shutdown.Token), _shutdown.Token);

        var steps = scene.Items
            .OfType<ImageItem>()
            .Select(item => (item.AssetId, item.Meta))
            .Append(scene.Background is { } layer ? (layer.AssetId, layer.Meta) : default)
            .Where(pair => pair.Meta is not null)
            .GroupBy(pair => pair.AssetId)
            .ToDictionary(
                group => group.Key,
                group => DecodeSteps.Base(edge, group.First().Meta.PixelWidth, group.First().Meta.PixelHeight));

        await foreach (var arrived in arrivals.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            Decode(arrived, steps.GetValueOrDefault(arrived.Asset));
        }

        var run = await loading.ConfigureAwait(false);

        // Said only where something actually came down. A scene whose pictures are all in the store
        // already is the ordinary case - every reconnect, every second look at the same table - and
        // a "0 assets in 0 ms" for each of them would bury the runs that carry a measurement.
        if (run.Fetched > 0)
        {
            DisplayLog.AssetsLoaded(
                _logger, run.Fetched, run.Milliseconds, run.Bytes, run.Peak, run.AlreadyHere);
        }

        // A picture that never arrived is said here, with its name. The loader has no logger and
        // should not get one: only this end knows that a hash is "Dilwyn Kemri", and a name is the
        // whole point of the line (Part 8).
        foreach (var failure in run.Failed)
        {
            var name = NameOf(failure.Asset);

            DisplayLog.AssetFailed(_logger, name, failure.Asset, failure.Detail);
        }
    }

    /// <summary>
    /// What the DM calls this picture. The scenes of this device carry it, and the hash is what is
    /// left when nothing does - a picture can be dropped from a scene while its bytes are still on
    /// their way.
    /// <para>
    /// Asked at the moment of writing rather than kept in a table beside the pictures: a second
    /// table over the same keys is the shape that cost 2 GB in M2b, and this one would be read a
    /// handful of times an evening.
    /// </para>
    /// </summary>
    private string NameOf(AssetId asset)
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Items)
            {
                if (item is ImageItem picture && picture.AssetId == asset)
                {
                    return picture.Name;
                }
            }

            if (scene.Background is { } background && background.AssetId == asset)
            {
                return background.Name ?? asset.Value[..8];
            }
        }

        return asset.Value[..8];
    }

    /// <summary>
    /// One arrival becomes a bitmap. A thumbnail is taken only while nothing better is there, so a
    /// picture STANDS at its place blurred within a second and is replaced when the original lands
    /// (Part 5) - never the other way round.
    /// </summary>
    private void Decode(AssetArrived arrived, int pixelWidth)
    {
        if (arrived.IsThumbnail && _images.ContainsKey(arrived.Asset))
        {
            return;
        }

        // Timed, because the table's silence between "the ring is full" and "the picture is there"
        // has two possible owners and the log named neither: the decode itself, and a pause of the
        // collector. This is the first of the two (hand-run of M3b, 37c1).
        var clock = Stopwatch.GetTimestamp();

        try
        {
            // A thumbnail is already the small step and is never stepped down further.
            var decoded = PictureDecoder.Decode(arrived.Bytes, arrived.IsThumbnail ? 0 : pixelWidth);

            _images[arrived.Asset] = decoded;

            // Kept only for what moves, and only from the full picture - a thumbnail of an
            // animation is a still, and animating it would show the wrong thing sharply.
            if (!arrived.IsThumbnail && Animated(arrived.Asset))
            {
                _moving[arrived.Asset] = arrived.Bytes;
            }

            if (!arrived.IsThumbnail)
            {
                // Only the full picture ends the load. Reporting done on the thumbnail would fill
                // the ring while the picture on the table was still the blurred one.
                _progress.Done(arrived.Asset);

                // Looked up before the call rather than inside it: an argument that costs something
                // must not be evaluated when the level would throw the line away (CA1873).
                var name = NameOf(arrived.Asset);
                var spent = (long)Stopwatch.GetElapsedTime(clock).TotalMilliseconds;

                DisplayLog.AssetDecoded(
                    _logger, name, arrived.Asset, decoded.PixelWidth, decoded.PixelHeight, spent);
            }
        }
        // A picture that arrives unreadable stays missing and the rest of the scene is drawn.
        // NotSupportedException is what WIC answers with - measured, not assumed - and without this
        // an undecodable asset left HandleAsync, ended the message loop and took the connection with
        // it. Silently: exactly the failure this project already paid for once (Part 6). What should
        // SHOW in its place - a placeholder with a reason - is still M2b.
        catch (NotSupportedException exception)
        {
            _progress.Failed(arrived.Asset);

            var name = NameOf(arrived.Asset);

            DisplayLog.AssetFailed(_logger, name, arrived.Asset, exception.Message);
        }
    }

    /// <summary>
    /// Lets go of every picture no scene of this device stands on any more - the decoded bitmap,
    /// the bytes kept for an animation, and the file in the store.
    /// <para>
    /// <b>Without this the display grows without end.</b> Part 6 says it in as many words: the sum
    /// only works if bitmaps disappear again, and a table keyed by identifier is "the obvious
    /// convenience" that prevents exactly that. Measured at the table (hand-run of M2b, step 44a):
    /// <b>2 GB</b> and still climbing, because nothing removed an entry - not the bitmaps, not the
    /// animation bytes, and the disk store was never trimmed at all.
    /// </para>
    /// <para>
    /// Counted over ALL screens, the inactive ones included: their arrangement is kept, so what
    /// lies there is still needed (Part 3).
    /// </para>
    /// </summary>
    private void LetGoOfWhatNoSceneNeeds()
    {
        var wanted = SceneAssets.InUse(_scenes.Values);

        foreach (var asset in _images.Keys.Where(asset => !wanted.Contains(asset)).ToList())
        {
            _images.TryRemove(asset, out _);
            _moving.TryRemove(asset, out _);
        }

        // The file may stay - the store keeps what it has room for, because a picture that comes
        // back costs nothing then (Part 5). What it must never do is keep growing, and this is the
        // only place that knows which files a current item needs.
        _cache.Trim(wanted);
    }

    /// <summary>
    /// Keeps the rings moving for as long as something is loading. It redraws on its own beat
    /// rather than on arrivals: between the first byte and the last there is no arrival at all,
    /// and a ring that only moved when a picture landed would jump instead of filling (Part 7).
    /// </summary>
    private async Task TurnRingsAsync(CancellationToken cancellationToken)
    {
        // <b>One loop for the process, not one per load.</b> Measured at the table (hand-run of
        // M3b, step 0.5): every arriving picture is its own patch and therefore its own load run,
        // and each run started a loop of its own that redrew every screen four times a second. With
        // several hundred pictures that is several hundred full redraws per second, each one
        // walking every item on the screen - the redraws were the load they were reporting on.
        if (Interlocked.Exchange(ref _turning, 1) == 1)
        {
            return;
        }

        try
        {
            while (_progress.Reading() is { } reading
                && reading.Loads.Any(load => load.State is not AssetLoadState.Done))
            {
                await Task.Delay(ProgressInterval, cancellationToken).ConfigureAwait(false);

                // Every screen that carries a window: the readings are per DEVICE, and a picture
                // being fetched for one screen is drawn on whichever screens show it.
                foreach (var screen in _windows.Keys.ToList())
                {
                    await RenderAsync(new ScreenRef(_device, screen)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            Volatile.Write(ref _turning, 0);
        }
    }

    /// <summary>
    /// How far each picture that is still coming has got - the source the progress ring is drawn
    /// from. Taken from the tracker rather than kept a second time, so what turns at the table and
    /// what the control is told are the same number (Part 7).
    /// </summary>
    /// <summary>
    /// What has a ring on it right now.
    /// <para>
    /// <b>Only what is actually on its way</b> - not everything the run will fetch. The loader
    /// announces every wanted picture before it fetches any, on purpose, because the control's list
    /// answers "what is this table still waiting for". A ring answers something else: "something is
    /// happening HERE". Found at the table (M3b), the first evening the arrangement stopped waiting
    /// for its pictures: twenty rings stood in twenty empty places, three of them moving.
    /// </para>
    /// </summary>
    private Dictionary<AssetId, double> Loading() =>
        _progress.Reading() is { } reading
            ? reading.Loads
                .Where(load => load.State is not (AssetLoadState.Done or AssetLoadState.Waiting))
                .ToDictionary(load => load.Asset, load => load.Fraction)
            : new Dictionary<AssetId, double>();

    /// <summary>
    /// Whether any scene this device holds says this picture moves. Asked of the SCENE rather than
    /// of the bytes: the codec has already worked it out, and asking again would be a second answer
    /// to a settled question (Part 5).
    /// </summary>
    private bool Animated(AssetId asset) =>
        _scenes.Values.Any(scene =>
            (scene.Background is { } background
                && background.AssetId == asset
                && background.Meta.IsAnimated)
            || scene.Items.OfType<ImageItem>().Any(item => item.AssetId == asset && item.Meta.IsAnimated));

    /// <summary>
    /// Sends what is being loaded, two to four times a second and <b>only while something is</b>
    /// (Part 4). It runs for as long as the connection does: the readings travel in the progress
    /// queue, so a slow socket overwrites them rather than queueing them up.
    /// </summary>
    private async Task ReportProgressAsync(ChannelWriter<ProtocolMessage> outbox, CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(ProgressInterval);

        while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_progress.Reading() is { } reading)
            {
                outbox.TryWrite(reading);

                // Settled only after it has gone out, so a finished or failed picture is reported
                // once and then drops out of the readings.
                _progress.Settle();
            }
        }
    }

    /// <summary>
    /// Puts one report of a running gesture on the wire, or lets it go.
    /// <para>
    /// Throttled per item at about 20 Hz, and the binding one at the end always goes (Part 4). The
    /// decision itself is in <see cref="TransformThrottle"/>, in Core, where it can be asserted -
    /// here is only the clock: <c>Environment.TickCount64</c> is monotonic, and a gesture is exactly
    /// the place where a wall clock stepping back an hour would freeze the reporting for an hour.
    /// </para>
    /// <para>
    /// A report while nothing is connected is dropped without a word. The picture is where the
    /// player put it, the display keeps its own scene, and the <c>Hello</c> of the next connection
    /// carries it - there is nothing to queue up here (Part 4).
    /// </para>
    /// </summary>
    private void Report(ScreenId screen, OverlayWindow.Reported reported)
    {
        // <b>The binding report is written into this device's own scene at once</b>, and that is
        // conflict rule 2 rather than an optimisation: "the display applies its gesture locally at
        // once and corrects as soon as a broadcast arrives that it did not cause itself". Until now
        // it did the opposite at the one moment that shows - on letting go the hold was dropped and
        // the drawing fell back to the position the hub last knew, so the picture SPRANG BACK and
        // only arrived where the hand had put it when the answer came round. Measured at the table
        // (M3b, fifth Pro 4 run): during a load that answer took the whole load, and every movement
        // of a minute replayed at its end.
        //
        // The values are the same ones the hub will compute - the reducer and the clamping are in
        // Core and both ends run them - so the patch that comes back confirms this rather than
        // moving anything. Written before the connection is even looked at: a display holding its
        // own scene while nothing is connected is the ordinary case, and the Hello carries it.
        if (reported.Binding)
        {
            var queue = _awaiting.GetOrAdd(reported.Transform.Item, _ => new ConcurrentQueue<(ItemTransform, long)>());

            // Bounded, because an operation the hub never answers - the item was gone, or locked -
            // would otherwise leave an entry that every later answer is measured against.
            while (queue.Count >= 16)
            {
                _ = queue.TryDequeue(out _);
            }

            queue.Enqueue((reported.Transform, Environment.TickCount64));
        }

        if (reported.Grabbed || reported.Binding)
        {
            Settle(screen, reported.Transform, toFront: reported.Grabbed);

            // Drawn at once, on the thread this already runs on: the scene now says something the
            // window does not show yet - the new depth on a grab, the settled place on letting go -
            // and waiting for the next drawing would put both back into the hub's round trip, which
            // is exactly what this is here to end. It happens twice per gesture, not per step.
            Draw(new ScreenRef(_device, screen));
        }

        if (_outbox is not { } outbox)
        {
            return;
        }

        // A grab is never held back: it is what brings the picture to the front, and a front that
        // arrives a twentieth of a second late is a picture that was under another one while
        // somebody was already moving it.
        if (!_throttle.Allows(
            reported.Transform.Item,
            Environment.TickCount64,
            binding: reported.Binding || reported.Grabbed))
        {
            return;
        }

        outbox.TryWrite(new ItemTransformedMessage(
            screen,
            reported.Transform,
            reported.KnownRevision,
            reported.Grabbed));
    }

    /// <summary>
    /// Says what the hub made of a gesture this device let go of - once, for the answer that was
    /// actually waited for. An operation for an item nobody here moved is a stranger's and passes
    /// without a word.
    /// </summary>
    private void Confirmed(TransformItem moved, ScreenContext context, SceneState scene)
    {
        if (!_awaiting.TryGetValue(moved.Item, out var queue) || !queue.TryDequeue(out var mine))
        {
            return;
        }

        // In DIP rather than in the normalised fractions, because a thousandth of a screen means
        // nothing to a reader and two pixels do. The two axes are separate fractions of two
        // different edges, so each is converted with its own (Part 3).
        var dx = (moved.CenterX - mine.Where.CenterX) * context.Size.Width;
        var dy = (moved.CenterY - mine.Where.CenterY) * context.Size.Height;

        var drift = Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), 1);
        var spent = Environment.TickCount64 - mine.AtMs;

        var name = scene.Items.OfType<ImageItem>().FirstOrDefault(item => item.ItemId == moved.Item)?.Name
            ?? moved.Item.Value.ToString()[..8];

        DisplayLog.GestureConfirmed(_logger, name, spent, drift);
    }

    /// <summary>
    /// Lays a gesture into this device's own scene, through the reducer both ends share.
    /// <para>
    /// The revision stays as it was: numbering is the hub's alone (conflict rule 1), and a display
    /// that invented one would win a comparison it has no business winning. What this changes is
    /// only WHERE the picture is drawn - and how high it lies - until the hub says so too.
    /// </para>
    /// <para>
    /// <b>The grab raises it here too</b>, by the hub's own arithmetic. Part 3 promises that what is
    /// taken hold of comes to the front "locally at once and bindingly from the hub right
    /// afterwards", and the second half was the only half built: the picture rose when the answer
    /// came back. On a quiet link that is ten milliseconds and nobody sees it. Under load the
    /// runner saw it plainly - every pushed picture was "touched again" at the end of the load, one
    /// after another, as the queued answers finally arrived with the depth each grab had earned.
    /// </para>
    /// </summary>
    /// <param name="toFront">
    /// Whether this is the grab. The formula is the hub's, deliberately duplicated rather than
    /// approximated: a display that guessed a different depth would be corrected visibly.
    /// </param>
    private void Settle(ScreenId screen, ItemTransform transform, bool toFront)
    {
        var reference = new ScreenRef(_device, screen);

        if (!_scenes.TryGetValue(reference, out var scene)
            || !_contexts.TryGetValue(screen, out var context)
            || scene.Items.FirstOrDefault(item => item.ItemId == transform.Item) is not { } standing)
        {
            return;
        }

        _scenes[reference] = SceneReducer.Apply(
            scene,
            new TransformItem(
                transform.Item,
                transform.CenterX,
                transform.CenterY,
                transform.Scale,
                transform.RotationDeg,
                toFront ? Math.Max(standing.ZOrder, scene.TopZOrder + 1) : standing.ZOrder,
                standing.Revision),
            context);
    }

    /// <summary>
    /// Decodes one picture a step larger, because it is being drawn larger than it was decoded.
    /// <para>
    /// The bytes come out of the store, so this costs a decode and never a second transfer. It is
    /// one step per crossing and capped at the source; zooming out asks for nothing, which is why
    /// there is no way back down in here (<see cref="DecodeSteps"/>).
    /// </para>
    /// <para>
    /// Animated pictures are left alone: their frames are built from the bytes by
    /// <c>AnimatedPicture</c>, and a still decoded beside them would be a second picture for the
    /// same item.
    /// </para>
    /// </summary>
    private void Sharpen(ScreenId screen, AssetId asset, int needed)
    {
        // Asked from the drawing, so this runs on the UI THREAD - and nothing here may cost
        // anything. Everything that does is below, on a thread nobody is waiting on.
        if (_moving.ContainsKey(asset)
            || !_images.TryGetValue(asset, out var current)
            || current is not BitmapSource bitmap
            || DecodeSteps.Next(bitmap.PixelWidth, needed, SourceWidth(asset)) is not { } step)
        {
            return;
        }

        // The mark goes on AFTER the cheap questions, not before them. It used to be the second
        // condition of the guard above, and an asset that fell out on a later one kept the mark for
        // the rest of the session - that picture then never sharpened again.
        if (!_sharpening.TryAdd(asset, 0))
        {
            return;
        }

        _ = Task.Run(() => SharpenAsync(screen, asset, bitmap.PixelWidth, step), _shutdown.Token);
    }

    /// <summary>
    /// The expensive half of sharpening, off the UI thread.
    /// <para>
    /// <b>It used to be on it</b>, and that is a hand's worth of standing still: reading the whole
    /// file back out of the store is 18 to 34 megabytes off the disk, and the decode above it builds
    /// up to forty megabytes of bitmap - both on the thread that answers the finger, in the middle
    /// of the zoom that asked for them. Found by reading the drawing path after the third Pro 4 run
    /// (M3b); it had not fired in that run, but check steps 18 and 18a ask for exactly the zooming
    /// that sets it off.
    /// </para>
    /// <para>
    /// The decoded bitmap is frozen, so it crosses back without a copy - the same property that
    /// lets the loader decode on its own thread.
    /// </para>
    /// </summary>
    private async Task SharpenAsync(ScreenId screen, AssetId asset, int before, int step)
    {
        try
        {
            await _sharpener.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = _sharpening.TryRemove(asset, out _);
            return;
        }

        try
        {
            // The ORIGINAL out of the store, never the thumbnail: sharpening a thumbnail would
            // produce a bigger blurred picture, which is worse than the small one it replaced.
            if (!_cache.TryGet(asset, out var bytes))
            {
                return;
            }

            var decoded = PictureDecoder.Decode(bytes, step);

            // Asked again after the decode: a picture can be taken off the table while its sharper
            // version is on its way, and putting it back into the table of bitmaps would be memory
            // no scene asks for - the very growth LetGoOfWhatNoSceneNeeds exists to prevent.
            if (!_images.ContainsKey(asset))
            {
                return;
            }

            _images[asset] = decoded;

            var name = NameOf(asset);

            DisplayLog.PictureSharpened(_logger, name, before, step);

            await Dispatcher.InvokeAsync(() => Draw(new ScreenRef(_device, screen))).Task
                .ConfigureAwait(false);
        }
        catch (NotSupportedException error)
        {
            // The bytes decoded once already, so this is not a picture we cannot read - it is a
            // failure of THIS decode, and the picture on the screen stays what it is.
            DisplayLog.AssetFailed(_logger, NameOf(asset), asset, error.Message);
        }
        catch (OperationCanceledException)
        {
            // Shutting down while a step was being decoded.
        }
        finally
        {
            _ = _sharpening.TryRemove(asset, out _);
            _ = _sharpener.Release();
        }
    }

    /// <summary>
    /// How many pixels the source has, from the scenes that carry the picture. Zero when nothing
    /// does any more - a picture dropped while its sharpening was on its way.
    /// </summary>
    private int SourceWidth(AssetId asset)
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Items.OfType<ImageItem>())
            {
                if (item.AssetId == asset)
                {
                    return item.Meta.PixelWidth;
                }
            }

            if (scene.Background is { } background && background.AssetId == asset)
            {
                return background.Meta.PixelWidth;
            }
        }

        return 0;
    }

    /// <summary>
    /// The screens being played right now, by the name the DM would recognise. Asked at the moment
    /// of writing rather than held: a warning that named a screen which had been unplugged two
    /// minutes earlier would send somebody looking at the wrong table.
    /// </summary>
    private IReadOnlyCollection<string> PlayingScreens() =>
    [
        .. _windows.Keys.Select(screen =>
            _names.TryGetValue(screen, out var named) ? named
                : _monitors.TryGetValue(screen, out var monitor) ? monitor.Screen.Label
                : screen.Value),
    ];

    /// <summary>
    /// Draws a screen, <b>behind the hand rather than in front of it</b>.
    /// <para>
    /// It was posted at <c>Normal</c>, which outranks <c>Input</c> - the priority the finger's own
    /// events arrive at. Every drawing therefore went ahead of every touch that was already waiting,
    /// and during a load there are many: one for each arriving picture and four a second per screen
    /// for the rings. Measured at the table (M3b, fourth Pro 4 run): the dispatcher was <b>200 to
    /// 313 ms</b> late while a single drawing cost at most 40 ms, so what the hand waited for was a
    /// QUEUE of drawings and not one slow one. The runner saw it from the other side: a pushed
    /// picture stood still and every movement of the whole load replayed in a few milliseconds at
    /// the end.
    /// </para>
    /// <para>
    /// At <c>Input</c> a drawing shares the queue with the finger instead of jumping it. Not lower:
    /// <c>Background</c> means "when the machine has nothing else to do", and during a manipulation
    /// it never has - that is the same mistake the staggered pass made in the second Pro 4 run,
    /// where pictures arrived in a block at the end (Part 11, the priority rule).
    /// </para>
    /// </summary>
    private Task RenderAsync(ScreenRef screen) =>
        Dispatcher.InvokeAsync(() => Draw(screen), System.Windows.Threading.DispatcherPriority.Input).Task;

    /// <summary>
    /// Draws one screen. Must run on the UI thread - windows are created and drawn there, and
    /// everything that reaches this comes over the connection task.
    /// <para>
    /// A screen without a window is not an error and not a special case: the scene is kept
    /// regardless, so an inactive screen switched active stands there complete at once. Giving up
    /// the scene when the window goes would make the arrangement fall out of the <c>Hello</c> and
    /// a control restart would lose it (Part 3).
    /// </para>
    /// </summary>
    private void Draw(ScreenRef screen)
    {
        if (!_windows.TryGetValue(screen.Screen, out var window)
            || !_contexts.TryGetValue(screen.Screen, out var context))
        {
            return;
        }

        // Timed, because the frame times cannot say whose second it was. Measured at the table
        // (M3b, 37c1): the picture stops between "the transfer is done" and "the picture is there",
        // and everything the runner could switch off from outside - transfer, decode, disk, the
        // rings, the arrival highlight - left it unchanged. What is left needs a WINDOW on the
        // receiving screen, and this is the work that happens there.
        var clock = Stopwatch.GetTimestamp();

        window.Render(
            _scenes.TryGetValue(screen, out var known) ? known : SceneState.Empty,
            context,
            _images,
            _moving,
            Loading());

        _frames?.Drew(Stopwatch.GetElapsedTime(clock).TotalMilliseconds);
    }
}
