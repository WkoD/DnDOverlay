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
    private readonly ISessionApi _session;
    private readonly PairingDesk _pairing;
    private readonly DemoAsset _asset;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 120 };
    private readonly ListBox _waiting = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 60 };
    private readonly ComboBox _state = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };

    internal MainWindow(
        ISessionApi session,
        PairingDesk pairing,
        DemoAsset asset,
        Uri address)
    {
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
        panel.Children.Add(StateRow());
        panel.Children.Add(send);
        panel.Children.Add(_status);

        Content = panel;

        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Refresh();
    }

    /// <summary>
    /// The state selector, and it is not decoration: since the silent start a display puts NO
    /// window anywhere until the control says so, so without a way back out of
    /// <see cref="ScreenState.Inactive"/> a screen the DM gave back could never be taken up again
    /// (Part 3).
    /// <para>
    /// It shows the WISH, never what is currently in the way. A finding stands next to it in the
    /// list and leaves the wish untouched - which is why the selector does not jump about on its
    /// own the moment somebody moves a window (Part 3, Part 7).
    /// </para>
    /// </summary>
    private StackPanel StateRow()
    {
        foreach (var state in Enum.GetValues<ScreenState>())
        {
            _state.Items.Add(state);
        }

        var apply = new Button { Content = "Set state", Padding = new Thickness(12, 6, 12, 6) };

        apply.Click += async (_, _) =>
        {
            if (_list.SelectedItem is not ScreenEntry entry || _state.SelectedItem is not ScreenState state)
            {
                _status.Text = "Pick a screen and a state.";
                return;
            }

            await _session.SetScreenStateAsync(entry.Screen, state).ConfigureAwait(true);

            _status.Text = $"{entry.Label} is now {state}.";
            Refresh();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(_state);
        row.Children.Add(apply);

        return row;
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
        // Read through the session rather than out of the catalogue: the surface is a client of
        // its own hub, and this is the shape a tile will be drawn from (Part 1, rule 1).
        var entries = _session.Screens.Select(view => new ScreenEntry(view)).ToList();

        if (_list.Items.Cast<ScreenEntry>().SequenceEqual(entries))
        {
            return;
        }

        var selected = _list.SelectedIndex;

        _list.Items.Clear();

        foreach (var entry in entries)
        {
            _list.Items.Add(entry);
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

    /// <summary>
    /// The wish and the finding side by side, never one instead of the other. A screen that is
    /// not being played on right now still shows the state the DM set - which is the whole reason
    /// findings are not states (Part 3, Part 7).
    /// </summary>
    private sealed record ScreenEntry(ScreenView View)
    {
        internal ScreenRef Screen => View.Screen;

        internal string Label => View.Info.Label;

        public override string ToString() =>
            View.Suppressed is { } reason
                ? $"{View.Info.Label} - {View.State}, not played on: {reason}"
                : $"{View.Info.Label} - {View.State}";
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
