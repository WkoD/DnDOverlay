using System.Windows;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;
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
public sealed partial class App : Application
{
    private WebApplication? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var asset = DemoAsset.Create();
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // Kestrel listens on ALL interfaces. The selection is made by the firewall rule anyway,
        // and binding to "the right" address breaks the moment the Surface goes into its dock
        // (Part 4).
        builder.WebHost.UseUrls($"http://0.0.0.0:{Protocol.DefaultPort}");

        builder.Services.AddDnDOverlayHub();
        builder.Services.AddSingleton<IAssetSource>(asset);

        _host = builder.Build();

        _host.UseWebSockets();
        _host.MapDnDOverlayHub();

        await _host.StartAsync().ConfigureAwait(true);

        var window = new MainWindow(
            _host.Services.GetRequiredService<ScreenCatalog>(),
            _host.Services.GetRequiredService<ISessionApi>(),
            asset,
            new Uri($"http://{Environment.MachineName}:{Protocol.DefaultPort}/"));

        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(true);
            await _host.DisposeAsync().ConfigureAwait(true);
        }

        base.OnExit(e);
    }
}
