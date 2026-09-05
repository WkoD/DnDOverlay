using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
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
    private readonly ControlSettings _settings;
    private readonly Uri _address;
    private readonly LogList _log;
    private readonly CancellationTokenSource _listening = new();
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly ComboBox _state = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _firstRun = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _notices = new() { Margin = new Thickness(0, 0, 0, 12) };

    private readonly PanelHead _panelHead = new();
    private readonly StageBoard _board;
    private readonly StagePanel _stage;

    private DevicesWindow? _devices;
    private NetworkWindow? _network;
    private NetworkPanel? _welcome;
    private StageGuard? _guard;

    internal MainWindow(
        ISessionApi session,
        PairingDesk pairing,
        Entrances entrances,
        AssetStore store,
        ControlSettings settings,
        Uri address,
        ProcessLog log)
    {
        _session = session;
        _pairing = pairing;
        _settings = settings;
        _address = address;
        _log = new LogList(log, "Control") { Height = 200 };

        _board = new StageBoard(session, settings, new Pictures(store));
        _stage = new StagePanel(session, entrances, Selected, _status, log.CreateLogger("Control"));

        Title = "DnDOverlay - M2b";
        Width = 620;
        Height = 760;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = $"Listening on {address}. Start a display with --host {address.Host}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(Windows());
        panel.Children.Add(_notices);
        panel.Children.Add(_firstRun);
        panel.Children.Add(new TextBlock
        {
            Text = "Screens",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4),
        });
        panel.Children.Add(_list);
        panel.Children.Add(_panelHead);
        panel.Children.Add(_board);
        panel.Children.Add(StateRow());
        panel.Children.Add(TouchRow());
        panel.Children.Add(_stage);
        panel.Children.Add(_status);
        panel.Children.Add(_log);

        Content = panel;

        // Built with the window and fed from the subscription below - a second stream would take
        // events away from this one, and the device list is half its input (Part 4).
        _guard = new StageGuard(this, session, Environment.MachineName);

        _list.SelectionChanged += async (_, _) => await _stage.RefreshAsync().ConfigureAwait(true);

        _panelHead.Toggled += (_, _) => Switch(!_board.Single);

        // "Set up screen ..." from a tile menu lands in the window that exists, on the screen it
        // was asked for (Part 7).
        _board.Configuring += (_, screen) => Devices().Reveal(screen);
        _board.ActiveChanged += (_, _) => Remember();

        // Where the window stood, if that place is still there (Part 7). Done before the window is
        // shown, so it never appears in one place and jumps to another.
        StagePlace.Restore(this, settings.Current.Window);

        if (StagePlace.Remembered(settings.Current) is { Opened: not null })
        {
            Switch(single: true);
        }

        Closing += (_, _) =>
        {
            _settings.Update(current => current with { Window = StagePlace.Taken(this) });
            _settings.Flush();
        };

        Closed += (_, _) => Dispose();

        _ = ListenAsync();
    }

    /// <summary>Ends the subscription with the window.</summary>
    public void Dispose()
    {
        _listening.Cancel();
        _listening.Dispose();
        _welcome?.Dispose();
        _guard?.Dispose();
        _log.Dispose();
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

                    // Redrawn from the authoritative scene rather than from what this window just
                    // sent: a second control changes the same table, and a panel that trusted its
                    // own command would drift from it (rule 1).
                    // Not bundled and not delayed: it is the answer to "is anything happening at
                    // all", and it has to keep moving while the drawing of a busy stage does not
                    // (Part 7, rank 3 before 4).
                    case SessionEvent.AssetProgress progress:
                        _board.Report(progress.Device, progress.Loads);
                        break;

                    // Rank 4, and it shows: never bundled, never kept, and the first thing
                    // dropped when a subscriber falls behind. A finger position from a moment ago
                    // is not inaccurate, it is worthless (Part 4).
                    case SessionEvent.TouchPoints touching:
                        _board.Touching(touching.Screen, touching.Touches);
                        break;

                    case SessionEvent.ScenePatched:
                    case SessionEvent.SceneReplaced:
                        await _stage.RefreshAsync().ConfigureAwait(true);
                        await _board.RefreshAsync(_listening.Token).ConfigureAwait(true);
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

    /// <summary>Which screen the grips act on, or nothing while the list is empty.</summary>
    /// <summary>
    /// Switches the stage between the overview and one screen on its own, and keeps the answer for
    /// THIS monitor arrangement (Part 7).
    /// </summary>
    private void Switch(bool single)
    {
        _board.Single = single;
        _panelHead.Show(single);

        Remember();
    }

    /// <summary>
    /// Keeps which view is open where. Called on every change of the active screen as well,
    /// because in the single view the active screen IS the open one - the two are the same fact
    /// from two directions (Part 7).
    /// </summary>
    private void Remember() =>
        _settings.Update(current => current with
        {
            StageViews = StagePlace.With(current.StageViews, _board.Single ? _board.Active : null),
        });

    private ScreenRef? Selected() =>
        _list.SelectedItem is ScreenEntry entry ? entry.Screen : null;

    /// <summary>
    /// Flattened to a list here, because a grip that sends one image needs a target rather than a
    /// tree. The tree is what the device window is for, and it draws from the same event.
    /// </summary>
    private void Show(IReadOnlyList<DeviceView> devices)
    {
        Welcome(devices.Count == 0);
        _guard?.Knows(devices);

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

        _board.Show([.. devices.SelectMany(device => device.Screens)]);

        _ = _board.RefreshAsync(_listening.Token);
    }

    /// <summary>
    /// Says something the DM has to read, beside the stage rather than in front of it.
    /// <para>
    /// Not a message box, and that is the point rather than a matter of taste: what is reported
    /// here is true for the rest of the evening, and a dialog is gone the moment it is clicked
    /// away - usually before it was read. It stands until it is dismissed, and the control stays
    /// operable underneath it (Part 3, Part 7).
    /// </para>
    /// <para>
    /// M1b has one caller: a replaced configuration. That one may not be quiet, because it costs
    /// every pairing - the displays come back as unknown devices, and being told beats finding out
    /// at the table (Part 6).
    /// </para>
    /// </summary>
    internal void Notify(string text)
    {
        var message = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var dismiss = new Button
        {
            Content = "OK",
            Padding = new Thickness(12, 2, 12, 2),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var row = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(dismiss, Dock.Right);
        row.Children.Add(dismiss);
        row.Children.Add(message);

        var notice = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0x8A, 0x00)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row,
        };

        dismiss.Click += (_, _) => _notices.Children.Remove(notice);

        _notices.Children.Add(notice);
    }

    /// <summary>
    /// The first-run view, and it goes away by itself. It answers the three questions of the very
    /// first start - where am I, can anything reach me, what now - and a machine with a device
    /// already paired has answered all three (Part 7).
    /// <para>
    /// The grips inside it stay reachable through the network window, because a firewall rule stops
    /// biting <b>without disappearing</b> when Windows reclassifies a network as public.
    /// </para>
    /// </summary>
    private void Welcome(bool show)
    {
        if (show == (_welcome is not null))
        {
            return;
        }

        if (!show)
        {
            _welcome?.Dispose();
            _welcome = null;
            _firstRun.Child = null;

            return;
        }

        _welcome = new NetworkPanel(_address.Port, firstRun: true);
        _firstRun.Child = _welcome;
    }

    /// <summary>
    /// The window <i>Devices</i>, brought forward rather than duplicated - it has two ways in, the
    /// button here and the tile menu, and two of it would be two answers about one machine.
    /// </summary>
    private DevicesWindow Devices()
    {
        if (_devices is { IsLoaded: true } standing)
        {
            _ = standing.Activate();

            return standing;
        }

        _devices = new DevicesWindow(_session, _pairing, _address) { Owner = this };
        _devices.Show();

        return _devices;
    }

    /// <summary>
    /// One instance each, reopened rather than duplicated. In M5b both move into the menu in the
    /// panel head, together with the log panel - the one place everything that is not a grip during
    /// play is reached from (Part 7).
    /// </summary>
    private StackPanel Windows()
    {
        var devices = new Button { Content = "Devices ...", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var network = new Button { Content = "Network ...", Padding = new Thickness(12, 6, 12, 6) };

        devices.Click += (_, _) => Devices();

        network.Click += (_, _) =>
        {
            if (_network is { IsLoaded: true })
            {
                _ = _network.Activate();
                return;
            }

            _network = new NetworkWindow(_address.Port) { Owner = this };
            _network.Show();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };

        row.Children.Add(devices);
        row.Children.Add(network);

        return row;
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

    /// <summary>
    /// The one switch for every device: whether the tables report the fingers on them (Part 4,
    /// Part 7).
    /// <para>
    /// It is here and nowhere else - not on the device page and not in the tray window - because
    /// at the table it changes nothing anybody could see or operate. What the trails SHOW needs
    /// the thumbnail, so the switch belongs where the picture is; that it also stops the sending
    /// is a consequence rather than a device's preference (Part 6).
    /// </para>
    /// <para>
    /// Nothing is drawn from it yet: the thumbnail is M4. What it does today is decide whether the
    /// traffic exists at all, and that is worth having before there is a surface for it - it is
    /// the rank-4 load every other limit is measured against.
    /// </para>
    /// </summary>
    private CheckBox TouchRow()
    {
        var wanted = _settings.Current.ShowTouchPoints;

        var box = new CheckBox
        {
            Content = "Show touch points",
            IsChecked = wanted,
            Margin = new Thickness(0, 0, 0, 12),
        };

        // Told once at startup as well, not only when it changes: the hub keeps this for the run
        // and a display that connects afterwards is answered out of it, so a control that never
        // spoke would leave every table reporting whatever it defaults to.
        _ = _session.SetTouchPointsAsync(wanted);

        void Set(bool reporting)
        {
            _settings.Update(configuration => configuration with { ShowTouchPoints = reporting });

            _ = _session.SetTouchPointsAsync(reporting);

            _status.Text = reporting
                ? "Tables report their touch points."
                : "Tables no longer report their touch points.";
        }

        box.Checked += (_, _) => Set(true);
        box.Unchecked += (_, _) => Set(false);

        return box;
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
