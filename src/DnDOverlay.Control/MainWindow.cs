using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The smallest surface that can drive the running thread: which screens are out there, and a
/// grip that puts the demo image on one.
/// <para>
/// This is scaffolding, not the DM surface. The stage with its tiles, the campaign panel and
/// everything Part 7 describes start in M4 - what matters here is that a command travels from
/// the control to a table and something appears.
/// </para>
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly ScreenCatalog _screens;
    private readonly ISessionApi _session;
    private readonly DemoAsset _asset;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 12), MinHeight = 160 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };

    internal MainWindow(ScreenCatalog screens, ISessionApi session, DemoAsset asset, Uri address)
    {
        _screens = screens;
        _session = session;
        _asset = asset;

        Title = "DnDOverlay - M1a";
        Width = 560;
        Height = 420;

        var send = new Button { Content = "Send the image to the selected screen", Padding = new Thickness(12, 8, 12, 8) };
        send.Click += OnSend;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Listening on {address}. Start a display with --host {address.Host}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(_list);
        panel.Children.Add(send);
        panel.Children.Add(_status);

        Content = panel;

        _refresh.Tick += (_, _) => RefreshScreens();
        _refresh.Start();
        RefreshScreens();
    }

    /// <summary>
    /// Polling once a second, deliberately. The event stream a real surface subscribes to is
    /// <c>ISessionApi.Subscribe</c>, and it arrives in M1b together with its opening picture -
    /// building half of it here would be something to throw away a milestone later.
    /// </summary>
    private void RefreshScreens()
    {
        var known = _screens.Known.ToList();

        if (_list.Items.Count == known.Count)
        {
            return;
        }

        var selected = _list.SelectedIndex;

        _list.Items.Clear();

        foreach (var screen in known)
        {
            var info = _screens.InfoFor(screen);
            _list.Items.Add(new ScreenEntry(screen, info?.Label ?? screen.Screen.Value));
        }

        _list.SelectedIndex = selected >= 0 && selected < _list.Items.Count ? selected : 0;
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is not ScreenEntry entry)
        {
            _status.Text = "No screen yet - start a display and wait a moment.";
            return;
        }

        // No drop point aimed at, so the hub decides where it goes: placement is its business,
        // because it reads the state and writes it in the same breath (Part 3).
        var item = await _session.AddItemAsync(entry.Screen, _asset.Reference, position: null).ConfigureAwait(true);

        _status.Text = $"Sent as item {item} to {entry.Label}.";
    }

    private sealed record ScreenEntry(ScreenRef Screen, string Label)
    {
        public override string ToString() => Label;
    }
}
