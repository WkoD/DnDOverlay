using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using DnDOverlay.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// A campaign library under a temporary base path, the hub's session with the timeline in front of
/// it and one known screen, and the desk over both - what the control assembles, without a window.
/// </summary>
internal sealed class CampaignBench : IDisposable
{
    internal static readonly ScreenRef Screen = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000d1")), new ScreenId("DISPLAY1"));

    internal CampaignBench(Dispatcher dispatcher)
    {
        // Before the first picture operation, as the control's start does it (Part 5).
        CoderPolicy.Apply();

        Base = Path.Combine(Path.GetTempPath(), "DnDOverlay.Control.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Base);

        var codec = new MagickCodec();

        Library = new CampaignLibrary(Base, directory => AssetStore.Open(directory, codec, TimeProvider.System), TimeProvider.System);

        CampaignDesk? desk = null;

        Stock = new OpenStock(() => desk!.EnsureOpen());

        var screens = new ScreenCatalog();

        screens.Report(Screen.Device, [new ScreenInfo(Screen.Screen, "TISCH-PC//1", null, new PixelSize(1920, 1080), 96, true)], reported: null);

        Session = new RecordingSession(new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance));

        desk = new CampaignDesk(Library, Stock, Session, dispatcher, NullLogger.Instance);
        Desk = desk;
    }

    internal string Base { get; }

    internal CampaignLibrary Library { get; }

    internal OpenStock Stock { get; }

    internal RecordingSession Session { get; }

    internal CampaignDesk Desk { get; }

    /// <summary>A small picture of its own colour, so every seed is another asset.</summary>
    internal static byte[] Png(int seed)
    {
        var pixels = new byte[8 * 8 * 4];

        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)(seed * 37);
            pixels[index + 1] = (byte)(seed * 91);
            pixels[index + 2] = (byte)(seed * 13);
            pixels[index + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4)));

        using var stream = new MemoryStream();

        encoder.Save(stream);

        return stream.ToArray();
    }

    /// <summary>What the status line was told, in order.</summary>
    internal ConcurrentQueue<string> Said { get; } = new();

    /// <summary>The control's settings, written to a file under the base path.</summary>
    internal ControlSettings Settings => _settings ??= new ControlSettings(
        new ConfigurationFile<ControlConfiguration>(Path.Combine(Base, "control.json"), ConfigurationJsonContext.Default.ControlConfiguration, TimeProvider.System),
        new ControlConfiguration());

    /// <summary>The panel's grids and runner, as the window builds them - on the dispatcher's thread.</summary>
    internal (StockView Stock, ScenesPanel Scenes, LayoutsPanel Layouts, IntakeRunner Intake, PanelCarry Carry) Parts()
    {
        var pictures = new Pictures(Stock);
        var carry = new PanelCarry(new System.Windows.Controls.Grid());
        var edits = new StockEdits(Session, Desk, () => Session.Screens, Said.Enqueue, NullLogger.Instance);

        return (
            new StockView(Session, Desk, pictures, () => Screen, () => Session.Screens, Said.Enqueue, carry, edits),
            new ScenesPanel(Session, Desk, pictures, () => Screen, () => Session.Screens, Said.Enqueue, NullLogger.Instance, carry, Settings),
            new LayoutsPanel(Session, Desk, pictures, () => Session.Screens, Said.Enqueue, NullLogger.Instance, carry, Settings),
            new IntakeRunner(Session, new Entrances(Stock, Settings, TimeProvider.System), Said.Enqueue, NullLogger.Instance),
            carry);
    }

    private ControlSettings? _settings;

    /// <summary>A campaign folder under the base path.</summary>
    internal string Campaign(string name)
    {
        Directory.CreateDirectory(Path.Combine(Base, name));

        return name;
    }

    /// <summary>A picture taken into the open stock.</summary>
    internal async Task<AssetRef> PictureAsync(string name, int seed) =>
        await Stock.IngestAsync(Png(seed), name).ConfigureAwait(true) is IngestResult.Taken taken
            ? taken.Asset
            : throw new InvalidOperationException("The picture was not taken in.");

    public void Dispose()
    {
        Session.Dispose();
        _settings?.Flush();

        try
        {
            Directory.Delete(Base, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder; the system cleans it up if a test left a file open.
        }
    }
}

/// <summary>
/// A WPF dispatcher on a thread of its own - the UI thread the control's types expect. Held, it does
/// not run until <see cref="Release"/>: what is posted to it waits, which is how a test orders a
/// report after a switch.
/// </summary>
internal sealed class DispatcherThread : IDisposable
{
    private readonly ManualResetEventSlim _go;
    private readonly Thread _thread;

    internal DispatcherThread(bool held = false)
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        _go = new ManualResetEventSlim(!held);
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            _go.Wait();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        Dispatcher = dispatcher!;
    }

    internal Dispatcher Dispatcher { get; }

    /// <summary>Lets a held dispatcher run.</summary>
    internal void Release() => _go.Set();

    /// <summary>Runs a piece of a test on the dispatcher's thread, as the control's own code runs.</summary>
    internal Task On(Func<Task> work) => Dispatcher.InvokeAsync(work).Task.Unwrap();

    public void Dispose()
    {
        _go.Set();
        Dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
        _go.Dispose();
    }
}
