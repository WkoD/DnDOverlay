using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The smallest surface that can drive the running thread: which screens are out there, and a
/// grip that puts the demo image on one.
/// <para>
/// This is scaffolding, not the DM surface. The stage with its tiles, the campaign panel and
/// everything Part 7 describes start in M4 - what matters here is that a command travels from
/// the control to a table and something appears. What is NOT scaffolding is the window it opens:
/// <see cref="DevicesWindow"/> is the plain form of <i>Devices</i> and stays.
/// </para>
/// </summary>
internal sealed class MainWindow : Window, IDisposable
{
    private readonly ISessionApi _session;
    private readonly PairingDesk _pairing;
    private readonly DemoAsset _asset;
    private readonly Uri _address;
    private readonly CancellationTokenSource _listening = new();
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly ComboBox _state = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    private DevicesWindow? _devices;

    internal MainWindow(
        ISessionApi session,
        PairingDesk pairing,
        DemoAsset asset,
        Uri address)
    {
        _session = session;
        _pairing = pairing;
        _asset = asset;
        _address = address;

        Title = "DnDOverlay - M1b";
        Width = 620;
        Height = 520;

        var send = new Button { Content = "Send the image to the selected screen", Padding = new Thickness(12, 8, 12, 8) };
        send.Click += OnSend;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = $"Listening on {address}. Start a display with --host {address.Host}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(Devices());
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

        Closed += (_, _) => Dispose();

        _ = ListenAsync();
    }

    /// <summary>Ends the subscription with the window.</summary>
    public void Dispose()
    {
        _listening.Cancel();
        _listening.Dispose();
    }

    /// <summary>
    /// Everything shown here comes out of the session's own event stream. There is no timer: the
    /// opening picture says how things stand, and after that only changes arrive - which is what
    /// makes a display appearing on the list immediate rather than up to a second late.
    /// <para>
    /// This is the second subscriber next to <see cref="DevicesWindow"/>, and that is the property
    /// being relied on: each call gets a stream of its own, so neither takes the other's events
    /// away (Part 4).
    /// </para>
    /// </summary>
    private async Task ListenAsync()
    {
        try
        {
            await foreach (var change in _session
                .Subscribe(_listening.Token)
                .WithCancellation(_listening.Token)
                .ConfigureAwait(true))
            {
                switch (change)
                {
                    case SessionEvent.Opening opening:
                        Show(opening.Devices);
                        break;

                    case SessionEvent.DevicesChanged devices:
                        Show(devices.Devices);
                        break;

                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed.
        }
    }

    /// <summary>
    /// Flattened to a list here, because a grip that sends one image needs a target rather than a
    /// tree. The tree is what the device window is for, and it draws from the same event.
    /// </summary>
    private void Show(IReadOnlyList<DeviceView> devices)
    {
        var entries = devices
            .SelectMany(device => device.Screens)
            .Select(view => new ScreenEntry(view))
            .ToList();

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

    /// <summary>
    /// One window, reopened rather than duplicated. In M5b this moves into the menu in the panel
    /// head, together with the log panel and the settings - the one place everything that is not a
    /// grip during play is reached from (Part 7).
    /// </summary>
    private Button Devices()
    {
        var button = new Button
        {
            Content = "Devices ...",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        button.Click += (_, _) =>
        {
            if (_devices is { IsLoaded: true })
            {
                _ = _devices.Activate();
                return;
            }

            _devices = new DevicesWindow(_session, _pairing, _address) { Owner = this };
            _devices.Show();
        };

        return button;
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

            // Nothing is refreshed here afterwards: the change comes back through the stream, the
            // same way it reaches every other subscriber (rule 1).
            await _session.SetScreenStateAsync(entry.Screen, state).ConfigureAwait(true);

            _status.Text = $"{entry.Label} is now {state}.";
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        row.Children.Add(_state);
        row.Children.Add(apply);

        return row;
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
}
