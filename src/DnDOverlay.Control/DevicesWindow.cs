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
/// The two bands - everything about a device, everything about one of its screens - arrive in M5b
/// out of the shared parameter description (Part 6). Until then this is the list and nothing else.
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

    internal DevicesWindow(ISessionApi session, PairingDesk pairing, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _session = session;
        _pairing = pairing;

        Title = "Devices";
        Width = 640;
        Height = 720;

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
        panel.Children.Add(Naming());
        panel.Children.Add(Waking());

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
        panel.Children.Add(_status);

        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Closed += (_, _) => Dispose();

        _ = ListenAsync();
    }

    /// <summary>
    /// Ends the subscription with the window. Nothing else does - a stream nobody reads would fill
    /// its channel and be cut off eventually, which is the right answer for a subscriber that has
    /// stopped listening and the wrong one to arrive at by accident.
    /// </summary>
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
        _tree.Items.Clear();
        _rows.Clear();

        foreach (var device in devices)
        {
            // The device rides along on both levels, so selecting a screen answers "which device"
            // as well as selecting the device does - the grips below act on the machine.
            var node = new TreeViewItem
            {
                Header = Describe(device),
                IsExpanded = true,
                Tag = device,
            };

            foreach (var screen in device.Screens)
            {
                var row = new TreeViewItem { Header = Describe(screen), Tag = device };

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
    private Button Naming()
    {
        var button = new Button
        {
            Content = "Show the screen names on the selected device",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        button.Click += async (_, _) =>
        {
            if ((_tree.SelectedItem as TreeViewItem)?.Tag is not DeviceView device)
            {
                _status.Text = "Select a device first.";
                return;
            }

            // Said rather than left to a button that does nothing: a device that is switched off
            // has no overlay to show anything on, and the grip is silent by nature.
            if (!device.Connected)
            {
                _status.Text = $"{device.Name} is not connected.";
                return;
            }

            await _session.IdentifyScreensAsync(device.Device).ConfigureAwait(true);
            _status.Text = $"{device.Name} is showing its screen names.";
        };

        return button;
    }

    /// <summary>
    /// Whether that device holds its screens on. Part 6 asks for it to be switchable from afar, and
    /// this is the machine the demand is written for: nobody is sitting at a display PC to notice
    /// that its table went dark.
    /// <para>
    /// Two buttons rather than a checkbox, and that is deliberate for now: the value lives in the
    /// device and does not travel back in <see cref="DeviceView"/>, so a box would have to show a
    /// state it does not know. The band that shows the real value is built in M5b out of the shared
    /// parameter description, together with the other nine device-scope parameters (Part 6, Part 7).
    /// </para>
    /// </summary>
    private StackPanel Waking()
    {
        var keep = new Button { Content = "Keep the screens awake", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var sleep = new Button { Content = "Let them sleep", Padding = new Thickness(12, 6, 12, 6) };

        keep.Click += (_, _) => WakeAsync(keep: true);
        sleep.Click += (_, _) => WakeAsync(keep: false);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        row.Children.Add(keep);
        row.Children.Add(sleep);

        return row;
    }

    private async void WakeAsync(bool keep)
    {
        if ((_tree.SelectedItem as TreeViewItem)?.Tag is not DeviceView device)
        {
            _status.Text = "Select a device first.";
            return;
        }

        // Sent whether or not the device is connected. What is set here stands in control.json and
        // goes out with the next ConfigUpdate - without that the remote configuration would be
        // useless exactly when it is wanted, before the display PC is switched on (Part 7).
        await _session
            .ApplyConfigAsync(device.Device, new ConfigUpdate([], new DeviceSettings(KeepAwake: keep)))
            .ConfigureAwait(true);

        _status.Text = keep
            ? $"{device.Name} keeps its screens awake."
            : $"{device.Name} lets its screens sleep.";
    }

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
    private sealed record RefusedEntry(RefusedDevice Device)
    {
        public override string ToString() =>
            $"{Device.Name} - {Device.Reason}, last seen {Device.LastSeen:HH:mm}";
    }
}
