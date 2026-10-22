using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    /// The touch log of every open screen, kept beside <see cref="_windows"/> because it is read
    /// from somewhere else: the reporter runs on a background thread at 10 Hz, and the window
    /// table belongs to the UI thread (Part 4).
    /// </summary>
    private readonly ConcurrentDictionary<ScreenId, TouchLog> _touching = new();

    /// <summary>
    /// The names the DM gave, apart from the hardware. <c>_monitors</c> holds what Windows says
    /// and its <c>Label</c> is therefore always the default one; laying the custom name over it
    /// here keeps the default available for the moment a name is taken away again (Part 6).
    /// </summary>
    private readonly Dictionary<ScreenId, string> _names = [];
    private readonly SceneBook _scenes = new();
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
    /// Pictures whose load ended for good, with the reason the placeholder shows (Part 7). Taken
    /// out again when the picture does arrive after all - a later scene or a retry can bring it.
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, string> _failed = new();

    /// <summary>
    /// Pictures memory pressure has put down to their thumbnails. Marked, so the drawing does not
    /// sharpen them straight back up - it asks for a sharper decode whenever a picture is drawn
    /// larger than its bitmap, and a stepped-down one always is. A hand taking it, or the DM
    /// fetching it out of the fan, brings it back (Part 6).
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, byte> _steppedDown = new();

    /// <summary>When a picture was last touched or arrived, as a running count - its age for the memory rule.</summary>
    private readonly ConcurrentDictionary<AssetId, long> _touchedAt = new();

    /// <summary>
    /// Pictures to fetch again although they are decoded - the store was emptied under them by
    /// "clear the picture store" (Part 7). Taken by the next run that asks for them.
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, byte> _refill = new();

    private long _touches;

    /// <summary>One relief at a time; a second asked meanwhile has nothing to add.</summary>
    private readonly Lock _relieving = new();

    /// <summary>
    /// What the last step-downs should save but the process does not show yet: a bitmap's memory
    /// returns only when the collector has run. Counted against the measurement until the next full
    /// sweep, or every decode in between would step down more pictures than the shortfall needs.
    /// </summary>
    private long _pendingRelief;

    private int _reliefSweeps = -1;

    /// <summary>The memory cap of this machine (Part 6) - the ceiling, or a quarter of what it has.</summary>
    private readonly long _memoryCap = MemoryPressure.Cap(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    /// <summary>What a picture costs once stepped down: a 256-pixel thumbnail, at most square.</summary>
    private const long ThumbnailBitmapBytes = 256L * 256 * 4;

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

    /// <summary>About once a second, as Part 4 sets the diagnostics stream.</summary>
    private static readonly TimeSpan DiagnosticsInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the fingers on this table are reported. Ten a second (Part 4): fast enough that a
    /// line traced across the map arrives as a line, slow enough that four people pointing is a
    /// tenth of the traffic one picture makes.
    /// </summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How often the display says what an evening has done to it.</summary>
    private static readonly TimeSpan PulseEvery = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Below half a device-independent pixel the two ends agree. Anything a hand or an eye could
    /// notice is above it by a wide margin.
    /// </summary>
    private const double DriftWorthSaying = 0.5;

    /// <summary>
    /// How long an exit may spend emptying the picture store. Windows gives a shutdown a few seconds
    /// in all, and the rest of the exit needs some of them.
    /// </summary>
    private static readonly TimeSpan EmptyWithin = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a restarted display waits for the process it replaces: that one's exit empties the
    /// store within <see cref="EmptyWithin"/> and closes its socket, with room to spare.
    /// </summary>
    private static readonly TimeSpan PredecessorPatience = TimeSpan.FromSeconds(10);

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

    /// <summary>What becomes of a store found at start: pruned by the first scenes, or emptied without a control.</summary>
    private StockLifetime? _stock;
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

    /// <summary>
    /// Where each picture was when this device took hold of it, until the hub has answered that
    /// grab - see <see cref="LateAnswerToOwnGrab"/>.
    /// </summary>
    private readonly ConcurrentDictionary<ItemId, ItemTransform> _grabbedAt = new();

    /// <summary>
    /// What only means something over hours - the numbers <c>3029</c> carries. They are counted
    /// rather than derived, because what is gone leaves no trace to derive from: a gesture that
    /// finished an hour ago is nowhere in the scene, and a connection that dropped and came back
    /// looks exactly like one that never did.
    /// </summary>
    private int _gestures;
    private int _connections;

    /// <summary>
    /// Whether a call for orphaned displays would be answered right now (Part 4). Opened by a
    /// connection to this device's own control that ended without a goodbye, a minute ago or more.
    /// </summary>
    private readonly TakeoverWindow _takeover = new(TimeProvider.System);

    /// <summary>Whether the store has been emptied for the end of this process - see <see cref="Empty"/>.</summary>
    private int _emptied;

    /// <summary>
    /// The control the last search's sighting answered a call FROM INSTEAD OF, or null when the
    /// sighting was this device's own control. Set by the search, taken by the next connection -
    /// the preferred host never goes through a search and so always reads null.
    /// </summary>
    private Guid? _answeringFor;

    /// <summary>
    /// Whether the control asked for the diagnostics stream on THIS connection. Every connection
    /// starts without it; a control that still wants it says so again (Part 4).
    /// </summary>
    private volatile bool _diagnosing;

    /// <summary>Whether the control wants the window list - forgotten with the connection, like the diagnostics wish.</summary>
    private volatile bool _windowsWanted;

    /// <summary>Who this device is connected to right now, for the bar - the beacon's name, or the host when it came by address.</summary>
    private volatile string? _connectedTo;

    /// <summary>The address this device reaches its control from, for the bar.</summary>
    private volatile string? _ownAddress;

    /// <summary>The round trip the control measured and sent with its last <c>Ping</c>; -1 before one (Part 4).</summary>
    private long _roundTripMs = -1;

    /// <summary>The name of the control the last search found, kept for the bar.</summary>
    private string? _heardName;

    /// <summary>The identifier of the control the last search found - the caller, when it answered a call.</summary>
    private Guid? _heardControl;

    /// <summary>
    /// The screens hidden at the device with the rescue mark - the local finding, kept apart from
    /// the control's, which only the control may lift (Part 3). Any arriving <c>ConfigUpdate</c>
    /// clears it: whoever has their control back wants to play on. UI thread only.
    /// </summary>
    private readonly HashSet<ScreenId> _hiddenHere = [];

    /// <summary>The rescue mark's grace as set for this device, or null for the default - kept to be written back (Part 6).</summary>
    private int? _rescueAfterSeconds;

    /// <summary>Whether the rescue mark is up for the absence running now - its line is written once.</summary>
    private bool _rescueUp;

    /// <summary>
    /// What this device is offering itself to after answering a call, and how to take it back: the
    /// mark's second line (Part 6). Null while it is not offering.
    /// </summary>
    private volatile Offer? _offer;

    private sealed record Offer(Guid Caller, string Name, CancellationTokenSource Withdraw);

    /// <summary>
    /// Whether the connection standing right now is an answer to a call - its pairing request puts
    /// no setup screen over the rescue mark (<see cref="SetupScreen"/>). Kept for the whole
    /// connection, unlike <see cref="_offer"/>, which a veto takes back.
    /// </summary>
    private volatile bool _answeringCall;

    private long _peakBytes;

    /// <summary>
    /// Touch reports sent, and the points inside them. <b>Two numbers rather than one, and the
    /// second is the one that says anything.</b>
    /// <para>
    /// A count of reports cannot tell a working table from a broken one, because of a rule two
    /// screens up: a finger that does not move is reported anyway, at the place it rests. So if
    /// the manipulation were swallowing every movement, the collector would see a finger go down
    /// and then hold still - and would go on reporting it ten times a second, at one point each.
    /// The traffic would not merely exist, it would look healthy.
    /// </para>
    /// <para>
    /// <b>What separates the two is points per report.</b> A moving finger contributes as many as
    /// the digitizer delivers in a tick - several, capped at 32; a resting or a swallowed one
    /// contributes exactly one. A ratio near one means no movement was ever collected, and that is
    /// the only reading at the table that can say so: what the trails SHOW needs the thumbnail of
    /// M4, so until then this line is the whole evidence (checks/M3.md, T1).
    /// </para>
    /// </summary>
    private int _touchReports;
    private int _touchPoints;

    /// <summary>
    /// When this process started, on the MONOTONIC clock. The wall clock jumps when Windows sets it:
    /// at a session on 18.10.2026 the display PC started 55 days behind, was corrected after two
    /// hours, and the pulse from then on said "standing after 1337:58:22".
    /// </summary>
    private readonly long _up = TimeProvider.System.GetTimestamp();

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

    /// <summary>
    /// Where each screen's window stood, for a screen shown as a window (Part 6) - kept beside the
    /// parameters in display.json, so the window comes back where it was put.
    /// </summary>
    private readonly Dictionary<ScreenId, WindowPlacement> _placements = [];

    /// <summary>What this device does about foreign windows (Part 6) - report and control, unless set otherwise.</summary>
    private ForeignWindowsMode _foreign = ForeignWindowsMode.ReportAndControl;

    /// <summary>The device's language as set (Part 8).</summary>
    private UiLanguage _language = UiLanguage.Automatic;

    /// <summary>What Windows showed at the start - the language Automatic stands for.</summary>
    private CultureInfo _system = CultureInfo.InstalledUICulture;

    /// <summary>The setup screen, one per monitor, while a pairing request lies before the DM.</summary>
    private readonly List<SetupWindow> _setup = [];
    private bool _windowed;

    /// <summary>
    /// The outgoing queue of the connection that stands right now, or <see langword="null"/>
    /// while there is none. A hot-plug while nothing is connected therefore reports nothing - and
    /// needs to report nothing, because the next <c>Hello</c> carries the inventory anyway.
    /// </summary>
    private SendQueues? _outbox;

    /// <summary>
    /// Whether the fingers on this table are reported. On until a control says otherwise: the
    /// switch lives in the control, and a device that has never heard from one cannot know it was
    /// turned off (Part 4, Part 7).
    /// </summary>
    private volatile bool _reportingTouches = true;

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

        // Before anything is read or opened: a restart from the control starts this process while
        // the old one is still emptying its store and holding its socket (DisplayOptions.ForRestart).
        WaitOut(options.After);

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

        // The language before the first window and the first line (Part 8). Windows take it once, at
        // their making; the log follows a later change at once. What Windows shows is kept first,
        // because Automatic means exactly that.
        _system = CultureInfo.CurrentUICulture;
        _language = loaded.Value.Device.Language ?? UiLanguage.Automatic;

        var culture = UiLanguages.CultureOf(_language, _system);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

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

        // The rescue mark's grace, and with it the takeover window's - one clock for both (Part 4).
        _rescueAfterSeconds = loaded.Value.Device.RescueAfterSeconds;
        _takeover.Grace = loaded.Value.Device.RescueAfter;

        // The frame counter starts with the process, not with the first overlay: a device that
        // renders badly while nothing is on it yet is worth knowing about too, and the window is
        // thirty seconds either way (Part 10).
        // The lines are written HERE rather than inside the watch, because the number is the
        // contract (Part 8): the display's reading is 3023 and the control's is 4013, and they say
        // different things about different surfaces. A shared class that logged for both would have
        // to pick one identifier, and an older counterpart would then render a plausible but wrong
        // line from it.
        _frames = FrameWatch.Always(
            PlayingScreens,
            frames => DisplayLog.FrameTimes(
                _logger,
                frames.Seconds,
                frames.Frames,
                frames.MedianMs,
                frames.P95Ms,
                frames.MaxMs,
                frames.CadenceMs,
                frames.CpuPercent,
                frames.GcMs,
                frames.Sweeps,
                frames.DrawMs,
                frames.HandMs),
            (screen, frames) => DisplayLog.FrameBudgetMissed(
                _logger,
                screen,
                frames.Missing,
                frames.MedianMs,
                frames.BudgetMs,
                frames.P95Ms,
                frames.StutterMs,
                frames.MaxMs,
                frames.CpuPercent));

        _ = Task.Run(() => PulseAsync(_shutdown.Token), _shutdown.Token);

        // With the process rather than with a connection, and that is not tidiness: the
        // collector is emptied by this loop, so one that ran per connection would let a table
        // with nobody listening accumulate a finger per touch for as long as no control was
        // there. Whether anything is SENT is a question about the socket; whether the
        // collector is drained is not.
        _ = Task.Run(() => ReportTouchesAsync(_shutdown.Token), _shutdown.Token);

        // The bar and the diagnostics stream, from one reading a second (Part 4, Part 6).
        _ = Task.Run(() => MeasureAsync(_shutdown.Token), _shutdown.Token);
        _ = Task.Run(() => ReportWindowsAsync(_shutdown.Token), _shutdown.Token);

        // The rescue mark, looked at once a second for the length of the process (Part 6).
        _ = Task.Run(() => WatchAbsenceAsync(_shutdown.Token), _shutdown.Token);

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
        // It ends with the process - unless the process ended hard, and then what is found here is
        // pruned by the first scenes or emptied when no control comes (Part 5, three rules).
        _cache = new AssetCache(Path.Combine(_dataRoot.Path, "cache"));
        _stock = new StockLifetime(_cache, TimeProvider.System);
        _stock.Begin(count => DisplayLog.StockEmptied(_logger, "because no control came", count, _cache.Count));
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
        _ = RunAsync(host, options.Port, _loggers).ContinueWith(
            loop => DisplayLog.ConnectionLoopFailed(_logger, loop.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Waits until the process this one replaces has ended - bounded, because a predecessor that
    /// hangs must not keep the display from ever coming up, and checked by name, because a process
    /// id can be handed to somebody else once its owner is gone.
    /// </summary>
    private static void WaitOut(int? predecessor)
    {
        if (predecessor is not { } id)
        {
            return;
        }

        try
        {
            using var old = Process.GetProcessById(id);
            using var self = Process.GetCurrentProcess();

            if (string.Equals(old.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                _ = old.WaitForExit(PredecessorPatience);
            }
        }
        catch (ArgumentException)
        {
            // Gone already - the ordinary case when the old one was quick.
        }
        catch (InvalidOperationException)
        {
            // Ended between the two questions.
        }
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

        foreach (var screen in _settings.Screens.Where(screen => screen.Window is not null))
        {
            _placements.TryAdd(new ScreenId(screen.ScreenId), screen.Window!);
        }

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
        _ = _outbox?.TrySend(new ScreensChangedMessage(screens));
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
        // <b>Whoever has their control back wants to play on</b> (Part 6): every arriving update
        // lifts what the rescue mark hid, before anything else, and the windows come back in exactly
        // the states that stood there all along - nothing was written over them.
        var unhidden = _hiddenHere.ToList();

        _hiddenHere.Clear();

        foreach (var screen in unhidden)
        {
            Show(screen);
        }

        foreach (var screen in update.Screens)
        {
            if (screen.Settings is { IsEmpty: false } settings && _contexts.TryGetValue(screen.Screen, out var context))
            {
                _contexts[screen.Screen] = settings.ApplyTo(context);

                // Overlay and window are two frames, not two switches on one: the window is built
                // anew in the other one, and Show below puts it down again.
                if (settings.Presentation is { } presentation && presentation != context.Presentation)
                {
                    Close(screen.Screen);
                }

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

            if (device.RescueAfterSeconds is { } seconds)
            {
                _rescueAfterSeconds = seconds;
                _takeover.Grace = device.RescueAfter;
            }

            _foreign = device.ForeignWindows ?? _foreign;

            // The log at once; the setup screen, rescue mark and diagnostic bar at the next start.
            if (device.Language is { } language)
            {
                _language = language;
                _log.Culture = UiLanguages.CultureOf(language, _system);
            }
        }

        // Not stored and not reported back, unlike everything above it: this is the control's one
        // switch for every device, and a device has no opinion about it to preserve (Part 6). A
        // restart therefore reports fingers again until the control says otherwise, which is the
        // right way round - the control knows, the table does not.
        if (update.TouchPoints is { } reporting)
        {
            _reportingTouches = reporting;
            DisplayLog.TouchReporting(_logger, reporting ? "on" : "off");
        }

        // The touch switch alone is NOT a reason to write a file or say a line. It travels on every
        // connection - it has to, there is no per-device wish a catalogue could keep - so counting
        // it here would mean a save and a "settings applied 0" per reconnect, on a device whose
        // reconnects are exactly what one reads the log for.
        if (update.Screens.Count == 0 && update.Device is null or { IsEmpty: true })
        {
            return;
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

                    // The OPINION, not the full set: a value nobody ever touched is not written, so
                    // a corrected default reaches this machine on the next start instead of being
                    // overwritten by its own file (Guide G13, ScreenSettings.Opinion).
                    ScreenSettings.Opinion(pair.Value, _names.GetValueOrDefault(pair.Key)),
                    _placements.GetValueOrDefault(pair.Key))),
            ],
            Device = new DeviceSettings(_log?.Level, _forwarding?.AtLeast, _wake?.Wanted, _rescueAfterSeconds, _foreign, _language),
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
            || _hiddenHere.Contains(screen)
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

        var windowed = Windowed(screen);
        var window = new OverlayWindow(monitor, windowed) { State = command.State };

        // A window comes back where it was put; an overlay always covers its monitor.
        if (windowed)
        {
            if (_placements.TryGetValue(screen, out var placed))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = placed.Left;
                window.Top = placed.Top;
                window.Width = placed.Width;
                window.Height = placed.Height;

                if (placed.Maximised)
                {
                    window.WindowState = WindowState.Maximized;
                }
            }

            window.LocationChanged += (_, _) => Placed(screen, window);
            window.SizeChanged += (_, _) => Placed(screen, window);
            window.StateChanged += (_, _) => Placed(screen, window);
        }

        // A window has a close box, and Alt+F4 reaches it. Closed from outside, it is forgotten here
        // too - a closed window kept in the table made the screen dead for good: Show only told the
        // dead window its state, and Close called Close on it again. The next Show puts a new one
        // down. Closed from here, Close has taken it out of the table already and this does nothing.
        window.Closed += (_, _) => Forget(screen, window);

        // Before Show, because the layout that gives the stage its size happens inside it. The
        // scene is normalised against the surface it is drawn on, so a surface that changes size
        // has to be drawn on again - otherwise the first drawing stands on a guess until the next
        // arrival happens to correct it.
        window.SurfaceChanged += () => Draw(new ScreenRef(_device, screen));

        window.ParkMeasured += (towards, travel, push, outcome) =>
            DisplayLog.ParkGesture(_logger, towards, travel, push, outcome);

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
            _ = _outbox?.TrySend(new ItemParkedMessage(screen, item, parked));
        };

        // A gesture ended with nothing to report - the park - so the scene has moved and nothing
        // else will ask for a drawing.
        window.Settled += () => Draw(new ScreenRef(_device, screen));

        // The rescue mark: one tap hides every screen of this device, not only the one tapped - the
        // taskbar may be on another screen (Part 6). The second line takes an answer back.
        window.HideRequested += HideHere;
        window.OfferWithdrawn += Withdraw;

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

        // The sender walks this rather than the windows: it runs on a background thread, and the
        // window table belongs to the UI one.
        _touching[screen] = window.Fingers;

        window.Show();

        DisplayLog.OverlayOpened(_logger, screen, _windowed ? "windowed" : "overlay");

        // Whatever was already known about this screen is drawn at once - a screen switched from
        // inactive to active has to stand there complete, not empty until the DM next touches it.
        // That is what makes preparing on an inactive screen work at all (Part 3).
        Draw(new ScreenRef(_device, screen));
    }

    /// <summary>Whether a screen is shown as a window: by its parameter, or for every screen by <c>--windowed</c> for this session (Part 6).</summary>
    private bool Windowed(ScreenId screen) =>
        _windowed || (_contexts.TryGetValue(screen, out var context) && context.Presentation is ScreenPresentation.Window);

    /// <summary>A window was moved or sized: kept, so it stands there after the next start too.</summary>
    private void Placed(ScreenId screen, OverlayWindow window)
    {
        if (!window.IsLoaded)
        {
            return;
        }

        var bounds = window.WindowState is WindowState.Maximized ? window.RestoreBounds : new System.Windows.Rect(window.Left, window.Top, window.Width, window.Height);

        _placements[screen] = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState is WindowState.Maximized);
        Remember();
    }

    private void Close(ScreenId screen)
    {
        _ = _touching.TryRemove(screen, out _);

        if (!_windows.Remove(screen, out var window))
        {
            return;
        }

        window.Close();
        DisplayLog.OverlayClosed(_logger, screen);
    }

    /// <summary>A window that was closed from outside - see the <c>Closed</c> handler in <see cref="Show"/>.</summary>
    private void Forget(ScreenId screen, OverlayWindow window)
    {
        if (!_windows.TryGetValue(screen, out var standing) || !ReferenceEquals(standing, window))
        {
            return;
        }

        _ = _windows.Remove(screen);
        _ = _touching.TryRemove(screen, out _);
        DisplayLog.OverlayClosed(_logger, screen);
    }

    /// <summary>
    /// Windows is logging off or shutting down - typically the display PC switched off right after a
    /// screen was renamed (Part 6, step 38c). The settings go to disk at once, past their debounce,
    /// and the store is emptied here rather than left to <see cref="OnExit"/>, which may not get its
    /// turn before Windows ends the process. Both are what every end does anyway (Part 5, rule 1);
    /// this only does them early enough.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _configuration?.Flush();

        if (_logger is not null)
        {
            DisplayLog.SessionEnding(_logger, e?.ReasonSessionEnding);
        }

        Empty("on session end");

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Empty("on exit");
        Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Settles a store found at start once every screen of this device has its scene - the hub sends
    /// one snapshot per screen after connecting, an empty one included, and only all of them
    /// together say which pictures are needed. Inactive screens count: the scene stands there too
    /// (Part 3). Asked after every snapshot; it acts once.
    /// </summary>
    private void Settle(List<ScreenId> screens)
    {
        if (_stock is null || !screens.All(id => _scenes.ContainsKey(new ScreenRef(_device, id))))
        {
            return;
        }

        var mine = _scenes.Of(_device).Select(pair => pair.Value);

        if (_stock.Settle(SceneAssets.InUse(mine)) is { Adopted: > 0 } settled)
        {
            DisplayLog.StockSettled(_logger, settled.Adopted, settled.Discarded);
        }
    }

    /// <summary>
    /// Every end empties the store, newest first, and within a time bound rather than at any cost:
    /// Windows grants a shutdown a few seconds, and what is left the next start prunes (Part 5,
    /// rules 1 and 2). <c>ShutdownBlockReasonCreate</c> stays struck - a display PC holding up a
    /// shutdown with a warning page is worse than a remnant on disk (Part 6).
    /// </summary>
    private void Empty(string occasion)
    {
        // Once per process: the end of a Windows session empties early, and the exit that follows
        // would otherwise write a second line about a store that is already empty.
        if (_cache is null || Interlocked.Exchange(ref _emptied, 1) == 1)
        {
            return;
        }

        _stock?.Dispose();

        using var bound = new CancellationTokenSource(EmptyWithin);
        var removed = _cache.Empty(bound.Token);

        DisplayLog.StockEmptied(_logger, occasion, removed.Count, _cache.Count);
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
    /// Finding a control and staying with it, for as long as this application runs. The loop
    /// itself lives in <see cref="ConnectionLoop"/>, where it can be tested; what stays here is
    /// what only the application knows - which control this device is bound to right now, and
    /// what one connection does to its windows.
    /// </summary>
    private Task RunAsync(
        string? preferred,
        int port,
        ILoggerFactory loggers)
    {
        var client = new DisplayClient(loggers.CreateLogger<DisplayClient>(), Power.Battery);
        var discovery = new DiscoveryListener(loggers.CreateLogger<DiscoveryListener>());
        var loop = new ConnectionLoop(loggers.CreateLogger<ConnectionLoop>(), TimeProvider.System, new ReconnectBackoff());

        return loop.RunAsync(
            preferred is null ? null : new HubAddress(preferred, port),

            // Read on every search, not once: after a pairing this device is bound to a control it
            // did not know when the loop started.
            listening => SearchAsync(discovery, listening),
            (target, _) => ConnectAsync(client, discovery, target),
            _shutdown.Token);
    }

    /// <summary>
    /// The rescue mark, once a second (Part 6): up on every screen with an overlay once the control
    /// has been gone for the grace period - the same clock as the takeover window, so the mark is up
    /// exactly when this device would answer a call. Not in windowed mode, where the taskbar lies
    /// free; and nowhere on a device that was never connected in this run, which has either no
    /// window or a setup screen that means a control is very much there.
    /// </summary>
    private async Task WatchAbsenceAsync(CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var due = _takeover.IsOpen;
                var absent = _takeover.Absent;
                var offer = _offer;

                string? words = null;

                if (due && absent is { } gone)
                {
                    words = Strings.NoControlFor(Math.Max(1, (int)gone.TotalMinutes));
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    var overlays = RescueReach.Of(_windows.Select(pair => (pair.Key, pair.Value.Windowed))).Count;

                    if (words is not null && !_rescueUp && overlays > 0)
                    {
                        _rescueUp = true;
                        DisplayLog.RescueMarkUp(_logger, overlays, (int)_takeover.Grace.TotalSeconds);
                    }
                    else if (words is null)
                    {
                        _rescueUp = false;
                    }

                    // Where an overlay lies and only there: a window leaves the taskbar free, and the
                    // mark rescues the operability of a device whose taskbar is covered (Part 6).
                    foreach (var window in _windows.Values)
                    {
                        window.Rescue(window.Windowed ? null : words, offer?.Name);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The process is going away.
        }
    }

    /// <summary>
    /// "Hide" on the rescue mark: every screen with an overlay is hidden at the device - a finding,
    /// not a state, so <c>State</c> stays what the DM wished, frozen stays frozen, and with the
    /// control back the windows return as they were (Part 6, step 43a). The scene stays in memory.
    /// </summary>
    private void HideHere()
    {
        var screens = RescueReach.Of(_windows.Select(pair => (pair.Key, pair.Value.Windowed)));

        foreach (var screen in screens)
        {
            _hiddenHere.Add(screen);
            Show(screen);
        }

        DisplayLog.HiddenAtDevice(_logger, screens.Count);
    }

    /// <summary>
    /// "Don't offer" on the mark's second line: the request at the caller's is dropped and that
    /// caller is not answered again until this device is accepted somewhere - the veto, not the gate
    /// (Part 6).
    /// </summary>
    private void Withdraw()
    {
        if (_offer is not { } offer)
        {
            return;
        }

        _takeover.Veto(offer.Caller);
        _offer = null;

        try
        {
            offer.Withdraw.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt ended in the same moment; there is nothing left to take back.
        }

        DisplayLog.OfferWithdrawn(_logger, offer.Name);
    }

    /// <summary>
    /// The address of this machine on the way to the control - the one a human would compare, on a
    /// machine with a dock, a Wi-Fi and a Hyper-V adapter (Part 4). Asked of the routing table by
    /// "connecting" a datagram socket, which sends nothing.
    /// </summary>
    private static string? LocalAddressTowards(string host, int port)
    {
        try
        {
            using var probe = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);

            probe.Connect(host, port);

            return (probe.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString();
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// One search for a control: this device's own, or - while its takeover window is open - one
    /// calling for orphaned displays (Part 4).
    /// </summary>
    private async Task<Sighting?> SearchAsync(DiscoveryListener discovery, CancellationToken listening)
    {
        var bound = _settings.ControlId;
        var sighting = await discovery.ListenAsync(bound, beacon => _takeover.Answers(beacon.ControlId), listening).ConfigureAwait(false);

        _answeringFor = sighting is not null && bound is { } own && sighting.Beacon.ControlId != own ? own : null;
        _heardName = sighting?.Beacon.Name;
        _heardControl = sighting?.Beacon.ControlId;

        return sighting;
    }

    /// <summary>Runs one connection to the end, and says what it amounted to.</summary>
    private async Task<Attempt> ConnectAsync(
        DisplayClient client,
        DiscoveryListener discovery,
        HubAddress target)
    {
        var answeringFor = _answeringFor;
        var heardName = _heardName;
        var heardControl = _heardControl;
        _answeringFor = null;
        _heardName = null;
        _heardControl = null;
        _diagnosing = false;
        _windowsWanted = false;

        var inbox = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        // Every attempt builds its own Hello. After a pairing this device has a token where it had
        // a code a moment ago - a message built once at startup would go on introducing this
        // machine as a stranger for as long as it runs.
        //
        // Built on the UI thread: it walks the screens, their names and parameters, and those
        // tables belong to it - a reconnect after waking the PC lands in the same moment as a burst
        // of DisplaySettingsChanged that rebuilds them.
        var hello = await Dispatcher.InvokeAsync(Introduction);

        _hubHttp = new Uri($"http://{target.Host}:{target.Port}/");

        var hubUri = new Uri($"ws://{target.Host}:{target.Port}{Protocol.DisplayPath}");

        // Runs for the length of THIS CONNECTION, and the token has to say so - not the one that
        // means "the application is going away". The forwarder parks in a wait that only a new log
        // entry wakes; with the shutdown token it would still be parked there long after the socket
        // was gone, and the await below would hold this method for ever. That is not a leak, it is
        // the reconnect: RunAsync never comes round again, and the device sits with its overlays
        // and never looks for a control any more.
        using var connected = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        // The socket's own lifetime, separate from the application's, for the one case that ends
        // it from here: this device's own control coming back while it stands as a request at a
        // stranger's.
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var welcomed = 0;

        _answeringCall = answeringFor is not null;

        // Offering itself to a caller: the rescue mark says so, and can take it back (Part 6).
        if (answeringFor is not null && heardControl is { } caller)
        {
            _offer = new Offer(caller, heardName ?? target.Host, abandon);
        }

        // <b>The old control wins for as long as nothing has been accepted</b> (Part 4). An answer
        // to a call is only a request at the caller's; while it waits, this device goes on
        // listening for its own control and drops the request the moment that one is heard. A
        // request dies with its connection and leaves nothing behind - the hub's rule, not a new one.
        var ownSearch = answeringFor is { } own
            ? Task.Run(
                async () =>
                {
                    var back = await discovery.ListenAsync(own, connected.Token).ConfigureAwait(false);

                    if (back is not null && Volatile.Read(ref welcomed) == 0)
                    {
                        DisplayLog.OwnControlBack(_logger, back.Beacon.Name, back.Address);
                        await abandon.CancelAsync().ConfigureAwait(false);
                    }
                },
                CancellationToken.None)
            : Task.CompletedTask;

        Task forwarding = Task.CompletedTask;
        Task reporting = Task.CompletedTask;

        // Handed the three queues the moment the socket stands. Until M3c this end had one
        // unbounded channel, so nothing here could be dropped and nothing could be replaced -
        // and gestures and touch points are exactly what needs both (Part 4, M3 siting question 2).
        void Ready(SendQueues queues)
        {
            // What a hot-plug, a locally changed setting and every gesture reach the wire through,
            // for as long as this connection stands.
            _outbox = queues;

            // The mark it reads from survives the connection, so whatever came up while there was
            // none goes out with the next one (Part 8).
            forwarding = Task.Run(() => _forwarding!.RunAsync(queues, connected.Token));

            // Same lifetime as the forwarder, and for the same reason: it belongs to THIS
            // connection.
            reporting = Task.Run(() => ReportProgressAsync(queues, connected.Token));

        }

        var pump = Task.Run(() => client.RunAsync(
            hubUri,
            hello,
            inbox.Writer,
            DisplayOptions.SendLimits,
            Ready,
            abandon.Token));

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

            if (message is PingMessage { RoundTripMs: { } measured })
            {
                Volatile.Write(ref _roundTripMs, measured);
            }

            if (message is WelcomeMessage)
            {
                // Accepted: an offer is a connection now, and there is nothing left to take back.
                _offer = null;

                _connectedTo = heardName is null ? target.Host : string.Create(CultureInfo.InvariantCulture, $"{heardName} ({target.Host})");
                _ownAddress = LocalAddressTowards(target.Host, target.Port);

                // Accepted, at its own control or at the caller's - which is then its own from
                // here on. Either way the window shuts, and the search for the old one has
                // nothing left to win.
                Volatile.Write(ref welcomed, 1);
                _takeover.Connected();
            }

            // One message that fails costs that message, not the connection and not the search for
            // a control (Part 1) - see MessageGuard.
            await MessageGuard.HandleAsync(
                () => HandleAsync(message),
                fault => MessageFailed(message, fault),
                _shutdown.Token).ConfigureAwait(false);
        }

        // Only a connection that was accepted opens the window when it ends. A request that stood
        // at a stranger's, or a refusal, was never this device's control going missing.
        if (welcomed == 1)
        {
            _takeover.Lost();
        }

        // A request is a connection: when it ends, nothing lies before the DM any more, and the
        // setup screen goes with it - the next attempt brings it back if it is still waiting.
        await Dispatcher.InvokeAsync(CloseSetup);

        _connectedTo = null;
        Volatile.Write(ref _roundTripMs, -1);

        // Let go on the way out, not on the way back in: from here the screens may go dark, which
        // is the right answer for a table nobody is playing on any more.
        _wake!.Connected = false;

        _outbox = null;

        // The forwarder is waiting on the ring buffer rather than on anything the socket owns, so
        // the connection has to say out loud that it is over.
        await connected.CancelAsync().ConfigureAwait(false);

        await pump.ConfigureAwait(false);
        await forwarding.ConfigureAwait(false);
        await ownSearch.ConfigureAwait(false);

        _offer = null;
        _answeringCall = false;

        // Dropped for its own control: worth starting from the top at once, the beacon is there.
        return abandon.IsCancellationRequested && !_shutdown.IsCancellationRequested ? Attempt.Worthwhile : attempt;
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
        _ = _outbox?.TrySend(new ConfigUpdateMessage(update));
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
    private HelloMessage Introduction()
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

            // The name as it stands NOW, not as it stood at start: a rename from the control changes
            // it while this process runs, and the hub's lines about this device are built from here.
            _deviceName,
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
                new DeviceSettings(
                    _log?.Level,
                    _forwarding?.AtLeast,
                    _wake?.Wanted,
                    (int)_takeover.Grace.TotalSeconds,
                    _foreign,
                    _language)),

            // What this device still has on its screens. A control that has just restarted takes
            // it over, and only where it has no scene of its own - which is the one exception to
            // "the hub is authoritative", and it is kept this narrow on purpose (Part 4).
            [.. _scenes.Of(_device)
                .Select(pair => new ScreenScene(pair.Key.Screen, pair.Value))]);
    }

    /// <summary>
    /// A message whose handling threw. Said with the fault, because nothing else shows it: the
    /// connection carries on, and whatever the message should have done is simply missing.
    /// </summary>
    private void MessageFailed(ProtocolMessage message, Exception fault)
    {
        // INTERIM: 4012 is the nearest line there is, and its text ("going down") is wrong here - the
        // connection carries on. To be replaced by a line of its own (MessageHandlingFailed), which
        // needs its catalogue entry first.
        var where = $"the handling of {message.GetType().Name} (the connection carries on)";

        DisplayLog.UnhandledFault(_logger, fault, where);
    }

    private async Task HandleAsync(ProtocolMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                _assetPath = welcome.AssetPath;
                _connections++;
                Remember(welcome);
                await Dispatcher.InvokeAsync(CloseSetup);
                break;

            case PairingPendingMessage pending:
                // The setup screen: name, address and code, large enough to read from two metres -
                // exactly while the request lies before the DM (Part 4, Part 6). Not for an answer
                // to a call: there the rescue mark shows it and carries the veto (SetupScreen).
                DisplayLog.PairingPending(_logger, pending.PairingCode);

                if (SetupScreen.Wanted(_answeringCall))
                {
                    await Dispatcher.InvokeAsync(() => ShowSetup(pending.PairingCode));
                }

                break;

            case RejectedMessage rejected:
                await Dispatcher.InvokeAsync(CloseSetup);
                Refused(rejected.Reason);
                break;

            case SceneSnapshotMessage snapshot:
                // The same order as a patch, and for the same reason: the arrangement stands and is
                // drawn at once, the pictures come after. A snapshot is the reconnect case, so it
                // is exactly the moment somebody has a hand on the table and is waiting to be
                // answered again.
                _scenes.Set(snapshot.Screen, snapshot.Scene);
                await RenderAsync(snapshot.Screen).ConfigureAwait(false);
                Fetch(snapshot.Screen);

                // The monitors are the UI thread's: Survey clears and refills them on every
                // DisplaySettingsChanged, and a resolution change brings one snapshot per screen
                // in the same moment. Only the list is taken there; the store is settled here.
                Settle(await Dispatcher.InvokeAsync(() => _monitors.Keys.ToList()));
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

            case DeviceCommandMessage command:
                await Dispatcher.InvokeAsync(() => Obey(command));
                break;

            case GoIdleMessage:
                // The control's orderly goodbye: the start state, and no takeover window for its
                // successor - the DM ended the evening rather than losing the control (Part 4).
                _takeover.SaidGoodbye();
                _scenes.Clear();
                await Dispatcher.InvokeAsync(GoIdle);
                break;

            case DiagnosticsRequestMessage request:
                // Said when it changes, so "switching the view off stops the device sending" can
                // be read off this device's log (step 37a).
                if (_diagnosing != request.On)
                {
                    DisplayLog.DiagnosticsRequested(_logger, request.On);
                }

                _diagnosing = request.On;
                break;

            case WindowsRequestMessage request:
                _windowsWanted = request.On;
                break;

            case WindowCommandMessage command:
                await Dispatcher.InvokeAsync(() => Lay(command));
                break;

            case SpotlightPulseMessage pulse:
                // Onto the UI thread, like every other drawing: this arrives on the connection
                // task. A pulse for a screen we have no window for is simply dropped - an inactive
                // screen has been given back to Windows and has nothing to point at (Part 3).
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_windows.TryGetValue(pulse.Screen, out var window))
                    {
                        window.Spotlight(new DnDOverlay.Core.Point(pulse.X, pulse.Y));
                    }
                });

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
    /// <summary>
    /// The coarse device commands (Part 7): a new name - the screens' default labels follow it and go
    /// up at once -, the picture store emptied and refilled for what lies on the screens, or a restart
    /// of this application, which connects again by itself.
    /// </summary>
    /// <summary>
    /// The start state, as after switching on (Part 3): every screen <see cref="ScreenState.Inactive"/>,
    /// so <see cref="Show"/> closes every overlay. Nothing stored changes - the next control sets the
    /// states again from its wishes.
    /// </summary>
    private void GoIdle()
    {
        foreach (var screen in _states.Keys.ToList())
        {
            _states[screen] = new ScreenCommand(ScreenState.Inactive);
            Show(screen);
        }

        _windowsWanted = false;
        _diagnosing = false;
        DisplayLog.WentIdle(_logger, _states.Count);
    }

    private void Obey(DeviceCommandMessage command)
    {
        switch (command.Command)
        {
            case DeviceCommand.Rename when command.Name is { Length: > 0 } name:
                _deviceName = name;
                _settings = _settings with { DeviceName = name };
                _configuration?.Save(_settings);
                DisplayLog.DeviceRenamed(_logger, name);

                // The same path as a monitor change: survey under the new name, report the labels.
                OnDisplaySettingsChanged(this, EventArgs.Empty);
                break;

            case DeviceCommand.ClearCache:
                var emptied = _cache.Empty();

                DisplayLog.CacheCleared(_logger, emptied.Count);

                // Every picture on the screens is decoded, so a fetch that asks for what is missing
                // would ask for nothing - they are marked to be fetched again (AssetsWanted).
                foreach (var asset in SceneAssets.InUse(_scenes.Values))
                {
                    _refill[asset] = 0;
                }

                foreach (var screen in _scenes.Keys.ToList())
                {
                    Fetch(screen);
                }

                break;

            case DeviceCommand.Restart:
                DisplayLog.Restarting(_logger);
                _configuration?.Flush();

                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };

                // The new one waits until this one is gone (DisplayOptions.ForRestart). A start that
                // throws leaves this process running and connected; the fault is said like any other.
                foreach (var argument in DisplayOptions.ForRestart([.. Environment.GetCommandLineArgs().Skip(1)], Environment.ProcessId))
                {
                    start.ArgumentList.Add(argument);
                }

                _ = System.Diagnostics.Process.Start(start);
                Shutdown();
                break;

            default:
                break;
        }
    }

    /// <summary>Puts the setup screen on every monitor, or leaves it where it already stands.</summary>
    private void ShowSetup(string code)
    {
        if (_setup.Count > 0)
        {
            return;
        }

        foreach (var monitor in _monitors.Values)
        {
            var window = new SetupWindow(monitor, _deviceName, code);

            window.Show();
            _setup.Add(window);
        }
    }

    private void CloseSetup()
    {
        foreach (var window in _setup)
        {
            window.Close();
        }

        _setup.Clear();
    }

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

                // And it is the id the next Hello carries. Written to the file only, the device went
                // on sending the old id: the hub probed the original again, found a clone again, and
                // the DM was asked again - with yet another unused id written each time. What this
                // device holds goes along: its scenes are still its own (Part 3, Part 7).
                var fresh = new DeviceId(_settings.DeviceId);

                _scenes.Rekey(_device, fresh);
                _device = fresh;

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
            IReadOnlyList<ItemId> arrived = [];

            // From the read to the write in one change of the book: a hand letting go writes into
            // the same scene on the UI thread, and two unguarded read-modify-writes lose one of the
            // two - the release that springs back, or this patch (Part 4, conflict rule 2).
            _scenes.Change(group.Key, known =>
            {
                var scene = known ?? SceneState.Empty;

                // Asked BEFORE the operations are applied, because "was this screen already
                // occupied?" has no answer afterwards.
                arrived = Arrival.Marked(scene, ops);

                foreach (var op in ops)
                {
                    if (op is TransformItem moved && LateAnswerToOwnGrab(moved, scene, context) is { } depthOnly)
                    {
                        // Not measured as the answer to the release either: it answers the grab, and
                        // pairing it with the release is what made the evening's log read 886 DIP.
                        scene = SceneReducer.Apply(scene, depthOnly, context);

                        continue;
                    }

                    scene = SceneReducer.Apply(scene, op, context);

                    if (op is ParkItem { Parked: false } unparked)
                    {
                        BringBack(group.Key.Screen, scene, unparked.Item);
                    }

                    if (op is TransformItem confirming)
                    {
                        Confirmed(confirming, context, scene);
                    }

                    if (op is RemoveItem removed)
                    {
                        // An item that goes away while a hand was on it leaves an entry in the
                        // throttle that nothing would ever clear again - its binding report is the
                        // one that never comes (Part 4, conflict rule 4).
                        _throttle.Forget(removed.Item);
                        _grabbedAt.TryRemove(removed.Item, out _);
                    }
                }

                return scene;
            });

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

        var wanted = AssetsWanted.Of(scene, _images.ContainsKey, asset => _refill.TryRemove(asset, out _));

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

        // <b>Thumbnails jump the queue; pictures keep their order.</b> Two rules, and they are about
        // different things - the first is about kind, the second about time. Neither is about size.
        //
        // Part 5 promises a picture STANDS at its place blurred within a second and is replaced when
        // the original lands, and that promise is kept by the order things are DECODED in, not the
        // order they arrive in. While one picture was fetched at a time the two coincided:
        // thumbnail, original, next picture. With three at a time they come apart, and the table
        // showed it the same evening the parallel fetching first engaged - a full decode costs one
        // to two and a half seconds on that machine, and every thumbnail landing during it waited
        // behind it. The runner saw rings filling over nothing where a blurred picture belonged.
        //
        // Among the originals the order is <b>the order they came in</b> and nothing else. <i>"The
        // DM has already thought about the order when he sends them"</i> - sorting them here by
        // anything of ours, size above all, would quietly overrule a decision somebody made on
        // purpose.
        var thumbnails = new Queue<AssetArrived>();
        var pictures = new Queue<AssetArrived>();

        while (true)
        {
            // Taken off the channel as fast as they come, so a thumbnail that lands in the middle of
            // a long decode is already in hand when that decode ends.
            while (arrivals.Reader.TryRead(out var arrived))
            {
                (arrived.IsThumbnail ? thumbnails : pictures).Enqueue(arrived);
            }

            if (thumbnails.Count == 0 && pictures.Count == 0)
            {
                if (!await arrivals.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            var next = thumbnails.Count > 0 ? thumbnails.Dequeue() : pictures.Dequeue();

            // <b>A blur nobody has to wait for is not worth showing.</b> When a picture comes out of
            // the store rather than off the wire, its thumbnail and its original land in the same
            // instant - and taking all the thumbnails first then puts twenty blurred pictures on the
            // table at once, followed by twenty sharpenings. The runner named it: everything appears
            // together instead of one after another. So a thumbnail whose original is already in
            // hand is dropped, and the pictures come up sharp, one after another, in the order they
            // arrived - which for a full store is the order the DM sent them in.
            if (next.IsThumbnail && pictures.Any(picture => picture.Asset == next.Asset))
            {
                continue;
            }

            Decode(next, steps.GetValueOrDefault(next.Asset));
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

            _failed[failure.Asset] = Strings.PictureNotLoaded;
        }

        if (run.Failed.Count > 0)
        {
            await Dispatcher.InvokeAsync(DrawEverywhere);
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
            _ = _failed.TryRemove(arrived.Asset, out _);

            // Kept only for what moves, and only from the full picture - a thumbnail of an
            // animation is a still, and animating it would show the wrong thing sharply.
            if (!arrived.IsThumbnail && Animated(arrived.Asset))
            {
                _moving[arrived.Asset] = arrived.Bytes;
            }

            if (!arrived.IsThumbnail)
            {
                // A picture that has just arrived is the newest on the table, not the oldest: it
                // counts as touched, or the memory rule would soften exactly the picture the DM
                // just sent.
                _touchedAt[arrived.Asset] = Interlocked.Increment(ref _touches);
                _ = _steppedDown.TryRemove(arrived.Asset, out _);

                // Only the full picture ends the load. Reporting done on the thumbnail would fill
                // the ring while the picture on the table was still the blurred one.
                _progress.Done(arrived.Asset);

                // Looked up before the call rather than inside it: an argument that costs something
                // must not be evaluated when the level would throw the line away (CA1873).
                var name = NameOf(arrived.Asset);
                var spent = (long)Stopwatch.GetElapsedTime(clock).TotalMilliseconds;

                DisplayLog.AssetDecoded(
                    _logger, name, arrived.Asset, decoded.PixelWidth, decoded.PixelHeight, spent);

                Relieve();
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

            // Only when nothing at all is on the table for it: an unreadable full picture over a
            // thumbnail that is already standing leaves the thumbnail, which is better than a frame.
            if (!_images.ContainsKey(arrived.Asset))
            {
                _failed[arrived.Asset] = Strings.PictureNotRead;
            }
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
    /// <summary>A hand took an item: its picture is the newest touched, and back at full sharpness if pressure had put it down.</summary>
    private void Touched(ScreenId screen, ItemId item)
    {
        if (!_scenes.TryGetValue(new ScreenRef(_device, screen), out var scene))
        {
            return;
        }

        if (scene.Items.OfType<ImageItem>().FirstOrDefault(one => one.ItemId == item) is { } image)
        {
            _touchedAt[image.AssetId] = Interlocked.Increment(ref _touches);
        }

        BringBack(screen, scene, item);
    }

    /// <summary>
    /// Takes a stepped-down picture back up to its base step - "fetching back a picture that pressure
    /// put down" is one of the four occasions Part 6 allows a new decode for. Off the UI thread, like
    /// every sharpening; the picture stays where it is and goes sharp when the decode is done.
    /// </summary>
    private void BringBack(ScreenId screen, SceneState scene, ItemId item)
    {
        if (scene.Items.OfType<ImageItem>().FirstOrDefault(one => one.ItemId == item) is not { } image
            || !_steppedDown.TryRemove(image.AssetId, out _)
            || !_contexts.TryGetValue(screen, out var context)
            || !_images.TryGetValue(image.AssetId, out var current)
            || current is not BitmapSource bitmap)
        {
            return;
        }

        var edge = Math.Max(context.Size.Width, context.Size.Height);
        var step = DecodeSteps.Base(edge, image.Meta.PixelWidth, image.Meta.PixelHeight);

        if (_sharpening.TryAdd(image.AssetId, 0))
        {
            _ = Task.Run(() => SharpenAsync(screen, image.AssetId, bitmap, step), _shutdown.Token);
        }
    }

    /// <summary>
    /// Measures the process against the memory cap and, if it is over a line, steps pictures down to
    /// their thumbnails in the order <see cref="MemoryPressure"/> gives - the fan first, the table at
    /// the cap, never what a hand is on, never the background. Called after every decode that made a
    /// picture larger, which is the only moment memory grows by a picture's worth.
    /// </summary>
    private void Relieve()
    {
        if (!_relieving.TryEnter())
        {
            return;
        }

        try
        {
            var sweeps = GC.CollectionCount(2);

            if (sweeps != _reliefSweeps)
            {
                _reliefSweeps = sweeps;
                _pendingRelief = 0;
            }

            long committed;

            using (var me = Process.GetCurrentProcess())
            {
                committed = me.PrivateMemorySize64;
            }

            var measured = committed - _pendingRelief;

            if (measured <= (long)(_memoryCap * MemoryPressure.FanShare))
            {
                return;
            }

            // The holds live on the UI thread. One short question there, and only under pressure.
            var held = Dispatcher.Invoke(() => _windows.Values.SelectMany(window => window.HeldItems).ToHashSet());
            var steps = MemoryPressure.StepDown(measured, _memoryCap, Candidates(held), ThumbnailBitmapBytes);
            var stepped = 0;

            foreach (var asset in steps)
            {
                if (!_cache.TryGetThumbnail(asset, out var thumbnail)
                    || !_images.TryGetValue(asset, out var before)
                    || before is not BitmapSource large)
                {
                    continue;
                }

                var small = PictureDecoder.Decode(thumbnail, 0);

                // Marked BEFORE the bitmap is swapped, and swapped only if nothing replaced the large
                // one meanwhile. The other order let a redraw in between see a small bitmap that was
                // not marked yet and sharpen it straight back up, while the books counted the saving.
                _steppedDown[asset] = 0;

                if (!_images.TryUpdate(asset, small, before))
                {
                    _ = _steppedDown.TryRemove(asset, out _);
                    continue;
                }

                _pendingRelief += Math.Max(0, ((long)large.PixelWidth * large.PixelHeight * 4) - ThumbnailBitmapBytes);
                stepped++;
            }

            if (stepped == 0)
            {
                return;
            }

            DisplayLog.PicturesSteppedDown(_logger, stepped, measured / (1024 * 1024), _memoryCap / (1024 * 1024));

            foreach (var screen in _scenes.Keys.Where(key => key.Device == _device).ToList())
            {
                _ = Dispatcher.InvokeAsync(() => Draw(screen));
            }
        }
        catch (NotSupportedException)
        {
            // A thumbnail that does not decode leaves its picture as it was; the next relief may
            // pick another.
        }
        catch (OperationCanceledException)
        {
            // The dispatcher is going away with the application; there is nothing left to relieve.
        }
        finally
        {
            _relieving.Exit();
        }
    }

    /// <summary>
    /// Every decoded picture the memory rule may step down: not one already down, not one that
    /// moves (its frames are built from bytes), not one any background shows.
    /// </summary>
    private List<DecodedPicture> Candidates(HashSet<ItemId> held)
    {
        var mine = _scenes.Of(_device).Select(pair => pair.Value).ToList();
        var backgrounds = mine.Where(scene => scene.Background is not null).Select(scene => scene.Background!.AssetId).ToHashSet();
        var items = mine.SelectMany(scene => scene.Items.OfType<ImageItem>()).ToList();
        var pictures = new List<DecodedPicture>();

        foreach (var (asset, image) in _images)
        {
            if (image is not BitmapSource bitmap
                || _steppedDown.ContainsKey(asset)
                || _moving.ContainsKey(asset)
                || backgrounds.Contains(asset))
            {
                continue;
            }

            var showing = items.Where(item => item.AssetId == asset).ToList();

            if (showing.Count == 0)
            {
                continue;
            }

            var parked = showing.All(item => item.Parked);

            pictures.Add(new DecodedPicture(
                asset,
                (long)bitmap.PixelWidth * bitmap.PixelHeight * 4,
                parked,
                parked ? showing.Min(item => item.ZOrder) : _touchedAt.GetValueOrDefault(asset),
                showing.Any(item => held.Contains(item.ItemId))));
        }

        return pictures;
    }

    private void LetGoOfWhatNoSceneNeeds()
    {
        var wanted = SceneAssets.InUse(_scenes.Values);

        foreach (var asset in _images.Keys.Where(asset => !wanted.Contains(asset)).ToList())
        {
            _images.TryRemove(asset, out _);
            _moving.TryRemove(asset, out _);
            _steppedDown.TryRemove(asset, out _);
            _touchedAt.TryRemove(asset, out _);
            _refill.TryRemove(asset, out _);
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
    /// Writes <c>3029</c> every five minutes, for as long as the process lives.
    /// <para>
    /// Off a clock and not off the composition, so that a gap in the line means the display is gone
    /// rather than that nobody touched the table. Five minutes because the questions it answers are
    /// slow ones - an evening of play is sixty of these lines, which can be read in one go, and a
    /// climbing number is visible across them long before anybody notices anything at the table.
    /// </para>
    /// </summary>
    private async Task PulseAsync(CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(PulseEvery);

        try
        {
            while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                using var self = Process.GetCurrentProcess();

                var bytes = self.PrivateMemorySize64;

                _peakBytes = Math.Max(_peakBytes, bytes);

                var scenes = _scenes.Of(_device).ToList();

                // Worked out into locals rather than inside the call: an argument that costs
                // something must not be evaluated when the level would throw the line away (CA1873).
                // Total hours rather than a day-and-hours form: a display is meant to stay up, and
                // "27:15:00" is read at a glance where "1.03:15:00" has to be worked out.
                var standing = TimeProvider.System.GetElapsedTime(_up);
                var uptime = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(int)standing.TotalHours}:{standing.Minutes:00}:{standing.Seconds:00}");
                var items = scenes.Sum(pair => pair.Value.Items.Count);
                var screens = scenes.Count;
                var bitmaps = _images.Count;
                var memoryMb = bytes / (1024 * 1024);
                var peakMb = _peakBytes / (1024 * 1024);
                var storeMb = _cache.Bytes / (1024 * 1024);
                var files = _cache.Count;

                DisplayLog.SessionPulse(
                    _logger,
                    uptime,
                    items,
                    screens,
                    bitmaps,
                    memoryMb,
                    peakMb,
                    storeMb,
                    files,
                    _gestures,
                    _connections,
                    _touchReports,
                    _touchPoints);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
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
    /// Sends every finger on every screen, ten times a second and <b>only while there are any</b>.
    /// <para>
    /// It never touches the dispatcher, and that is deliberate: the log is written from the UI
    /// thread and drained from here, so ten reports a second can never queue behind a hand. A
    /// dispatcher hop at this rate is exactly the traffic that made a load stutter in M3b.
    /// </para>
    /// <para>
    /// One message per screen rather than per finger - four people with six fingers make one
    /// message with six entries. They travel in the transient queue, so a socket that cannot keep
    /// up drops them and nothing else: that is the whole point of rank 4 (Part 1, Part 4).
    /// </para>
    /// </summary>
    private async Task ReportTouchesAsync(CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(TouchInterval);

        while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var (screen, log) in _touching.ToArray())
            {
                // ALWAYS drained - switch off, no control, no matter. The collector forgets a
                // finger when its trail is taken, so a round that is skipped is a finger that is
                // remembered for ever; and a trail kept from minutes ago would be thrown away by
                // the 200 ms rule anyway, without anybody being able to see why.
                var fingers = log.Take(screen);

                if (fingers is null || !_reportingTouches)
                {
                    continue;
                }

                // Null between connections, which is the whole gate: there is nowhere to send.
                if (_outbox?.TrySend(fingers) is not true)
                {
                    continue;
                }

                // Counted where it actually went out, not where it was collected: a report the
                // queue refused says nothing about the table (3029, and T1 reads the ratio).
                _touchReports++;
                _touchPoints += fingers.Touches.Sum(trail => trail.Points.Count);
            }
        }
    }

    /// <summary>
    /// One reading a second, for whoever wants it: the diagnostic bar of every screen in
    /// <c>Diagnostic</c>, and the control while it asks for the stream (Part 4, Part 6). One loop
    /// for both, and that is the promise rather than a saving - <b>one source, several
    /// renderings</b>: two readers taking turns at the counter would each get half a CPU baseline,
    /// and the bar and the tile's view would disagree by exactly that.
    /// <para>
    /// For the length of the process, not of a connection: the bar is about this device and runs
    /// whether or not a control is there - it is most needed when one is not. Nothing is measured
    /// while nobody looks.
    /// </para>
    /// </summary>
    /// <summary>
    /// The foreign windows, about once a second while the control wants them and this device reports
    /// them at all (Part 4, Part 6). Our own windows - overlays, setup screen - are left out, or the
    /// tile would draw itself.
    /// </summary>
    private async Task ReportWindowsAsync(CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_windowsWanted || _foreign is ForeignWindowsMode.Off)
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(SendWindows);
            }
        }
        catch (OperationCanceledException)
        {
            // The process is going away.
        }
    }

    private void SendWindows() =>
        _outbox?.TrySend(new WindowListMessage(ForeignWindowSurvey.Survey([.. _monitors.Values], Environment.ProcessId)));

    /// <summary>
    /// A command for a foreign window (Part 6): refused where this device only reports, and on an
    /// inactive screen - the DM handed it back, somebody may be typing there; otherwise carried out,
    /// read back, and answered with what actually happened, a fresh list right behind it.
    /// </summary>
    private void Lay(WindowCommandMessage command)
    {
        DnDOverlay.Core.Rect? after = null;

        var outcome = _foreign is not ForeignWindowsMode.ReportAndControl
            ? WindowOutcome.NotAllowed
            : !_monitors.TryGetValue(command.Screen, out var monitor)
                ? WindowOutcome.Gone
                : _states.TryGetValue(command.Screen, out var state) && state.State is ScreenState.Inactive
                    ? WindowOutcome.Inactive
                    : ForeignWindowSurvey.Lay(command.Handle, command.Program, command.Action, command.Bounds, monitor, out after);

        DisplayLog.WindowLaid(_logger, command.Program, command.Action, outcome);
        _outbox?.TrySend(new WindowResultMessage(command.Handle, outcome, after));

        if (_windowsWanted)
        {
            SendWindows();
        }
    }

    private async Task MeasureAsync(CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(DiagnosticsInterval);

        try
        {
            while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var showing = await Dispatcher.InvokeAsync(
                    () => _windows.Values.Any(window => window.State == ScreenState.Diagnostic));

                if (!showing && !_diagnosing)
                {
                    continue;
                }

                // On the thread that draws: the counter is fed there, and reading it anywhere else
                // would race the frame being added.
                var frames = await Dispatcher.InvokeAsync(() => _frames?.Now());
                var (running, waiting) = _progress.Counts();

                long committed;

                using (var me = Process.GetCurrentProcess())
                {
                    committed = me.PrivateMemorySize64;
                }

                var reading = new DiagnosticsReading(
                    frames?.Frames ?? 0,
                    frames?.MedianMs,
                    frames?.P95Ms,
                    frames?.MaxMs,
                    frames?.CpuPercent ?? 0,
                    running,
                    waiting,
                    _cache.Count,
                    _cache.Bytes,
                    committed,
                    _memoryCap,
                    frames?.Stalls ?? 0);

                if (_diagnosing)
                {
                    _ = _outbox?.TrySend(new DiagnosticsMessage(reading));
                }

                if (showing)
                {
                    var lines = Readout(reading, Power.Battery());

                    await Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var (screen, window) in _windows)
                        {
                            var edge = _contexts.TryGetValue(screen, out var context)
                                ? DiagnosticBar.EdgeOf(context)
                                : ParkEdge.Left;

                            window.Diagnose(lines, edge);
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The process is going away.
        }
    }

    /// <summary>
    /// What the bar says, in three groups: the connection, the machine, and what went wrong lately
    /// (Part 6). Everything measured here - nothing of it is asked of the control.
    /// <para>
    /// The <b>stalls</b> stand beside the three frame numbers because those hide them: nine stops in
    /// thirty seconds are half a percent, so median and 95th never move and the maximum shows only
    /// the worst (hand-run of M3). The battery is a number among numbers here, with the lightning
    /// sign when on the cable - the battery shape belongs to the control (Part 7).
    /// </para>
    /// </summary>
    private List<string> Readout(DiagnosticsReading reading, BatteryInfo? battery)
    {
        var culture = CultureInfo.CurrentCulture;
        var lines = new List<string>();

        var who = $"{_deviceName} {_ownAddress ?? "?"} · {_device.Value.ToString()[..8]}";

        lines.Add(_connectedTo is { } control
            ? Strings.DiagnosticConnected(control, Volatile.Read(ref _roundTripMs) is var ms and >= 0 ? ms.ToString(culture) : "?", who)
            : Strings.DiagnosticNoControl(who));

        var frames = reading.MedianMs is { } median
            ? Strings.DiagnosticFrames(median, reading.P95Ms, reading.MaxMs, reading.Frames, reading.Stalls)
            : Strings.DiagnosticFramesTooFew;

        var power = battery is { } charge
            ? Strings.DiagnosticBattery(charge.Percent, charge.PluggedIn)
            : string.Empty;

        lines.Add(Strings.DiagnosticLoad(
            frames,
            reading.CpuPercent,
            reading.Downloading,
            reading.Waiting,
            reading.CachedPictures,
            reading.CacheBytes / (1024 * 1024),
            reading.MemoryBytes / (1024 * 1024),
            reading.MemoryCapBytes / (1024 * 1024),
            power));

        // The last three warnings and errors from the ring. Information would bury them: the
        // frame line alone comes every thirty seconds.
        if (_log is not null)
        {
            foreach (var record in _log.Ring.Recent(64).Where(entry => entry.Level >= LogLevel.Warning).TakeLast(3))
            {
                lines.Add(string.Format(culture, "{0:HH:mm} {1}", record.At, LogCatalog.Render(record, _log.Culture)));
            }
        }

        return lines;
    }

    /// <summary>
    /// Sends what is being loaded, two to four times a second and <b>only while something is</b>
    /// (Part 4). It runs for as long as the connection does: the readings travel in the progress
    /// queue, so a slow socket overwrites them rather than queueing them up.
    /// </summary>
    private async Task ReportProgressAsync(SendQueues outbox, CancellationToken cancellationToken)
    {
        using var tick = new PeriodicTimer(ProgressInterval);

        while (await tick.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_progress.Reading() is { } reading)
            {
                _ = outbox.TrySend(reading);

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
            _gestures++;

            var queue = _awaiting.GetOrAdd(reported.Transform.Item, _ => new ConcurrentQueue<(ItemTransform, long)>());

            // Bounded, because an operation the hub never answers - the item was gone, or locked -
            // would otherwise leave an entry that every later answer is measured against.
            while (queue.Count >= 16)
            {
                _ = queue.TryDequeue(out _);
            }

            queue.Enqueue((reported.Transform, Environment.TickCount64));
        }

        if (reported.Grabbed && !reported.Binding)
        {
            _grabbedAt[reported.Transform.Item] = reported.Transform;
        }

        if (reported.Grabbed)
        {
            Touched(screen, reported.Transform.Item);
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

        var message = new ItemTransformedMessage(
            screen,
            reported.Transform,
            reported.KnownRevision,
            reported.Grabbed,
            reported.Binding);

        // A grab is never held back: it is what brings the picture to the front, and a front that
        // arrives a twentieth of a second late is a picture that was under another one while
        // somebody was already moving it. A step may wait and is sent when its interval ends, from a
        // timer's thread - which the send queue takes (TransformThrottle).
        var kind = reported.Binding ? ReportKind.Final
            : reported.Grabbed ? ReportKind.Urgent
            : ReportKind.Step;

        _throttle.Report(reported.Transform.Item, kind, () => _ = outbox.TrySend(message));
    }

    /// <summary>
    /// Says what the hub made of a gesture this device let go of - once, for the answer that was
    /// actually waited for. An operation for an item nobody here moved is a stranger's and passes
    /// without a word.
    /// </summary>
    /// <summary>
    /// The hub's answer to this device's own GRAB, arriving after the hand has already let go -
    /// returned as depth only, with this device's own place kept; <see langword="null"/> for every
    /// other operation, which is applied as it comes.
    /// <para>
    /// <b>Found at a game session (18.10.2026), and it is a picture jumping back.</b> The hub answers
    /// a grab with the place the picture had when it was taken - it has to, the operation carries
    /// one - and sends that answer back to the grabbing device, because it carries the binding
    /// depth. On a quiet link it arrives while the hand is still on the picture and the hold keeps
    /// the finger's geometry. While the table was loading a 32 MB picture over Wi-Fi it arrived
    /// 1.6 s late, after the release: the picture sprang back to where it had arrived, and forward
    /// again when the answer to the release came round.
    /// </para>
    /// <para>
    /// Told apart by what it says, not by where it came from, because a patch carries no sender
    /// (Guide F9): an operation that names exactly the place this device sent with its grab, for a
    /// picture whose release is still unanswered. Anything else - the DM moving it, the hub
    /// clamping it - is a correction and wins, as conflict rule 2 says.
    /// </para>
    /// </summary>
    private TransformItem? LateAnswerToOwnGrab(TransformItem moved, SceneState scene, ScreenContext context)
    {
        if (!_grabbedAt.TryGetValue(moved.Item, out var grab) || !SamePlace(moved, grab, context))
        {
            // Somebody else's move, or the hub's own clamping: a correction, applied as it is
            // (conflict rule 2).
            return null;
        }

        _ = _grabbedAt.TryRemove(moved.Item, out _);

        // Nothing let go yet: the answer is in time, and the hold keeps the finger's geometry anyway.
        if (!_awaiting.TryGetValue(moved.Item, out var released) || released.IsEmpty
            || scene.Items.FirstOrDefault(item => item.ItemId == moved.Item) is not { } mine)
        {
            return null;
        }

        return moved with
        {
            CenterX = mine.CenterX,
            CenterY = mine.CenterY,
            Scale = mine.Scale,
            RotationDeg = mine.RotationDeg,
        };
    }

    /// <summary>
    /// Whether an answer carries the very place a report sent - within the half pixel that counts
    /// as agreement everywhere else here.
    /// </summary>
    private static bool SamePlace(TransformItem answer, ItemTransform sent, ScreenContext context)
    {
        var dx = (answer.CenterX - sent.CenterX) * context.Size.Width;
        var dy = (answer.CenterY - sent.CenterY) * context.Size.Height;

        return Math.Sqrt((dx * dx) + (dy * dy)) <= DriftWorthSaying
            && Math.Abs(answer.Scale - sent.Scale) < 0.0005
            && Math.Abs(answer.RotationDeg - sent.RotationDeg) < 0.05;
    }

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

        // Half a pixel is agreement. The values travel as doubles and come back through the same
        // arithmetic on both ends, so the ordinary case is exactly nothing - measured over an
        // evening, 120 of 123 gestures came back at 0.
        if (drift <= DriftWorthSaying)
        {
            DisplayLog.GestureConfirmed(_logger, name, spent, drift);

            return;
        }

        // In the normalised units the two ends exchange, because that is where a difference has to
        // be looked for. Three decimals is a thousandth of a screen - finer than anything that
        // could be a real disagreement, coarse enough to read.
        DisplayLog.GestureCorrected(
            _logger,
            name,
            drift,
            spent,
            Math.Round(mine.Where.CenterX, 3),
            Math.Round(mine.Where.CenterY, 3),
            Math.Round(mine.Where.Scale, 3),
            Math.Round(moved.CenterX, 3),
            Math.Round(moved.CenterY, 3),
            Math.Round(moved.Scale, 3));
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
        if (!_contexts.TryGetValue(screen, out var context))
        {
            return;
        }

        // In one change of the book, against a patch for the same screen landing on the connection
        // task at the same moment - see SceneBook.
        _scenes.Change(new ScreenRef(_device, screen), scene =>
            scene?.Items.FirstOrDefault(item => item.ItemId == transform.Item) is not { } standing
                ? null
                : SceneReducer.Apply(
                    scene,
                    new TransformItem(
                        transform.Item,
                        transform.CenterX,
                        transform.CenterY,
                        transform.Scale,
                        transform.RotationDeg,
                        toFront ? Math.Max(standing.ZOrder, scene.Top(standing.Parked) + 1) : standing.ZOrder,
                        standing.Revision),
                    context));
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
            || _steppedDown.ContainsKey(asset)
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

        _ = Task.Run(() => SharpenAsync(screen, asset, bitmap, step), _shutdown.Token);
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
    /// <param name="before">The bitmap the step was asked for - the decode replaces exactly that one, or nothing.</param>
    private async Task SharpenAsync(ScreenId screen, AssetId asset, BitmapSource before, int step)
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

            // Put in only over the bitmap it was asked for. A picture can be taken off the table
            // while its sharper version is on its way - putting it back would be memory no scene asks
            // for, the very growth LetGoOfWhatNoSceneNeeds exists to prevent - and memory pressure can
            // step it down meanwhile: the large bitmap would come back while the picture stayed
            // marked as stepped down and the books counted the saving (Part 6).
            if (!_images.TryUpdate(asset, decoded, before))
            {
                return;
            }

            var name = NameOf(asset);

            DisplayLog.PictureSharpened(_logger, name, before.PixelWidth, step);

            Relieve();

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
    /// <summary>Draws every screen of this device again - after something changed that all of them may show.</summary>
    private void DrawEverywhere()
    {
        foreach (var screen in _windows.Keys.ToList())
        {
            Draw(new ScreenRef(_device, screen));
        }
    }

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
            Loading(),
            _failed);

        _frames?.Drew(Stopwatch.GetElapsedTime(clock).TotalMilliseconds);
    }
}
