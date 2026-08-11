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
    private readonly PairingDesk _pairing;
    private readonly DemoAsset _asset;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 12), MinHeight = 120 };
    private readonly ListBox _waiting = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 60 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };

    internal MainWindow(
        ScreenCatalog screens,
        ISessionApi session,
        PairingDesk pairing,
        DemoAsset asset,
        Uri address)
    {
        _screens = screens;
        _session = session;
        _pairing = pairing;
        _asset = asset;

        Title = "DnDOverlay - M1b";
        Width = 620;
        Height = 560;

        var send = new Button { Content = "Send the image to the selected screen", Padding = new Thickness(12, 8, 12, 8) };
        send.Click += OnSend;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Listening on {address}. Start a display with --host {address.Host}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // Waiting devices, with the code the DM compares against what stands on the table. There
        // is no notification and no badge: pairing is done deliberately and while setting up, so
        // an alarm for something one just triggered oneself would be noise (Part 7).
        panel.Children.Add(new TextBlock { Text = "Waiting for a decision", FontWeight = FontWeights.Bold });
        panel.Children.Add(_waiting);
        panel.Children.Add(PairingButtons());
        panel.Children.Add(new TextBlock
        {
            Text = "Screens",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4),
        });
        panel.Children.Add(_list);
        panel.Children.Add(send);
        panel.Children.Add(_status);

        Content = panel;

        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Refresh();
    }

    private StackPanel PairingButtons()
    {
        var allow = new Button { Content = "Allow", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var deny = new Button { Content = "Reject", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var own = new Button { Content = "Take on as its own device", Padding = new Thickness(12, 6, 12, 6) };

        allow.Click += (_, _) => DecideAsync(request => _pairing.AllowAsync(request));
        deny.Click += (_, _) => DecideAsync(request => _pairing.DenyAsync(request));
        own.Click += (_, _) => DecideAsync(request => _pairing.AcceptAsOwnAsync(request));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(allow);
        row.Children.Add(deny);
        row.Children.Add(own);

        return row;
    }

    private async void DecideAsync(Func<PendingPairing, Task> decision)
    {
        if (_waiting.SelectedItem is not PendingEntry entry)
        {
            _status.Text = "Nothing is waiting.";
            return;
        }

        await decision(entry.Request).ConfigureAwait(true);

        _status.Text = $"Decided about {entry.Request.Name}.";
        Refresh();
    }

    private void Refresh()
    {
        RefreshScreens();
        RefreshWaiting();
    }

    /// <summary>
    /// What is knocking right now. The list is rebuilt from the hub rather than accumulated here:
    /// a request has no deadline, it has a connection - and it vanishes with it (Part 4).
    /// </summary>
    private void RefreshWaiting()
    {
        var pending = _session.PendingPairings;

        if (_waiting.Items.Count == pending.Count)
        {
            return;
        }

        var selected = _waiting.SelectedIndex;

        _waiting.Items.Clear();

        foreach (var request in pending)
        {
            _waiting.Items.Add(new PendingEntry(request));
        }

        _waiting.SelectedIndex = selected >= 0 && selected < _waiting.Items.Count ? selected : 0;
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

    /// <summary>
    /// Name, address and code - the three the DM holds against what stands large on the display
    /// (Part 4). A clone says so, because there the right grip is a different one.
    /// </summary>
    private sealed record PendingEntry(PendingPairing Request)
    {
        public override string ToString() =>
            Request.IsClone
                ? $"{Request.Name} at {Request.Address} - same DeviceId as a device that is already connected"
                : $"{Request.Name} at {Request.Address}, code {Request.PairingCode}";
    }
}
