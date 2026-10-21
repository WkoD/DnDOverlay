using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The window <i>Devices</i>, in its plain form: the list of everything known, what is knocking,
/// and what was turned away.
/// <para>
/// It passes the test Part 7 sets for a window - one could leave it shut for a whole evening
/// without missing anything. What is ONLY in here has no tile: waiting and rejected devices,
/// refused limit violations, our own address, the version of each device. That is setting up and
/// fault-finding, not a grip during play.
/// </para>
/// <para>
/// <b>The list on the left, the two bands on the right</b> (Part 7) - everything about a device,
/// everything about one of its screens, out of the shared parameter description (<see cref="DeviceBands"/>).
/// On a narrow window they lie one beneath the other: nothing disappears, it only wraps.
/// </para>
/// </summary>
internal sealed class DevicesWindow : Window, IDisposable
{
    private readonly ISessionApi _session;
    private readonly PairingDesk _pairing;
    private readonly CancellationTokenSource _listening = new();
    private readonly TreeView _tree = new() { MinHeight = 160, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox _waiting = new() { MinHeight = 60, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _refused = new() { MinHeight = 60, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _bandsHost = new() { Margin = new Thickness(16) };
    private readonly ScrollViewer _list = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScrollViewer _right = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Grid _panes = new();
    private readonly DeviceBands _bands;

    /// <summary>The devices as last reported - the bands are drawn from it.</summary>
    private IReadOnlyList<DeviceView> _devices = [];

    /// <summary>What is chosen in the list, kept across the list being rebuilt on every device event.</summary>
    private (DeviceId Device, ScreenRef? Screen)? _picked;

    /// <summary>
    /// Where each screen sits in the tree, so that "set up screen ..." can arrive at one. The tree
    /// is rebuilt on every device event, so this is refilled with it rather than kept.
    /// </summary>
    private readonly Dictionary<ScreenRef, TreeViewItem> _rows = [];

    /// <summary>
    /// The screen the window was opened for, kept until it can be shown. It arrives from a menu
    /// before the first device list has come in, and selecting nothing then would be the whole
    /// point of the entry lost.
    /// </summary>
    private ScreenRef? _wanted;

    internal DevicesWindow(ISessionApi session, PairingDesk pairing, CampaignDesk desk, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _session = session;
        _pairing = pairing;
        _bands = new DeviceBands(session, desk, said => _status.Text = said, this);
        _bands.Changed += (_, _) => ShowBands(force: true);
        _bands.ScenesSaved += (_, _) => ScenesSaved?.Invoke(this, EventArgs.Empty);

        Title = "Devices";
        Width = 1180;
        Height = 760;

        var panel = new StackPanel { Margin = new Thickness(16) };

        // Waiting first, and at the top: it is the only thing in here that somebody is standing at
        // a table waiting for. There is no badge and no notification for it - pairing is done
        // deliberately and while setting up, so an alarm for something one just triggered oneself
        // would be noise (Part 7).
        panel.Children.Add(Heading("Waiting for a decision", first: true));
        panel.Children.Add(_waiting);
        panel.Children.Add(Decisions());

        panel.Children.Add(Heading("Devices"));
        panel.Children.Add(_tree);

        panel.Children.Add(Heading("Turned away"));
        panel.Children.Add(_refused);
        panel.Children.Add(TakeBack());

        panel.Children.Add(Heading("This control"));
        panel.Children.Add(new TextBlock
        {
            Text = $"Reachable at {address}. Type that at a display when discovery does not get through.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(Gate());
        panel.Children.Add(Orphans());
        panel.Children.Add(_status);

        _list.Content = panel;
        _right.Content = _bandsHost;
        _panes.Children.Add(_list);
        _panes.Children.Add(_right);
        Content = _panes;

        Lay(wide: true);
        SizeChanged += (_, _) => Lay(ActualWidth >= 1000);

        _tree.SelectedItemChanged += (_, _) =>
        {
            if ((_tree.SelectedItem as TreeViewItem)?.Tag is Picked picked)
            {
                _picked = (picked.Device.Device, picked.Screen?.Screen);
                ShowBands(force: true);
            }
        };

        ShowBands(force: true);

        Closed += (_, _) => Dispose();

        _ = ListenAsync();
    }

    /// <summary>
    /// Ends the subscription with the window. Nothing else does - a stream nobody reads would fill
    /// its channel and be cut off eventually, which is the right answer for a subscriber that has
    /// stopped listening and the wrong one to arrive at by accident.
    /// </summary>
    /// <summary>Scenes were saved before an unpair - the tab Scenes has new entries.</summary>
    internal event EventHandler? ScenesSaved;

    public void Dispose()
    {
        _listening.Cancel();
        _listening.Dispose();
    }

    /// <summary>
    /// Reads the session's own event stream rather than asking every second. The opening picture
    /// arrives first and completely, so a device that connected and knocked BEFORE this window
    /// existed is in the list the moment it opens - which is the ordinary case, because the hub
    /// listens before any surface stands (rule 5, Part 4).
    /// </summary>
    private async Task ListenAsync()
    {
        try
        {
            // No ConfigureAwait(false) anywhere on this path: the continuation is meant to come
            // back to the dispatcher, because what follows touches controls.
            await foreach (var change in _session
                .Subscribe(_listening.Token)
                .WithCancellation(_listening.Token)
                .ConfigureAwait(true))
            {
                Apply(change);
            }

            // Reached when the hub cut this subscriber off for falling behind. Saying so is the
            // point: the list is frozen from here, and the way back is to open the window again
            // (Part 4).
            if (!_listening.IsCancellationRequested)
            {
                _status.Text = "The event stream ended - close and reopen this window for a fresh picture.";
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed.
        }
    }

    private void Apply(SessionEvent change)
    {
        switch (change)
        {
            case SessionEvent.Opening opening:
                Show(opening.Devices);
                Show(opening.Pending, opening.Refused);
                break;

            case SessionEvent.DevicesChanged devices:
                Show(devices.Devices);
                break;

            case SessionEvent.PairingChanged pairing:
                Show(pairing.Pending, pairing.Refused);
                break;

            default:
                // Scenes and log lines belong to the stage and the log panel. An event this window
                // has no use for is passed over, never treated as an error (rule 7).
                break;
        }
    }

    /// <summary>
    /// The two-stage tree: device, and its screens underneath. Flat would be unreadable with two
    /// devices of two monitors each, and it would not say which monitor hangs off which machine
    /// (Part 7).
    /// </summary>
    private void Show(IReadOnlyList<DeviceView> devices)
    {
        _devices = devices;
        _tree.Items.Clear();
        _rows.Clear();

        foreach (var device in devices)
        {
            // The device rides along on both levels, so selecting a screen answers "which device"
            // as well as selecting the device does - the grips below act on the machine.
            // The battery beside the device's line, the same gauge as on its tiles (Part 7). Only
            // while it is connected - a percentage from before it left would read as current.
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var gauge = new BatteryGauge { Margin = new Thickness(8, 0, 0, 0) };

            header.Children.Add(new TextBlock { Text = Describe(device), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(gauge);
            gauge.Show(device.Connected ? device.Battery : null);

            var node = new TreeViewItem
            {
                Header = header,
                IsExpanded = true,
                Tag = new Picked(device, null),
            };

            if (_picked is { Screen: null } wantedDevice && wantedDevice.Device == device.Device)
            {
                node.IsSelected = true;
            }

            foreach (var screen in device.Screens)
            {
                var row = new TreeViewItem { Header = Describe(screen), Tag = new Picked(device, screen) };

                if (_picked is { Screen: { } wantedScreen } && wantedScreen == screen.Screen)
                {
                    row.IsSelected = true;
                }

                _rows[screen.Screen] = row;
                node.Items.Add(row);
            }

            _tree.Items.Add(node);
        }

        if (devices.Count == 0)
        {
            _tree.Items.Add(new TreeViewItem { Header = "No device paired yet." });
        }

        Reach();
        ShowBands(force: false);
    }

    /// <summary>
    /// Draws the bands for what is chosen. Not while a value in them is being typed, unless asked to:
    /// a device event arrives every few seconds, and a band redrawn under the cursor would eat the
    /// number half typed.
    /// </summary>
    private void ShowBands(bool force)
    {
        if (!force && _bandsHost.IsKeyboardFocusWithin)
        {
            return;
        }

        var device = _picked is { } picked ? _devices.FirstOrDefault(known => known.Device == picked.Device) : null;

        if (device is null)
        {
            _bandsHost.Children.Clear();
            _bandsHost.Children.Add(new TextBlock
            {
                Text = "Choose a device or one of its screens on the left - its settings stand here.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });

            return;
        }

        var screen = _picked?.Screen is { } wanted ? device.Screens.FirstOrDefault(known => known.Screen == wanted) : null;

        _bands.Show(_bandsHost, device, screen, _devices);
    }

    /// <summary>Side by side on a wide window, one beneath the other on a narrow one (Part 7).</summary>
    private void Lay(bool wide)
    {
        _panes.ColumnDefinitions.Clear();
        _panes.RowDefinitions.Clear();

        if (wide)
        {
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            Grid.SetColumn(_right, 1);
            Grid.SetRow(_right, 0);
        }
        else
        {
            _panes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _panes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_right, 0);
            Grid.SetRow(_right, 1);
        }
    }

    /// <summary>
    /// Opens this window on one screen - what the tile menu's "set up screen ..." promises
    /// (Part 7). <b>The window is the one that exists</b>; its two bands of parameters are M5b, and
    /// pointing an entry at half a window is still pointing it somewhere.
    /// </summary>
    internal void Reveal(ScreenRef screen)
    {
        _wanted = screen;

        Reach();
    }

    /// <summary>
    /// Selects the screen the window was opened for, once there is a row for it. Tried again after
    /// every rebuild, because the first device list may arrive after the menu did.
    /// </summary>
    private void Reach()
    {
        if (_wanted is { } wanted && _rows.TryGetValue(wanted, out var row))
        {
            row.IsSelected = true;
            row.BringIntoView();

            _wanted = null;
        }
    }

    /// <summary>
    /// A device that is not connected stays here with its screens, and it says so instead of
    /// vanishing: its wishes and parameters live in the control, and setting them before the
    /// display PC is switched on is what this window is for (Part 3, Part 7).
    /// </summary>
    private static string Describe(DeviceView device)
    {
        if (!device.Connected)
        {
            return $"{device.Name} - not connected";
        }

        var latency = device.RoundTrip is { } trip ? $"{trip.TotalMilliseconds:F0} ms" : "measuring";

        return $"{device.Name} - {device.Address}, {latency}, "
            + $"version {device.AppVersion} (protocol {device.ProtocolVersion})";
    }

    /// <summary>
    /// Wish and finding side by side, never one instead of the other - and the resolution, which is
    /// the question during play: how much room have I got there? It comes out of what was kept, so
    /// a screen whose device is switched off shows it too (Part 3, Part 7).
    /// </summary>
    private static string Describe(ScreenView screen)
    {
        var size = $"{screen.Info.Size.Width}x{screen.Info.Size.Height}";

        return screen.Suppressed is { } reason
            ? $"{screen.Info.Label} - {size} - {screen.State}, not played on: {reason}"
            : $"{screen.Info.Label} - {size} - {screen.State}";
    }

    private void Show(IReadOnlyList<PendingPairing> pending, IReadOnlyList<RefusedDevice> refused)
    {
        Refill(_waiting, [.. pending.Select(request => new PendingEntry(request))]);
        Refill(_refused, [.. refused.Select(device => new RefusedEntry(device))]);
    }

    /// <summary>Keeps the selection where it was, so a list that redraws does not move under a finger.</summary>
    private static void Refill(ListBox list, IReadOnlyList<object> entries)
    {
        var selected = list.SelectedIndex;

        list.Items.Clear();

        foreach (var entry in entries)
        {
            list.Items.Add(entry);
        }

        list.SelectedIndex = selected >= 0 && selected < list.Items.Count ? selected : 0;
    }

    /// <summary>
    /// The decisions, in the row itself rather than behind a dialog of their own - one dialog too
    /// many (Part 7). Assigning to a KNOWN device is the fourth and arrives in M5b, together with
    /// the screen mask it shares with "reassign screen".
    /// </summary>
    private StackPanel Decisions()
    {
        var allow = new Button { Content = "Allow", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var deny = new Button { Content = "Reject", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var own = new Button { Content = "Take on as its own device", Padding = new Thickness(12, 6, 12, 6) };

        allow.Click += (_, _) => DecideAsync(request => _pairing.AllowAsync(request));
        deny.Click += (_, _) => DecideAsync(request => _pairing.DenyAsync(request));
        own.Click += (_, _) => DecideAsync(request => _pairing.AcceptAsOwnAsync(request));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

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
    }

    /// <summary>
    /// "Which one are you?" - every overlay of the selected device shows its own name for a few
    /// seconds (Part 6). With two devices of two screens each it is the only answer there is: the
    /// names are the DM's own, and the identifiers behind them appear in no surface (Part 3).
    /// <para>
    /// In M4 this moves into the tile's screen context menu, where the DM already is. Here it sits
    /// under the tree because there are no tiles yet.
    /// </para>
    /// </summary>
    /// <summary>
    /// The only way out of "rejected" the DM walks himself. Without it a mistaken no could only be
    /// healed at the device, with "reset pairing" on a machine that has no keyboard (Part 4).
    /// </summary>
    private Button TakeBack()
    {
        var button = new Button
        {
            Content = "Take the rejection back",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        button.Click += async (_, _) =>
        {
            if (_refused.SelectedItem is not RefusedEntry entry)
            {
                _status.Text = "Nothing has been turned away.";
                return;
            }

            await _session.ClearRejectionAsync(entry.Device.Device).ConfigureAwait(true);

            // Said out loud, or the grip looks as if it did nothing: the device only tries again
            // after a deliberate pause (Part 4).
            _status.Text = $"{entry.Device.Name} may knock again - it can take up to five minutes.";
        };

        return button;
    }

    /// <summary>
    /// It acts on exactly what this window shows, and it is reached for in the moment the window is
    /// open anyway - when a strange device keeps knocking (Part 7).
    /// </summary>
    private CheckBox Gate()
    {
        var box = new CheckBox
        {
            Content = "Accept new devices",
            IsChecked = _session.AcceptNewDevices,
            Margin = new Thickness(0, 0, 0, 8),
        };

        box.Checked += (_, _) => _session.AcceptNewDevices = true;
        box.Unchecked += (_, _) =>
        {
            _session.AcceptNewDevices = false;
            _status.Text = "Requests are only written to the log from now on.";
        };

        return box;
    }

    /// <summary>
    /// The call for orphaned displays, for a control whose identity is gone: two minutes in the
    /// beacon, and displays that lost their control without a goodbye more than a minute ago
    /// answer and stand above as requests (Part 4). Pressing again starts the two minutes anew.
    /// </summary>
    private Button Orphans()
    {
        var button = new Button
        {
            Content = "Search for orphaned devices",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };

        button.Click += async (_, _) =>
        {
            await _session.CallOrphansAsync(_listening.Token).ConfigureAwait(true);
            _status.Text = "Calling for two minutes. Displays that lost their control more than a minute ago "
                           + "appear under Waiting for a decision.";
        };

        return button;
    }

    private static TextBlock Heading(string text, bool first = false) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, first ? 0 : 12, 0, 4),
        };

    /// <summary>
    /// Name, address and code - the three the DM holds against what stands large on the display
    /// (Part 4). A clone says so, because there the right grip is a different one.
    /// </summary>
    private sealed record PendingEntry(PendingPairing Request)
    {
        public override string ToString() => Request switch
        {
            { IsClone: true } =>
                $"{Request.Name} at {Request.Address} - same DeviceId as a device that is already connected",

            // Said, because it changes what the DM is looking at: this is almost always his own
            // display after the control lost control.json, not a stranger. The code still stands
            // beside it - a device that knows this control is the one an impostor would imitate
            // (Part 4).
            { BroughtUnknownToken: true } =>
                $"{Request.Name} at {Request.Address}, code {Request.PairingCode} - brought a token "
                + "this control does not know",

            _ => $"{Request.Name} at {Request.Address}, code {Request.PairingCode}",
        };
    }

    /// <summary>
    /// A device that was turned away stays visible with its reason - a <c>Rejected</c> ended the
    /// connection, so it would otherwise vanish although it is running and has a problem.
    /// <para>
    /// It carries "last seen" rather than "reachable": whether it is up right now is not something
    /// the control can know once the socket is closed. What it knows is when somebody last knocked
    /// - which is the answer to the question actually being asked (Part 4).
    /// </para>
    /// </summary>
    /// <summary>What a row of the list stands for: a device, or one of its screens with the device it belongs to.</summary>
    private sealed record Picked(DeviceView Device, ScreenView? Screen);

    private sealed record RefusedEntry(RefusedDevice Device)
    {
        public override string ToString() =>
            $"{Device.Name} - {Device.Reason}, last seen {Device.LastSeen:HH:mm}";
    }
}
