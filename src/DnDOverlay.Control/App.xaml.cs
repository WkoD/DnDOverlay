using System.IO;
using System.Windows;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Logging;
using DnDOverlay.Hub;
using DnDOverlay.Imaging;
using DnDOverlay.Platform.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// Entry point of the DM application. It hosts the hub in its own process - the hub is a
/// library, not a service, and there is nothing separate to install (Part 9).
/// <para>
/// The host is a <see cref="WebApplication"/>, which IS the generic host with DI plus the
/// Kestrel the hub needs. Nesting a second host inside the WPF one would buy nothing and cost a
/// lifetime to reason about.
/// </para>
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private WebApplication? _host;
    private ConfigurationFile<ControlConfiguration>? _configuration;
    private ProcessLog? _log;
    private AssetStore? _store;
    private ScreenCatalog? _screens;
    private ControlSettings? _settings;
    private DataRoot _dataRoot;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var options = ControlOptions.Parse(e.Args);

        // Handed in, never fetched from inside a library (rule 10). Every further place -
        // control.json, the campaign folder, the logs - is derived from this one value, and
        // --data moves all of them at once so a development run never touches the installed
        // copy on the same machine (Part 9).
        _dataRoot = WindowsDataRoot.Resolve(options.DataRoot);

        // The clock is handed in as well (rule 10). It is the second half of the same rule, and
        // M1b is where the first timestamps appear - the debounce here, the file log and the
        // device time next. A single DateTime.Now that settles in now would make two acceptance
        // steps unautomatable later (Part 10, Part 11).
        _configuration = new ConfigurationFile<ControlConfiguration>(
            _dataRoot.ControlConfiguration,
            ConfigurationJsonContext.Default.ControlConfiguration,
            TimeProvider.System);

        var loaded = _configuration.Load(() => new ControlConfiguration());

        // A replaced file must not cost this control its identity, or its own displays would treat
        // it as a stranger and never knock again (Part 4, Part 6). Recovered here rather than
        // inside Load, because only THIS document has an identity worth keeping - display.json
        // loses a DeviceId, and that is healed by "reassign device" instead.
        var recovered = false;

        if (loaded.Outcome is ConfigurationOutcome.Replaced
            && ControlIdentity.TryRecover(loaded.SetAside, out var identity))
        {
            loaded = loaded with { Value = loaded.Value with { ControlId = identity } };
            recovered = true;
        }

        // One owner for the file, several callers: pairing, the screen inventory and later the
        // view state all change it, and separate copies would silently overwrite one another.
        _settings = new ControlSettings(_configuration, loaded.Value);

        // Written back at once when it did not exist: the ControlId has to survive the first
        // restart, or every display would find a stranger where its control used to be (Part 4).
        if (loaded.Outcome is not ConfigurationOutcome.Loaded)
        {
            _settings.Update(configuration => configuration);
            _settings.Flush();
        }

        // DPAPI, behind its interface. It is created here rather than resolved from the hub's
        // container because the hub must not know it exists: pairing is hub business, the hub
        // builds for net10.0, and ProtectedData is Windows only (Part 4).
        var secrets = new WindowsSecretStore();
        var restored = PairingDesk.Restore(loaded.Value, secrets);

        // BEFORE the first picture operation, and that is the whole point of the call: applied
        // late the policy silently does nothing at all, and the hardening that separates a URL
        // import from a remote control is simply absent (Part 5). It proves its own effect.
        CoderPolicy.Apply();

        // The real stock replaces the hard-coded stand-in from M1a (checks/M2.md, decision 5).
        // One unnamed campaign is enough for M2 - naming, switching and the campaign panel are
        // M5b (Part 10).
        var store = AssetStore.Open(
            Path.Combine(_dataRoot.CampaignsDefault, "unbenannt"),
            new MagickCodec(),
            TimeProvider.System,

            // The unpacker is handed in here and nowhere else. Without it a .rptok would be an
            // unreadable file, and the shortest way to a full stock - the monsters the DM has had
            // as tokens for years - would not exist (Part 5).
            containers: new TokenContainer());

        _store = store;

        var builder = WebApplication.CreateBuilder();

        // One provider per process, registered unconditionally and never taken out again - which
        // is not a matter of taste: ILoggerFactory has AddProvider and no counterpart, so a level
        // that changes while the process runs has to live INSIDE the provider (Part 6, Part 8).
        _log = new ProcessLog(
            LogIdentity.Of(typeof(App).Assembly, Core.Protocol.Protocol.Version),
            _dataRoot.Logs,
            LogFileLimits.Control,
            TimeProvider.System);

        // Set before the first line is written, because the first lines are the ones that explain
        // a start that went wrong. It is the same wiring the display has for its own level - and
        // without it the control would be the only process that cannot be turned up at all.
        _log.Level = loaded.Value.LogLevel;

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_log);
        builder.Logging.AddDebug();
        builder.Logging.AddSimpleConsole();

        // Trace at the factory so that OUR level is the effective gate and can be moved at run
        // time; the factory's own filters are fixed when it is built.
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        // The control hosts Kestrel, so the framework's own messages arrive in the same provider.
        // At Information that is request noise in the DM's file; what is worth having is what goes
        // wrong - a taken port, a socket that will not bind.
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

        // Kestrel listens on ALL interfaces. The selection is made by the firewall rule anyway,
        // and binding to "the right" address breaks the moment the Surface goes into its dock
        // (Part 4).
        builder.WebHost.UseUrls($"http://0.0.0.0:{loaded.Value.Port}");

        // ControlId and port are START values, not part of any later snapshot: they stand
        // before the first line of state exists (Part 7).
        builder.Services.AddDnDOverlayHub(hub =>
        {
            hub.ControlId = loaded.Value.ControlId;
            hub.Port = loaded.Value.Port;

            // A snapshot, taken once. The hub never learns where control.json is; what it gets
            // are the values that belong to it, and what changes later comes in through
            // ApprovePairingAsync (Part 7).
            hub.KnownDevices = restored.Devices;

            // The five wishes and the display parameters of every screen ever reported. Without
            // them a table the DM had given back would come up Enabled after a restart of the
            // control, and a scene could not be prepared for a switched-off device at all
            // (Part 3).
            hub.KnownScreens = loaded.Value.KnownScreens;
        });

        builder.Services.AddSingleton<IAssetSource>(store);

        // Registered so the hub can put FORWARDED entries into the same log as our own - one
        // stream out of everybody's lines (Part 8). The hub asks for it optionally and runs
        // without one, which is what keeps it a library rather than a part of this application.
        builder.Services.AddSingleton(_log);

        _host = builder.Build();

        _host.UseWebSockets();
        _host.MapDnDOverlayHub();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();

        // As early as there is somewhere to write to. Everything after this point can fail loudly;
        // before it, a fault is the silence that cost us a diagnosis once already.
        LastWords.Listen(this, logger);

        // Said BEFORE the port is taken, not after. A start that dies at the socket still has to
        // have recorded where it looked and what it found there - otherwise the one run whose log
        // matters most is the one that wrote nothing.
        ControlLog.DataRootChosen(logger, _dataRoot.Path);
        Report(logger, loaded, recovered);

        if (!await ListeningAsync(logger, loaded.Value.Port).ConfigureAwait(true))
        {
            return;
        }

        ControlLog.KnownDevicesRestored(logger, restored.Devices.Count, restored.Dropped);

        var session = _host.Services.GetRequiredService<ISessionApi>();
        var screens = _host.Services.GetRequiredService<ScreenCatalog>();

        // Written when the catalogue says something changed, never on a timer. Polling would be
        // the obvious alternative and is quietly broken: the configuration file debounces, so a
        // save on every tick would push its own deadline out for ever and write nothing at all.
        screens.Changed += Persist;
        _screens = screens;

        var window = new MainWindow(
            session,
            new PairingDesk(session, secrets, _settings, TimeProvider.System),
            new Entrances(store, _settings, TimeProvider.System),
            store,
            _settings,
            new Uri($"http://{Environment.MachineName}:{loaded.Value.Port}/"),
            _log);

        MainWindow = window;
        window.Show();

        // Beside the stage, once the stage exists. The log line above is for later; this is for
        // now - a replacement costs every pairing, and a DM who is not told simply finds his
        // tables gone and looks for the fault in the network (Part 6, Part 7).
        if (loaded.Outcome is ConfigurationOutcome.Replaced)
        {
            // Two quite different messages, and the difference is a walk through the flat.
            window.Notify(recovered
                ? $"control.json was unreadable and was set aside as {loaded.SetAside ?? "(not kept)"}. "
                    + "The identity of this control was recovered from it, so the paired displays find "
                    + "it again by themselves - they arrive as pairing requests, because their tokens "
                    + "went with the file. Allow them here; nothing has to be done at the devices."
                // No mention of calling for orphaned devices: that grip is M5a, and a sentence that
                // points at a function which does not exist leaves the reader worse off than one
                // that names the only thing they can actually do (Part 8).
                : $"control.json was unreadable and was set aside as {loaded.SetAside ?? "(not kept)"}. "
                    + "This control started with defaults - and with a new identity, so paired displays "
                    + "discard its announcements and will not appear here by themselves. The pairing "
                    + "has to be reset at each device.");
        }
    }

    /// <summary>
    /// Starts listening, or says why it cannot.
    /// <para>
    /// A taken port is the one startup fault that is worth a dialog: nothing of this application
    /// works without its hub, so there is no surface left to report into - and the DM would
    /// otherwise be left with a program that flashes and disappears. It names the number and what
    /// to do about it, because "the port is in use" alone leaves the reader exactly where they were
    /// (Part 4).
    /// </para>
    /// </summary>
    private async Task<bool> ListeningAsync(ILogger logger, int port)
    {
        try
        {
            await _host!.StartAsync().ConfigureAwait(true);

            return true;
        }
        catch (IOException exception)
        {
            // What Kestrel throws when the address is already taken - the SocketException is the
            // inner one, and catching the outer keeps this from depending on which of the two
            // layers reports first.
            ControlLog.PortTaken(logger, exception, port, _dataRoot.ControlConfiguration);

            _ = MessageBox.Show(
                $"Port {port} is already in use.\n\n"
                + "Another DnDOverlay control is probably running already - look for its window "
                + $"before starting a second one. Otherwise change \"Port\" in {_dataRoot.ControlConfiguration} "
                + "and start again; the displays find the new one by themselves.",
                "DnDOverlay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();

            return false;
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Anything outstanding goes to disk before the process does. SessionEnding gets the
        // same treatment in M6, when the tray and the close dialog arrive (Part 6).
        Dispose();

        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(true);
            await _host.DisposeAsync().ConfigureAwait(true);
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Writes the screen inventory back. Only the WISH goes in, never a finding: what stands in
    /// control.json has to be what the DM asked for, or a screen unplugged during a change would
    /// come back with the wrong value (Part 3).
    /// </summary>
    private void Persist()
    {
        if (_settings is null || _screens is null)
        {
            return;
        }

        _settings.Update(configuration => configuration with { KnownScreens = _screens.Snapshot() });
    }

    public void Dispose()
    {
        if (_screens is not null)
        {
            _screens.Changed -= Persist;
        }

        _configuration?.Dispose();

        // Nothing is buffered, so this closes a handle rather than saving anything: crash safety
        // is a property of writing through, not of a shutdown hook (Part 8).
        _log?.Dispose();
    }

    /// <summary>
    /// Says what reading the file did. A replaced configuration must be visible, not quiet: the
    /// control keeps known devices and their tokens in it, so a replacement means every display
    /// has to be paired again - and being told that at the table is the difference between a
    /// puzzle and a task (Part 6).
    /// </summary>
    private static void Report(ILogger logger, ConfigurationLoad<ControlConfiguration> loaded, bool recovered)
    {
        switch (loaded.Outcome)
        {
            case ConfigurationOutcome.Created:
                ControlLog.ConfigurationCreated(logger, loaded.Value.ControlId);
                break;

            case ConfigurationOutcome.Replaced when recovered:
                ControlLog.IdentityRecovered(logger, loaded.SetAside ?? "(not kept)", loaded.Value.ControlId);
                break;

            case ConfigurationOutcome.Replaced:
                ControlLog.ConfigurationReplaced(logger, loaded.SetAside ?? "(not kept)");
                break;

            case ConfigurationOutcome.Loaded:
            default:
                break;
        }
    }
}
