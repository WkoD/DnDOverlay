using System.Windows;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
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

        // Written back at once when it did not exist: the ControlId has to survive the first
        // restart, or every display would find a stranger where its control used to be (Part 4).
        if (loaded.Outcome is not ConfigurationOutcome.Loaded)
        {
            _configuration.Save(loaded.Value);
            _configuration.Flush();
        }

        // DPAPI, behind its interface. It is created here rather than resolved from the hub's
        // container because the hub must not know it exists: pairing is hub business, the hub
        // builds for net10.0, and ProtectedData is Windows only (Part 4).
        var secrets = new WindowsSecretStore();
        var restored = PairingDesk.Restore(loaded.Value, secrets);

        var asset = DemoAsset.Create();
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

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
        });

        builder.Services.AddSingleton<IAssetSource>(asset);

        _host = builder.Build();

        _host.UseWebSockets();
        _host.MapDnDOverlayHub();

        await _host.StartAsync().ConfigureAwait(true);

        var logger = _host.Services.GetRequiredService<ILogger<App>>();

        ControlLog.DataRootChosen(logger, _dataRoot.Path);
        Report(logger, loaded);
        ControlLog.KnownDevicesRestored(logger, restored.Devices.Count, restored.Dropped);

        var session = _host.Services.GetRequiredService<ISessionApi>();

        var window = new MainWindow(
            _host.Services.GetRequiredService<ScreenCatalog>(),
            session,
            new PairingDesk(session, secrets, _configuration, loaded.Value, TimeProvider.System),
            asset,
            new Uri($"http://{Environment.MachineName}:{loaded.Value.Port}/"));

        MainWindow = window;
        window.Show();
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

    public void Dispose() => _configuration?.Dispose();

    /// <summary>
    /// Says what reading the file did. A replaced configuration must be visible, not quiet: the
    /// control keeps known devices and their tokens in it, so a replacement means every display
    /// has to be paired again - and being told that at the table is the difference between a
    /// puzzle and a task (Part 6).
    /// </summary>
    private static void Report(ILogger logger, ConfigurationLoad<ControlConfiguration> loaded)
    {
        switch (loaded.Outcome)
        {
            case ConfigurationOutcome.Created:
                ControlLog.ConfigurationCreated(logger, loaded.Value.ControlId);
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
