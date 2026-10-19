using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Logging;
using DnDOverlay.Hub;
using DnDOverlay.Rendering.Windows;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The DM's window (Part 7): the stage with its tiles, and beside it the campaign panel docked at
/// an edge, with a line between the two that can be dragged. Notices and the first-run view stand
/// above, the status line below.
/// <para>
/// The M2 scaffold below the stage is gone since M5b3: its entrances live at the foot of the tab
/// "Pictures", its switches on the tiles, the state selector under every tile, and what is no grip
/// in play - devices, network, touch points - in the head's menu.
/// </para>
/// </summary>
internal sealed class MainWindow : Window, IDisposable
{
    private readonly ISessionApi _session;
    private readonly PairingDesk _pairing;
    private readonly ControlSettings _settings;
    private readonly Uri _address;
    private readonly CancellationTokenSource _listening = new();
    private readonly StageMemory _memory = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _firstRun = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _notices = new() { Margin = new Thickness(0, 0, 0, 12) };

    private readonly PanelHead _panelHead = new();
    private readonly StageBoard _board;
    private readonly StockView _stock;
    private readonly IntakeRunner _intake;
    private readonly CampaignPanel _panel;
    private readonly Grid _main = new();
    private readonly GridSplitter _splitter = new() { Background = SystemColors.ControlLightBrush, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
    private readonly FrameWatch _frames;

    /// <summary>This window's own line into the trail - shared, so the log carries one category.</summary>
    private readonly ILogger _logger;

    private DevicesWindow? _devices;
    private NetworkWindow? _network;
    private NetworkPanel? _welcome;
    private StageGuard? _guard;
    private readonly ScenesPanel _scenes;
    private readonly CampaignDesk _desk;
    private ControlPower? _power;

    internal MainWindow(
        ISessionApi session,
        PairingDesk pairing,
        Entrances entrances,
        CampaignDesk desk,
        ControlSettings settings,
        Uri address,
        ProcessLog log)
    {
        _session = session;
        _pairing = pairing;
        _settings = settings;
        _address = address;

        var pictures = new Pictures(desk.Stock);

        _desk = desk;

        _board = new StageBoard(session, settings, pictures);

        _logger = log.CreateLogger("Control");

        _intake = new IntakeRunner(session, entrances, said => _status.Text = said, _logger);

        _stock = new StockView(session, desk, pictures, () => _board.Active, () => _board.Screens, said => _status.Text = said);

        _scenes = new ScenesPanel(
            session,
            desk,
            pictures,
            () => _board.Active,
            () => _board.Screens,
            said => _status.Text = said,
            _logger);

        // The counter the control never had. It does NOT hold the render hook - Redraw drives it,
        // and only while the stage is actually drawing, because a permanent subscription would keep
        // a battery machine rendering at sixty frames a second all evening. What it therefore
        // measures is the stretch that matters: the stage under a moving hand.
        //
        // It reports and does not judge: a budget needs a cadence, and a stream this sparse cannot
        // estimate one - measured in the second hand-run of M4, where a stage holding 16.7 ms was
        // warned against a budget of 2.8 ms (FrameWatch.WhileDrawing).
        _frames = FrameWatch.WhileDrawing(window =>
        {
            ControlLog.FrameTimes(
                _logger,
                window.Seconds,
                window.Frames,
                window.MedianMs,
                window.P95Ms,
                window.MaxMs,
                window.CadenceMs,
                window.CpuPercent,
                window.GcMs,
                window.Sweeps,
                window.DrawMs,
                window.HandMs);

            // Right behind it and over the same window, so the two lines read as one: the frame
            // line says THAT the collector took the time, this one says what it was collecting
            // (fourth hand-run, 15.09.2026 - see StageMemory for the three causes it separates).
            var memory = _memory.Read();

            ControlLog.StageMemory(
                _logger,
                window.Seconds,
                memory.AllocatedMb,
                memory.HeapMb,
                memory.LargeMb,
                memory.ProcessMb,
                memory.Gen0,
                memory.Gen1,
                memory.Gen2,
                pictures.Loaded,
                pictures.Greyed,
                _board.Tiles);
        });

        Redraw.Measure(_frames);

        Title = "DnDOverlay";
        Width = 1280;
        Height = 820;

        _panel = new CampaignPanel(_panelHead, _stock, _scenes, _intake, settings);

        // Notices and the first-run view across the top, the stage and the panel beneath, the status
        // line at the foot. The stage's row takes what is left, so pulling the window taller gives
        // the room to the tiles (hand-run of M4, second run, 24a).
        var panel = new Grid { Margin = new Thickness(12) };

        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_notices, 0);
        Grid.SetRow(_firstRun, 1);
        Grid.SetRow(_main, 2);
        Grid.SetRow(_status, 3);

        panel.Children.Add(_notices);
        panel.Children.Add(_firstRun);
        panel.Children.Add(_main);
        panel.Children.Add(_status);

        _status.Margin = new Thickness(0, 6, 0, 0);

        Content = panel;

        // The touch switch is told once at startup, not only when it changes: the hub keeps it for
        // the run and answers devices that connect later out of it (Part 4).
        _ = _session.SetTouchPointsAsync(settings.Current.ShowTouchPoints);

        // The panel's first place comes from the window's shape - landscape on the right, portrait
        // at the bottom (DM, 19.10.2026); after that it is the DM's and kept globally (Part 7).
        Loaded += (_, _) =>
        {
            var placement = settings.Current.Panel
                ?? new PanelPlacement(ActualWidth >= ActualHeight ? PanelSide.Right : PanelSide.Bottom, ActualWidth >= ActualHeight ? 380 : 300);

            Dock(placement.Side, placement.Size);
        };

        // Built with the window and fed from the subscription below - a second stream would take
        // events away from this one, and the device list is half its input (Part 4).
        _guard = new StageGuard(this, session, Environment.MachineName);
        _power = new ControlPower(_logger, Notify);

        _panelHead.Toggled += (_, _) => Switch(!_board.Single);

        // "Set up screen ..." from a tile menu lands in the window that exists, on the screen it
        // was asked for (Part 7).
        _board.Configuring += (_, screen) => Devices().Reveal(screen);
        _board.Saving += async (_, screen) => await _scenes.SaveAsync(this, screen).ConfigureAwait(true);

        // A drop from outside onto a tile: into the stock, then onto that screen where it was let go.
        _board.DroppedFromOutside += async (_, dropped) =>
            await _intake.RunAsync(_intake.Entrances.FromDataObject(dropped.Outside.Data), dropped.Screen, dropped.Outside.At).ConfigureAwait(true);

        _panelHead.MenuAsked += (_, _) => OpenMenu();

        // Another campaign, or none: the pictures the stage draws are other files now, the scenes
        // tab shows other scenes, and the head says which one (Part 3, Part 7).
        desk.Changed += (_, _) =>
        {
            pictures.Clear();
            _panelHead.Campaign(desk.OpenLabel);
            _panel.Refresh();
            _ = _stock.CountAsync();
            _ = _board.RefreshAsync(_listening.Token);
        };

        desk.Reported += (_, words) => Notify(words);

        _panelHead.CampaignAsked += (_, _) => new CampaignSwitcher(desk, this, said => _status.Text = said).Open(_panelHead);
        _panelHead.Campaign(desk.OpenLabel);
        _board.ActiveChanged += (_, _) => Remember();

        // Whichever of the two ways switched the view - the button here or the tile's own menu -
        // the answer is written down and the button relabelled in one place.
        _board.ViewChanged += (_, _) =>
        {
            _panelHead.Show(_board.Single);
            Remember();
        };

        // <b>While the hand is over the stage, the space bar belongs to the stage.</b> Handled
        // here rather than asked about at the tile: the key goes to whatever has keyboard FOCUS,
        // which after any click on a grip is a button - and a button presses on space. Swallowing
        // it stops that without the tile having to guess from focus what the pointer is doing, and
        // the key state the spotlight reads is untouched by the routing (TileFace.Pointing).
        PreviewKeyDown += (_, key) =>
        {
            if (key.Key is Key.Space && _board.IsMouseOver)
            {
                key.Handled = true;
            }

            // Del removes what is selected, as the menu's "Remove" does (fourth hand-run,
            // 15.09.2026) - except while typing, where it belongs to the text. The stock list has
            // no delete of its own yet; when it gets one (M5b) the key has to be split by focus.
            if (key.Key is Key.Delete
                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
            {
                key.Handled = _board.RemoveSelected();
            }
        };

        // Where the window stood, if that place is still there (Part 7). Done before the window is
        // shown, so it never appears in one place and jumps to another.
        StagePlace.Restore(this, settings.Current.Window);

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
        _power?.Dispose();

        // Off the collector first, then away: a tick arriving at a disposed watch would be the one
        // fault this line exists to keep out of a shutdown.
        Redraw.Measure(null);
        _frames.Dispose();
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
            // <b>A stream that ends is subscribed to again, and that is the whole of this loop.</b>
            // The hub never drops a state event: where one cannot be queued it ENDS that reader's
            // stream rather than serving it something stale, and names the way back in the same
            // breath - "subscribing again yields a fresh opening picture" (SessionEvents).
            //
            // This window never went back. The await foreach simply ran to its end, ListenAsync
            // returned into a discarded task, and from that moment the control was DEAF: no patch,
            // so no Draw, so no Redraw.Ask, so no render hook and not one frame line for the rest of
            // the run. The stage kept the last picture it had been given and answered nothing,
            // while the display next door carried on perfectly - it has a socket of its own.
            //
            // Measured at the table, 07.09.2026: an intake of 723 files published 714 AddItem
            // patches into a queue that holds 256, one per picture, from the very thread that had
            // to read them. The stream was cut about a third of the way in. Nothing was logged,
            // because ending a stream is ordinary hub operation - the fault was only ever on this
            // side, in not noticing.
            while (true)
            {
                await ListenOnceAsync().ConfigureAwait(true);

                _listening.Token.ThrowIfCancellationRequested();

                ControlLog.SessionStreamRestarted(_logger, ++_restarts);

                // The opening picture is NOT enough on its own. Show() returns early when the
                // screens are unchanged - which is precisely this case - so the board would keep
                // the stale scene it froze on. Asking the hub outright is what puts the tiles right.
                await _board.RefreshAsync(_listening.Token).ConfigureAwait(true);
                await _stock.CountAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed.
        }
    }

    /// <summary>How often the stream had to be taken up again. Read by the line that reports it.</summary>
    private int _restarts;

    /// <summary>
    /// One subscription's life. It returns when the hub ends the stream - which is a thing that
    /// happens, not a thing that has gone wrong here (see <see cref="ListenAsync"/>).
    /// </summary>
    private async Task ListenOnceAsync()
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

                // Rank 4 as well, and only while a tile asked for it (Part 4).
                case SessionEvent.Diagnostics reading:
                    _board.Diagnosed(reading.Device, reading.Reading);
                    break;

                case SessionEvent.ScenePatched:
                case SessionEvent.SceneReplaced:
                    await _board.RefreshAsync(_listening.Token).ConfigureAwait(true);
                    await _stock.CountAsync().ConfigureAwait(true);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Which screen the grips act on, or nothing while the list is empty.</summary>
    /// <summary>
    /// Switches the stage between the overview and one screen on its own, and keeps the answer for
    /// THIS monitor arrangement (Part 7).
    /// </summary>
    private void Switch(bool single) => _board.Single = single;

    /// <summary>
    /// Keeps which view is open where. Called on every change of the active screen as well,
    /// because in the single view the active screen IS the open one - the two are the same fact
    /// from two directions (Part 7).
    /// </summary>
    private void Remember() =>
        _settings.Update(current => current with
        {
        });

    /// <summary>
    /// The devices and their screens, as the stage needs them: tiles come and go with the screens,
    /// and every tile hears what concerns it - wish, reasons, battery.
    /// </summary>
    private void Show(IReadOnlyList<DeviceView> devices)
    {
        Welcome(devices.Count == 0);
        _guard?.Knows(devices);
        _power?.Displays(devices.Any(device => device.Connected));

        _board.Show([.. devices.SelectMany(device => device.Screens)]);
        _board.Know(devices);

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
    /// <returns>How to take the notice away again, for one that stops being true by itself.</returns>
    internal Action Notify(string text)
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

        DockPanel.SetDock(dismiss, System.Windows.Controls.Dock.Right);
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

        return () => _notices.Children.Remove(notice);
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

    /// <summary>The network window, reopened rather than duplicated.</summary>
    private void Network()
    {
        if (_network is { IsLoaded: true })
        {
            _ = _network.Activate();
            return;
        }

        _network = new NetworkWindow(_address.Port) { Owner = this };
        _network.Show();
    }

    /// <summary>
    /// The head's menu (Part 7): clear all and send to all, then - set apart - what has nothing to do
    /// with play: devices, network, the touch points, where the panel docks. Protocol and settings
    /// join it with the windows they open (M5b7).
    /// </summary>
    private void OpenMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _panelHead.MenuButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        // Both one action without a question; undo brings them back (Part 7, from M5b6).
        menu.Items.Add(Item("Clear all", () => _ = _session.ClearAllAsync()));

        var chosen = _stock.Chosen;
        var toAll = Item("Send to all", () => _ = _session.SendToAllAsync(chosen));

        toAll.IsEnabled = chosen.Count > 0;
        menu.Items.Add(toAll);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Devices ...", () => Devices()));
        menu.Items.Add(Item("Network ...", Network));

        var touch = new MenuItem { Header = "Show touch points", IsCheckable = true, IsChecked = _settings.Current.ShowTouchPoints };

        touch.Click += (_, _) =>
        {
            var reporting = touch.IsChecked;

            _settings.Update(configuration => configuration with { ShowTouchPoints = reporting });
            _ = _session.SetTouchPointsAsync(reporting);
            _status.Text = reporting ? "Tables report their touch points." : "Tables no longer report their touch points.";
        };

        menu.Items.Add(touch);

        var docking = new MenuItem { Header = "Dock panel" };
        var side = _settings.Current.Panel?.Side ?? PanelSide.Right;

        foreach (var edge in Enum.GetValues<PanelSide>())
        {
            var item = new MenuItem { Header = edge.ToString(), IsCheckable = true, IsChecked = edge == side };

            item.Click += (_, _) => Dock(edge, _settings.Current.Panel?.Size ?? (edge is PanelSide.Left or PanelSide.Right ? 380 : 300));
            docking.Items.Add(item);
        }

        menu.Items.Add(docking);

        menu.IsOpen = true;
    }

    /// <summary>
    /// Puts the panel at an edge, at a size, with the line to drag between it and the stage - and
    /// keeps both, globally (Part 7). At least a row of tiles, at most half the window: the stage
    /// must not disappear.
    /// </summary>
    private void Dock(PanelSide side, double size)
    {
        _main.Children.Clear();
        _main.RowDefinitions.Clear();
        _main.ColumnDefinitions.Clear();

        var across = side is PanelSide.Left or PanelSide.Right;
        var panelFirst = side is PanelSide.Left or PanelSide.Top;
        var limit = Math.Max(200, (across ? ActualWidth : ActualHeight) / 2);
        var length = new GridLength(Math.Clamp(size, 160, limit));
        var star = new GridLength(1, GridUnitType.Star);

        GridLength[] lengths = panelFirst ? [length, new GridLength(6), star] : [star, new GridLength(6), length];
        UIElement[] parts = panelFirst ? [_panel, _splitter, _board] : [_board, _splitter, _panel];

        for (var index = 0; index < 3; index++)
        {
            if (across)
            {
                _main.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = lengths[index],
                    MinWidth = index == (panelFirst ? 0 : 2) ? 160 : 0,
                    MaxWidth = index == (panelFirst ? 0 : 2) ? limit : double.PositiveInfinity,
                });
                Grid.SetColumn(parts[index], index);
                Grid.SetRow(parts[index], 0);
            }
            else
            {
                _main.RowDefinitions.Add(new RowDefinition
                {
                    Height = lengths[index],
                    MinHeight = index == (panelFirst ? 0 : 2) ? 160 : 0,
                    MaxHeight = index == (panelFirst ? 0 : 2) ? limit : double.PositiveInfinity,
                });
                Grid.SetRow(parts[index], index);
                Grid.SetColumn(parts[index], 0);
            }

            _main.Children.Add(parts[index]);
        }

        _splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        _splitter.VerticalAlignment = VerticalAlignment.Stretch;
        _splitter.ResizeDirection = across ? GridResizeDirection.Columns : GridResizeDirection.Rows;

        _panel.Lying = !across;

        Keep(side);
    }

    private void Keep(PanelSide side)
    {
        var size = _settings.Current.Panel?.Size ?? 380;

        _settings.Update(current => current with
        {
            Panel = (current.Panel ?? new PanelPlacement(side, size)) with { Side = side },
        });

        _splitter.DragCompleted -= Resized;
        _splitter.DragCompleted += Resized;
    }

    private void Resized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var size = _panel.Lying ? _panel.ActualHeight : _panel.ActualWidth;

        _settings.Update(current => current with
        {
            Panel = (current.Panel ?? new PanelPlacement(PanelSide.Right, size)) with { Size = size },
        });
    }

    private static MenuItem Item(string header, Action chosen)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => chosen();

        return item;
    }
}
