using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
/// <para>
/// <b>Pinned or retracting</b> (Part 7): pinned the panel shares the room with the stage; retracted
/// only a narrow tab stays at the edge, a tap on it pushes the panel over the stage, and a tap beside
/// it, a swipe toward the edge or the start of a carry out of it sends it back.
/// </para>
/// </summary>
internal sealed class MainWindow : Window, IDisposable
{
    private readonly RecordingSession _session;
    private readonly PairingDesk _pairing;
    private readonly ControlSettings _settings;
    private readonly Uri _address;
    private readonly CancellationTokenSource _listening = new();

    /// <summary>
    /// The token of <see cref="_listening"/>, taken once. Handlers that run after <c>Closed</c> - a
    /// late continuation, a desk event - still read it, and asking a disposed source for its token
    /// throws <see cref="ObjectDisposedException"/> where a cancelled token only says "cancelled".
    /// </summary>
    private readonly CancellationToken _closing;

    private readonly StageMemory _memory = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _firstRun = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _notices = new() { Margin = new Thickness(0, 0, 0, 12) };

    private readonly PanelHead _panelHead = new();
    private readonly StageBoard _board;
    private readonly StockView _stock;
    private readonly StockEdits _edits;
    private readonly IntakeRunner _intake;
    private readonly CampaignPanel _panel;
    private readonly Grid _root = new() { Margin = new Thickness(12) };
    private readonly Grid _main = new();

    /// <summary>The main room with the log panel at its own edge beside it, or the main room alone (Part 7).</summary>
    private readonly Grid _frame = new();

    private readonly LogPanel _logPanel;
    /// <summary>
    /// The line between the log panel and the main room. Stretched in both directions like the
    /// campaign panel's: a splitter left at its default alignment hugs one edge of its 6 DIP cell
    /// and comes out zero wide, so there was nothing to take hold of (M5 review).
    /// </summary>
    private readonly GridSplitter _logSplitter = new()
    {
        Background = SystemColors.ControlLightBrush,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    /// <summary>The stage with the retracted panel lying over it - only in use while the panel is not pinned.</summary>
    private readonly Grid _stage = new();

    /// <summary>Where the retracted panel slides; it clips, so a panel pushed out of it can be neither seen nor hit.</summary>
    private readonly Grid _overlay = new() { ClipToBounds = true };

    private readonly TranslateTransform _slide = new();
    private readonly TextBlock _edgeLabel = new();
    private readonly Button _edgeTab = new() { Padding = new Thickness(4, 8, 4, 8) };
    private readonly PanelCarry _carry;

    /// <summary>Whether the panel shares the room with the stage rather than retracting to a tab.</summary>
    private bool _pinned = true;

    /// <summary>Whether the retracted panel is out over the stage right now.</summary>
    private bool _out;

    private PanelSide _side = PanelSide.Right;
    private double _overlaySize = 380;
    private readonly GridSplitter _splitter = new() { Background = SystemColors.ControlLightBrush, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
    private readonly FrameWatch _frames;

    /// <summary>This window's own line into the trail - shared, so the log carries one category.</summary>
    private readonly ILogger _logger;

    private DevicesWindow? _devices;
    private SettingsWindow? _settingsWindow;
    private readonly ProcessLog _log;

    /// <summary>What Windows showed at the start - the language "Automatic" stands for (Part 8).</summary>
    private readonly CultureInfo _system;
    private NetworkPanel? _welcome;
    private StageGuard? _guard;
    private readonly ScenesPanel _scenes;
    private readonly LayoutsPanel _layouts;
    private readonly CampaignDesk _desk;
    private ControlPower? _power;

    internal MainWindow(
        RecordingSession session,
        PairingDesk pairing,
        Entrances entrances,
        CampaignDesk desk,
        ControlSettings settings,
        Uri address,
        ProcessLog log,
        CultureInfo system)
    {
        _session = session;
        _closing = _listening.Token;
        _log = log;
        _system = system;
        _pairing = pairing;
        _settings = settings;
        _address = address;

        var pictures = new Pictures(desk.Stock);

        _desk = desk;

        _board = new StageBoard(session, settings, pictures);

        _logger = log.CreateLogger("Control");

        _logPanel = new LogPanel(log, "Control");
        _logSplitter.DragCompleted += LogResized;

        _intake = new IntakeRunner(session, entrances, said => _status.Text = said, _logger);

        _carry = new PanelCarry(_root);

        _edits = new StockEdits(session, desk, () => _board.Screens, said => _status.Text = said, _logger);

        // The labels follow the timeline, on the UI thread: a step is recorded wherever the command
        // finished, and a button must never show the act before last.
        session.TimelineChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            _panelHead.Timeline(session.Timeline.UndoLabel, session.Timeline.RedoLabel));
        _panelHead.UndoAsked += async (_, _) => await UndoAsync().ConfigureAwait(true);
        _panelHead.RedoAsked += async (_, _) => await RedoAsync().ConfigureAwait(true);
        _stock = new StockView(session, desk, pictures, () => _board.Active, () => _board.Screens, said => _status.Text = said, _carry, _edits);

        _scenes = new ScenesPanel(
            session,
            desk,
            pictures,
            () => _board.Active,
            () => _board.Screens,
            said => _status.Text = said,
            _logger,
            _carry,
            settings);

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

        _layouts = new LayoutsPanel(session, desk, pictures, () => _board.Screens, said => _status.Text = said, _logger, _carry, settings);
        _layouts.ScenesSaved += (_, _) => _scenes.Refresh();

        _panel = new CampaignPanel(_panelHead, _stock, _scenes, _layouts, _intake, settings);

        // Notices and the first-run view across the top, the stage and the panel beneath, the status
        // line at the foot. The stage's row takes what is left, so pulling the window taller gives
        // the room to the tiles (hand-run of M4, second run, 24a).
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_notices, 0);
        Grid.SetRow(_firstRun, 1);
        Grid.SetRow(_frame, 2);
        Grid.SetRow(_status, 3);

        _root.Children.Add(_notices);
        _root.Children.Add(_firstRun);
        _root.Children.Add(_frame);
        _root.Children.Add(_status);

        _status.Margin = new Thickness(0, 6, 0, 0);

        Content = _root;

        WireRetracting();
        WireCarrying();

        // The touch switch is told once at startup, not only when it changes: the hub keeps it for
        // the run and answers devices that connect later out of it (Part 4).
        _ = _session.SetTouchPointsAsync(settings.Current.ShowTouchPoints);

        // The panel's first place comes from the window's shape - landscape on the right, portrait
        // at the bottom (DM, 19.10.2026); after that it is the DM's and kept globally (Part 7).
        Loaded += (_, _) =>
        {
            var placement = settings.Current.Panel
                ?? new PanelPlacement(ActualWidth >= ActualHeight ? PanelSide.Right : PanelSide.Bottom, ActualWidth >= ActualHeight ? 380 : 300);

            _pinned = placement.Pinned;
            Dock(placement.Side, placement.Size);
            Frame();
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
            // The campaign open last, for the offer after a restart (Part 4) - and an offer standing
            // is answered by any campaign that opens.
            if (desk.Open is { } now)
            {
                if (!string.Equals(_settings.Current.LastCampaign, now.Name, StringComparison.Ordinal))
                {
                    _settings.Update(current => current with { LastCampaign = now.Name });
                }

                _withdrawOffer?.Invoke();
                _withdrawOffer = null;
            }

            pictures.Clear();
            _layouts.Refresh();
            ShowCampaign();
            _panel.Refresh();
            _ = _stock.CountAsync();
            _ = _board.RefreshAsync(_closing);
        };

        desk.Reported += (_, report) => ShowReport(report);

        // A picture pulled in that is already in the stock is shown there - the panel narrows to it
        // (Part 7). A retracted panel comes out for it, as for "Show in stock" from a tile; narrowed
        // behind the edge, the answer to the drop would be nowhere to see.
        _intake.AlreadyThere += (_, _) =>
        {
            if (!_pinned && !_out)
            {
                Slide(open: true);
            }
        };
        desk.Measured += (_, measure) =>
        {
            Report(measure);
            OfferVerify();
        };

        _panelHead.CampaignAsked += (_, _) =>
        {
            // Looked at again here: a drive plugged back in, or pulled, since the head last said.
            ShowCampaign();
            new CampaignSwitcher(desk, this, said => _status.Text = said, Tidy, _settings.Current.LastCampaign).Open(_panelHead);
        };
        ShowCampaign();
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
        PreviewKeyDown += async (_, key) =>
        {
            // Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) take back and redo - except while typing, where a
            // text box has an undo of its own and a name half typed must not vanish from the table.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
                && key.Key is Key.Z or Key.Y
                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
            {
                key.Handled = true;

                var redo = key.Key is Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                await (redo ? RedoAsync() : UndoAsync()).ConfigureAwait(true);

                return;
            }

            if (key.Key is Key.Space && _board.IsMouseOver)
            {
                key.Handled = true;
            }

            // Del removes what is selected, as the menu's "Remove" does (fourth hand-run,
            // 15.09.2026) - except while typing, where it belongs to the text. Split by focus since the
            // stock has a delete of its own (Part 7): with the stock focused it deletes from the
            // campaign - after its question - and otherwise it takes items off the stage.
            if (key.Key is Key.Delete
                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
            {
                if (_stock.IsKeyboardFocusWithin)
                {
                    key.Handled = true;
                    _ = _stock.DeleteChosenAsync();
                }
                else
                {
                    key.Handled = _board.RemoveSelected();
                }
            }
        };

        // Where the window stood, if that place is still there (Part 7). Applied as the handle comes
        // into being, before the window is drawn, so it never appears in one place and jumps to another.
        StagePlace.Restore(this, settings.Current.Window);

        Closing += (_, _) =>
        {
            _settings.Update(current => current with { Window = StagePlace.Taken(this) ?? current.Window });
            _settings.Flush();
        };

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            Dispose();
        };

        _ = ListenAsync();
    }

    /// <summary>Ends the subscription with the window.</summary>
    public void Dispose()
    {
        _session.Dispose();
        _listening.Cancel();
        _listening.Dispose();
        _welcome?.Dispose();
        _guard?.Dispose();
        _power?.Dispose();
        _logPanel.Dispose();

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

                _closing.ThrowIfCancellationRequested();

                ControlLog.SessionStreamRestarted(_logger, ++_restarts);

                // The opening picture is NOT enough on its own. Show() returns early when the
                // screens are unchanged - which is precisely this case - so the board would keep
                // the stale scene it froze on. Asking the hub outright is what puts the tiles right.
                await _board.RefreshAsync(_closing).ConfigureAwait(true);
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
                .Subscribe(_closing)
                .WithCancellation(_closing)
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

                case SessionEvent.WindowList list:
                    _board.Windows(list.Device, list.Windows);
                    break;

                case SessionEvent.WindowResult result:
                    _board.WindowAnswered(result.Device, result.Handle, result.Outcome);
                    break;

                case SessionEvent.ScenePatched:
                case SessionEvent.SceneReplaced:
                    await _board.RefreshAsync(_closing).ConfigureAwait(true);
                    await _stock.CountAsync().ConfigureAwait(true);
                    await OfferLastCampaignAsync().ConfigureAwait(true);
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
        _connected = devices.Any(device => device.Connected);
        _logPanel.Know(devices);

        _ = _board.RefreshAsync(_closing);
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
    internal Action Notify(string text) => Notify(text, dismissed: null);

    /// <summary>
    /// A notice whose dismissal means something - <paramref name="dismissed"/> runs on "OK" - and
    /// that may carry one more answer beside it, which dismisses it too.
    /// </summary>
    private Action Notify(string text, Action? dismissed, string? otherLabel = null, Action? other = null) =>
        Notice(
            new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0),
            },
            dismissed,
            otherLabel,
            other);

    /// <summary>A notice of any content - a line, or a line with more behind it - with "OK" and maybe one more answer beside it.</summary>
    private Action Notice(FrameworkElement message, Action? dismissed, string? otherLabel = null, Action? other = null)
    {
        var dismiss = new Button
        {
            Content = Strings.MainNoticeOk,
            Padding = new Thickness(12, 2, 12, 2),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var row = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(dismiss, System.Windows.Controls.Dock.Right);
        row.Children.Add(dismiss);

        Button? second = null;

        if (otherLabel is not null)
        {
            second = new Button
            {
                Content = otherLabel,
                Padding = new Thickness(12, 2, 12, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };

            DockPanel.SetDock(second, System.Windows.Controls.Dock.Right);
            row.Children.Add(second);
        }

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

        dismiss.Click += (_, _) =>
        {
            _notices.Children.Remove(notice);
            dismissed?.Invoke();
        };

        if (second is not null)
        {
            second.Click += (_, _) =>
            {
                _notices.Children.Remove(notice);
                other?.Invoke();
            };
        }

        _notices.Children.Add(notice);

        return () => _notices.Children.Remove(notice);
    }

    /// <summary>
    /// The folder report (Part 3, phase 3): its line, and - when it counts names - expandable to them,
    /// with the actions beside them: a missing entry is shown in the stock or deleted from it, a foreign
    /// file is shown in Explorer. Folded it reads as before; the names are built only when opened, so a
    /// folder of two thousand missing files costs nothing until somebody looks.
    /// </summary>
    private void ShowReport(FolderReport report)
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };

        body.Children.Add(new TextBlock { Text = report.Line, TextWrapping = TextWrapping.Wrap });

        if (report.HasNames)
        {
            var names = new Expander { Header = Strings.MainReportNames, Margin = new Thickness(0, 4, 0, 0) };

            names.Expanded += (_, _) => names.Content ??= Names(report);
            body.Children.Add(names);
        }

        Notice(body, dismissed: null);
    }

    /// <summary>The names behind a folder report, by kind, each with what can be done about it.</summary>
    private ScrollViewer Names(FolderReport report)
    {
        var list = new StackPanel();

        if (report.Missing.Count > 0)
        {
            list.Children.Add(Heading(Strings.MainReportMissing));

            foreach (var missing in report.Missing)
            {
                var show = Small(Strings.TileMenuShowInStock);
                var delete = Small(Strings.StockMenuDelete);

                show.Click += (_, _) =>
                {
                    if (Still(report))
                    {
                        ShowInStock(missing.Asset);
                    }
                };

                // The stock's own delete, with its question: the entry goes out of every scene and
                // layout too, and the report's button must not be a shorter way around that (Part 7).
                delete.Click += async (_, _) =>
                {
                    if (!Still(report))
                    {
                        return;
                    }

                    await _edits.DeleteAsync(this, [missing.Asset]).ConfigureAwait(true);

                    if (report.Campaign.Store.Entries.All(entry => entry.AssetId != missing.Asset.Value))
                    {
                        show.IsEnabled = false;
                        delete.IsEnabled = false;
                    }
                };

                list.Children.Add(Row(missing.Name, show, delete));
            }
        }

        if (report.Foreign.Count > 0)
        {
            list.Children.Add(Heading(Strings.MainReportForeign));

            foreach (var name in report.Foreign)
            {
                var explorer = Small(Strings.StockMenuShowFile);

                explorer.Click += (_, _) => ShowFile(report, name);
                list.Children.Add(Row(name, explorer));
            }
        }

        foreach (var (heading, names) in new[]
        {
            (Strings.MainReportOriginals, report.Originals),
            (Strings.MainReportSkipped, report.Skipped),
            (Strings.MainReportUnreadable, report.UnreadableScenes),
        })
        {
            if (names.Count == 0)
            {
                continue;
            }

            list.Children.Add(Heading(heading));

            foreach (var name in names)
            {
                list.Children.Add(Row(name));
            }
        }

        return new ScrollViewer { Content = list, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };

        static Button Small(string label) => new() { Content = label, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 1, 0, 1) };

        static DockPanel Row(string name, params Button[] actions)
        {
            var row = new DockPanel { LastChildFill = true };

            foreach (var action in actions.Reverse())
            {
                DockPanel.SetDock(action, System.Windows.Controls.Dock.Right);
                row.Children.Add(action);
            }

            row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = name });

            return row;
        }
    }

    /// <summary>
    /// Whether the report's campaign is still the open one. Its names mean nothing in the next one - a
    /// missing entry there may be another picture, a foreign file another folder's - so the box says so.
    /// </summary>
    private bool Still(FolderReport report)
    {
        if (ReferenceEquals(_desk.Open, report.Campaign))
        {
            return true;
        }

        _status.Text = Strings.MainReportOtherCampaign;

        return false;
    }

    /// <summary>A file the report names, selected in Explorer - where the DM keeps his notes, or finds what holds a file.</summary>
    private void ShowFile(FolderReport report, string name)
    {
        if (!Still(report))
        {
            return;
        }

        var file = System.IO.Path.Combine(report.Campaign.Directory, name);

        if (!System.IO.File.Exists(file))
        {
            _status.Text = Strings.MainReportFileGone(name);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
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

        _devices = new DevicesWindow(_session, _pairing, _desk, _settings, _address) { Owner = this };
        _devices.ScenesSaved += (_, _) => _scenes.Refresh();
        _devices.Show();

        return _devices;
    }

    /// <summary>The window Settings - one at a time, brought forward when it is open already.</summary>
    private void Settings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _ = _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _desk, _session, _log, _logger, _address.Port, _system, said => _status.Text = said) { Owner = this };
        _settingsWindow.Show();
    }

    /// <summary>
    /// The head's menu (Part 7): clear all and send to all, then - set apart - what has nothing to do
    /// with play: devices, the log, settings, where the two panels dock.
    /// </summary>
    private void OpenMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _panelHead.MenuButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        // Both one action without a question; undo brings them back (Part 7, from M5b6).
        menu.Items.Add(Item(Strings.MainClearAll, () => _ = _session.ClearAllAsync()));

        var chosen = _stock.Chosen;
        var toAll = Item(Strings.MainSendToAll, () => _ = _session.SendToAllAsync(chosen));

        toAll.IsEnabled = chosen.Count > 0;
        menu.Items.Add(toAll);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.MainDevices, () => Devices()));

        var log = new MenuItem { Header = Strings.MainLog, IsCheckable = true, IsChecked = _settings.Current.LogPanel is { Shown: true } };

        log.Click += (_, _) => PlaceLog(placement => placement with { Shown = log.IsChecked });
        menu.Items.Add(log);
        menu.Items.Add(Item(Strings.MainSettings, Settings));

        // The same question as the X - quitting asks wherever it starts (Part 7).
        menu.Items.Add(Item(Strings.MainQuit, Close));

        var docking = new MenuItem { Header = Strings.MainDockPanel };
        var side = _settings.Current.Panel?.Side ?? PanelSide.Right;

        foreach (var edge in Enum.GetValues<PanelSide>())
        {
            var item = new MenuItem { Header = EdgeLabel(edge), IsCheckable = true, IsChecked = edge == side };

            item.Click += (_, _) => Dock(edge, _settings.Current.Panel?.Size ?? (edge is PanelSide.Left or PanelSide.Right ? 380 : 300));
            docking.Items.Add(item);
        }

        docking.Items.Add(new Separator());

        var pinned = new MenuItem { Header = Strings.MainPinned, IsCheckable = true, IsChecked = _pinned };

        pinned.Click += (_, _) =>
        {
            _pinned = pinned.IsChecked;
            _settings.Update(current => current with
            {
                Panel = (current.Panel ?? new PanelPlacement(side, _overlaySize)) with { Pinned = _pinned },
            });
            Dock(side, _settings.Current.Panel?.Size ?? _overlaySize);
        };

        docking.Items.Add(pinned);

        menu.Items.Add(docking);

        // The campaign panel's edge is taken; docking the log is wanting to see it.
        var logDocking = new MenuItem { Header = Strings.MainDockLog };
        var logSide = LogSide();

        foreach (var edge in Enum.GetValues<PanelSide>())
        {
            var item = new MenuItem { Header = EdgeLabel(edge), IsCheckable = true, IsChecked = edge == logSide, IsEnabled = edge != _side };

            item.Click += (_, _) => PlaceLog(placement => placement with { Side = edge, Shown = true });
            logDocking.Items.Add(item);
        }

        menu.Items.Add(logDocking);

        // Beside the other ways the control is laid out, and here rather than in a tile's menu: it
        // acts on all tiles at once, and a screen's menu carries what concerns that one screen (Part 7).
        menu.Items.Add(Item(Strings.MainResetArrangement, _board.ResetArrangement));

        menu.IsOpen = true;
    }

    /// <summary>An edge in the menu's words - the enum's names are for files, not for people.</summary>
    private static string EdgeLabel(PanelSide edge) => edge switch
    {
        PanelSide.Left => Strings.MainEdgeLeft,
        PanelSide.Right => Strings.MainEdgeRight,
        PanelSide.Top => Strings.MainEdgeTop,
        PanelSide.Bottom => Strings.MainEdgeBottom,
        _ => edge.ToString(),
    };

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
        _stage.Children.Clear();
        _overlay.Children.Clear();

        var across = side is PanelSide.Left or PanelSide.Right;
        var panelFirst = side is PanelSide.Left or PanelSide.Top;
        var limit = Docking.PanelLimit(across ? ActualWidth : ActualHeight);
        var length = new GridLength(Docking.Length(size, limit));
        var star = new GridLength(1, GridUnitType.Star);

        _side = side;
        _overlaySize = length.Value;
        _panel.Side = side;

        if (!_pinned)
        {
            Retract(side, across, panelFirst, length.Value);
            Keep(side);
            Frame();
            return;
        }

        _panel.Width = double.NaN;
        _panel.Height = double.NaN;
        _panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        _panel.VerticalAlignment = VerticalAlignment.Stretch;
        _panel.RenderTransform = Transform.Identity;
        _panel.BorderThickness = default;
        _out = false;

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

        Keep(side);
        Frame();
    }

    /// <summary>
    /// The log panel at its edge beside the main room, or the main room alone (Part 7). <b>Never at the
    /// campaign panel's edge</b> - it goes to the opposite one when that edge is taken - and <b>the one
    /// shown last gives way</b>: it opens narrower rather than pressing the stage under its minimum.
    /// </summary>
    private void Frame()
    {
        _frame.Children.Clear();
        _frame.RowDefinitions.Clear();
        _frame.ColumnDefinitions.Clear();
        Grid.SetRow(_main, 0);
        Grid.SetColumn(_main, 0);

        if (_settings.Current.LogPanel is not { Shown: true } placement)
        {
            _frame.Children.Add(_main);
            return;
        }

        var side = LogSide();
        var across = side is PanelSide.Left or PanelSide.Right;
        var logFirst = side is PanelSide.Left or PanelSide.Top;
        var room = across ? _frame.ActualWidth : _frame.ActualHeight;
        var taken = _pinned && (_side is PanelSide.Left or PanelSide.Right) == across ? _overlaySize : 0;
        var limit = Docking.LogLimit(room, taken);
        var length = new GridLength(Docking.Length(placement.Size, limit));
        var star = new GridLength(1, GridUnitType.Star);

        GridLength[] lengths = logFirst ? [length, new GridLength(6), star] : [star, new GridLength(6), length];
        UIElement[] parts = logFirst ? [_logPanel, _logSplitter, _main] : [_main, _logSplitter, _logPanel];
        var at = logFirst ? 0 : 2;

        for (var index = 0; index < 3; index++)
        {
            if (across)
            {
                _frame.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = lengths[index],
                    MinWidth = index == at ? 160 : 0,
                    MaxWidth = index == at ? limit : double.PositiveInfinity,
                });
                Grid.SetColumn(parts[index], index);
                Grid.SetRow(parts[index], 0);
            }
            else
            {
                _frame.RowDefinitions.Add(new RowDefinition
                {
                    Height = lengths[index],
                    MinHeight = index == at ? 160 : 0,
                    MaxHeight = index == at ? limit : double.PositiveInfinity,
                });
                Grid.SetRow(parts[index], index);
                Grid.SetColumn(parts[index], 0);
            }

            _frame.Children.Add(parts[index]);
        }

        _logSplitter.ResizeDirection = across ? GridResizeDirection.Columns : GridResizeDirection.Rows;

        if (side != placement.Side)
        {
            _settings.Update(current => current with { LogPanel = placement with { Side = side } });
        }
    }

    /// <summary>The log panel's edge: its own, unless the campaign panel lies there - then the opposite one.</summary>
    private PanelSide LogSide() => Docking.LogSide(_settings.Current.LogPanel?.Side, _side);

    /// <summary>Changes the log panel's placement, kept globally, and lays the frame anew.</summary>
    private void PlaceLog(Func<LogPlacement, LogPlacement> change)
    {
        var side = LogSide();

        _settings.Update(current => current with
        {
            LogPanel = change(current.LogPanel ?? new LogPlacement(side, side is PanelSide.Left or PanelSide.Right ? 480 : 260, Shown: false)),
        });
        Frame();
    }

    private void LogResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var size = LogSide() is PanelSide.Left or PanelSide.Right ? _logPanel.ActualWidth : _logPanel.ActualHeight;

        PlaceLog(placement => placement with { Size = size });
    }

    /// <summary>
    /// The retracted arrangement: the tab at the edge, the stage taking the rest, and the panel lying
    /// over the stage at its full size, pushed out of sight until it is wanted (Part 7).
    /// </summary>
    private void Retract(PanelSide side, bool across, bool panelFirst, double length)
    {
        var tabLength = GridLength.Auto;
        var star = new GridLength(1, GridUnitType.Star);

        _stage.Children.Add(_board);
        _stage.Children.Add(_overlay);
        _overlay.Children.Add(_panel);

        _panel.Width = across ? length : double.NaN;
        _panel.Height = across ? double.NaN : length;
        _panel.HorizontalAlignment = side switch
        {
            PanelSide.Left => HorizontalAlignment.Left,
            PanelSide.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch,
        };
        _panel.VerticalAlignment = side switch
        {
            PanelSide.Top => VerticalAlignment.Top,
            PanelSide.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Stretch,
        };

        // A line on the side that faces the stage, so the panel lying over it has an edge.
        _panel.BorderBrush = SystemColors.ControlDarkBrush;
        _panel.BorderThickness = side switch
        {
            PanelSide.Left => new Thickness(0, 0, 1, 0),
            PanelSide.Right => new Thickness(1, 0, 0, 0),
            PanelSide.Top => new Thickness(0, 0, 0, 1),
            _ => new Thickness(0, 1, 0, 0),
        };
        _panel.RenderTransform = _slide;

        // The tab reads along the edge it stands at.
        _edgeLabel.LayoutTransform = across ? new RotateTransform(side is PanelSide.Left ? -90 : 90) : Transform.Identity;
        _edgeTab.Content = _edgeLabel;
        _edgeTab.Padding = across ? new Thickness(4, 12, 4, 12) : new Thickness(12, 4, 12, 4);
        _edgeTab.Margin = side switch
        {
            PanelSide.Left => new Thickness(0, 0, 6, 0),
            PanelSide.Right => new Thickness(6, 0, 0, 0),
            PanelSide.Top => new Thickness(0, 0, 0, 6),
            _ => new Thickness(0, 6, 0, 0),
        };

        GridLength[] lengths = panelFirst ? [tabLength, star] : [star, tabLength];
        UIElement[] parts = panelFirst ? [_edgeTab, _stage] : [_stage, _edgeTab];

        for (var index = 0; index < 2; index++)
        {
            if (across)
            {
                _main.ColumnDefinitions.Add(new ColumnDefinition { Width = lengths[index] });
                Grid.SetColumn(parts[index], index);
                Grid.SetRow(parts[index], 0);
            }
            else
            {
                _main.RowDefinitions.Add(new RowDefinition { Height = lengths[index] });
                Grid.SetRow(parts[index], index);
                Grid.SetColumn(parts[index], 0);
            }

            _main.Children.Add(parts[index]);
        }

        Slide(open: false, animate: false);
    }

    /// <summary>
    /// Pushes the retracted panel out over the stage or back out of sight. <b>Moved, never
    /// collapsed</b>: a carry out of the panel begins with the panel going back, and an element that
    /// collapses takes the hand's capture with it - the carry would end the moment it began.
    /// </summary>
    private void Slide(bool open, bool animate = true)
    {
        _out = open;

        var away = _overlaySize + 24;
        var (x, y) = open
            ? (0.0, 0.0)
            : _side switch
            {
                PanelSide.Left => (-away, 0.0),
                PanelSide.Right => (away, 0.0),
                PanelSide.Top => (0.0, -away),
                _ => (0.0, away),
            };

        var duration = animate ? TimeSpan.FromMilliseconds(140) : TimeSpan.Zero;

        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration));
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration));
    }

    /// <summary>
    /// The retracted panel's grips (Part 7): a tap on the tab opens and closes it, a tap beside it or
    /// a swipe toward the edge closes it - nowhere is hovering needed, because a finger cannot hover.
    /// </summary>
    private void WireRetracting()
    {
        _edgeTab.Click += (_, _) => Slide(!_out);

        // The tap beside it closes and does nothing else: it was meant for the panel, and a picture
        // taken hold of by the same tap would move under a hand that only wanted the panel gone.
        _board.PreviewMouseLeftButtonDown += (_, down) =>
        {
            if (!_pinned && _out && !_carry.Carrying)
            {
                Slide(open: false);
                down.Handled = true;
            }
        };

        _board.PreviewTouchDown += (_, down) =>
        {
            if (!_pinned && _out && !_carry.Carrying)
            {
                Slide(open: false);
                down.Handled = true;
            }
        };

        _panel.SwipedIn += (_, _) =>
        {
            if (!_pinned && _out)
            {
                Slide(open: false);
            }
        };
    }

    /// <summary>
    /// Carrying in both directions (Part 7). Out of the panel onto a tile: a picture where it was let
    /// go, the background on the strip, a scene loaded. Off a tile into the panel - or onto the tab of a
    /// retracted one, which does not open for it - the picture leaves the screen and stays in the
    /// stock. Deleting is a different act with its own question; the fastest gesture must not be the
    /// most final one.
    /// </summary>
    private void WireCarrying()
    {
        _carry.Began += (_, _) =>
        {
            // Out of the way of the tile the DM is aiming at.
            if (!_pinned && _out)
            {
                Slide(open: false);
            }

            // A layout lights the whole stage; everything else shows where a background would land.
            if (_carry.Load?.Layout is not null)
            {
                _board.Glow(on: true);
            }
            else
            {
                _board.Strips(showing: true);
            }
        };

        _carry.Moved += (_, at) =>
        {
            if (_carry.Load?.Layout is null)
            {
                _ = _board.Aim(at);
            }
        };
        _carry.Ended += (_, _) =>
        {
            _board.Strips(showing: false);
            _board.Glow(on: false);
        };
        _carry.Dropped += async (_, dropped) => await LandAsync(dropped.Load, dropped.At).ConfigureAwait(true);

        _board.CarriedAway += async (_, away) =>
        {
            if (OverPanel(away.At))
            {
                await _session.RemoveItemAsync(away.Screen, away.Item).ConfigureAwait(true);
            }
        };

        _board.ShowingInStock += (_, asset) => ShowInStock(asset);
        _board.Renaming += async (_, asset) => await _edits.RenameAsync(this, asset).ConfigureAwait(true);

        // A rename or a delete reaches the stock, the scenes, the layouts and the marks.
        _edits.Changed += (_, _) =>
        {
            _panel.Refresh();
            _scenes.Refresh();
            _layouts.Refresh();
            _ = _stock.CountAsync();
        };
    }

    private async Task LandAsync(CarryLoad load, System.Windows.Point at)
    {
        // A layout asks only whether it is over the stage, anything else only which tile is under the
        // hand - asking the stage lights the strip it finds.
        var landing = load.Layout is not null
            ? CarryLanding.Decide(load, Hits(_board, at), aim: null)
            : CarryLanding.Decide(load, overStage: false, _board.Aim(at));

        // Where the DM just put something is where the next blind grip lands (Part 7).
        if (landing is CarryLanding.Landing.OnScreen on)
        {
            _board.Activate(on.Screen);
        }

        switch (landing)
        {
            case CarryLanding.Landing.Layout layout:
                await _layouts.LoadAsync(layout.Name, _settings.Current.LayoutLoad).ConfigureAwait(true);
                break;

            case CarryLanding.Landing.Scene scene:
                await _scenes.LoadAsync(scene.Name, scene.Screen, _settings.Current.SceneLoad).ConfigureAwait(true);
                break;

            case CarryLanding.Landing.BackgroundRefused:
                _status.Text = Strings.MainBackgroundOnePicture;
                break;

            case CarryLanding.Landing.Background background:
                await _session.SetBackgroundAsync(background.Screen, background.Asset).ConfigureAwait(true);
                break;

            case CarryLanding.Landing.Picture picture:
                _ = await _session.AddItemAsync(picture.Screen, picture.Asset, picture.At).ConfigureAwait(true);
                break;

            case CarryLanding.Landing.Pictures pictures:
                _ = await _session.AddItemsAsync(pictures.Screen, pictures.Assets).ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    /// <summary>Whether a place on the screen lies on the panel, where it can be seen, or on the tab of a retracted one.</summary>
    private bool OverPanel(System.Windows.Point absolute) =>
        (_pinned || _out) && Hits(_panel, absolute) || (!_pinned && Hits(_edgeTab, absolute));

    private static bool Hits(FrameworkElement element, System.Windows.Point absolute)
    {
        if (!element.IsVisible || !element.IsLoaded)
        {
            return false;
        }

        var local = element.PointFromScreen(absolute);

        return local.X >= 0 && local.Y >= 0 && local.X <= element.ActualWidth && local.Y <= element.ActualHeight;
    }

    /// <summary>
    /// The open campaign in the head and on the tab of a retracted panel - and with none open and the
    /// campaigns folder out of reach, why the switcher will be empty (Part 3).
    /// </summary>
    private void ShowCampaign()
    {
        var unreachable = _desk.Open is null && !_desk.Reachable ? Strings.CampaignsUnreachable(_desk.BasePath) : null;

        _panelHead.Campaign(_desk.OpenLabel, unreachable);
        _edgeLabel.Text = _desk.OpenLabel ?? Strings.MainNoCampaign;
    }

    /// <summary>"Show in stock" from a tile: the panel comes out if it was retracted, and narrows to the picture.</summary>
    private void ShowInStock(AssetId asset)
    {
        if (_desk.Open?.Store.Entries.FirstOrDefault(entry => entry.AssetId == asset.Value) is not { } entry)
        {
            _status.Text = Strings.MainNotInStock;
            return;
        }

        if (!_pinned && !_out)
        {
            Slide(open: true);
        }

        _panel.ShowInStock(asset, entry.Name);
    }

    /// <summary>Whether any device is connected - "Release the screens" is checked only then.</summary>
    private bool _connected;

    /// <summary>Where quitting stands: nobody asked, the question or its work is under way, or it is done.</summary>
    private readonly QuitGate _quit = new();

    /// <summary>The window has closed - a quit whose work was still running has nothing left to close.</summary>
    private bool _closed;

    /// <summary>Windows is ending the session: no question then - nobody is there to answer it (Part 7).</summary>
    internal bool SessionEnding { get; set; }

    /// <summary>
    /// The X asks instead of deciding (Part 7): quit, with "Save layout" and "Release the screens", or
    /// cancel. This close is always refused; the question is asked AFTER it, and the window closes for
    /// real once the work of the answer is done.
    /// <para>
    /// <b>Never asked inside this event.</b> WPF refuses a <c>Close</c> - and a <c>Shutdown</c>, which
    /// closes every window - while a window is still in its own <c>Closing</c>, and throws. Asked in
    /// here, the second close came while the first was still running whenever the answer's work
    /// finished without yielding: nothing ticked, "Save layout" (an uncontended scene read), "Release"
    /// with no device left. Every quit then ended as an unhandled fault, without <c>Closed</c> and
    /// without <c>OnExit</c>; and the end of a Windows session during the open question did the same
    /// (M5 review, measured with a minimal WPF program).
    /// </para>
    /// <para>
    /// <b>One question at a time</b>: an X while the question or its work is under way is refused and
    /// asks nothing - otherwise a second dialog, a second <c>GoIdle</c> and a second layout under the
    /// same stamp could follow while the first waited for the devices (Part 7, quitting asks once).
    /// </para>
    /// </summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var verdict = _quit.Closing(SessionEnding);

        if (verdict is QuitGate.Verdict.Close)
        {
            return;
        }

        e.Cancel = true;

        if (verdict is QuitGate.Verdict.Ask)
        {
            Dispatcher.BeginInvoke(QuitAsked);
        }
    }

    /// <summary>
    /// The question behind the X, outside the <c>Closing</c> that asked for it (see <see cref="OnClosing"/>).
    /// The work of the answer is done first - the layout written, <c>GoIdle</c> out to the devices -
    /// then the window closes.
    /// </summary>
    private async void QuitAsked()
    {
        if (_closed || QuitDialog.Ask(this, _desk.Open is not null, _connected) is not { } answer || _closed)
        {
            // Cancelled - or the window went meanwhile, at the end of a Windows session.
            _quit.Cancelled();
            return;
        }

        if (answer.SaveLayout)
        {
            _ = await _layouts.SaveOnQuitAsync(this).ConfigureAwait(true);
        }

        if (answer.ReleaseScreens)
        {
            await _session.GoIdleAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(true);
        }

        if (_closed)
        {
            return;
        }

        _quit.Answered();
        Close();
    }

    /// <summary>Whether the last campaign has been offered in this run - once is enough.</summary>
    private bool _offered;

    /// <summary>Takes the offer away again - a campaign opened by any way answers it.</summary>
    private Action? _withdrawOffer;

    /// <summary>
    /// After a restart the hub takes the table over from the displays, and with no campaign open the
    /// thumbnails show placeholders with names (Part 4). The campaign those pictures come from is
    /// the one open last - so the box beside the stage names it and offers to open it, once per run.
    /// A campaign that is gone from the list is not offered.
    /// </summary>
    private async Task OfferLastCampaignAsync()
    {
        if (_offered || _desk.Open is not null || _settings.Current.LastCampaign is not { } last)
        {
            return;
        }

        if (_desk.List().FirstOrDefault(summary => string.Equals(summary.Name, last, StringComparison.OrdinalIgnoreCase)) is not { } known)
        {
            return;
        }

        foreach (var view in _board.Screens)
        {
            var scene = await _session.GetSceneAsync(view.Screen, _closing).ConfigureAwait(true);

            if (!ShowsPictures(scene))
            {
                continue;
            }

            if (_offered || _desk.Open is not null)
            {
                return;
            }

            _offered = true;
            _withdrawOffer = Notify(
                Strings.MainTableFromCampaign(CampaignDesk.Label(known)),
                dismissed: null,
                Strings.MainOpenCampaign,
                async () =>
                {
                    if (await _desk.SwitchAsync(known.Name).ConfigureAwait(true) is { } refused)
                    {
                        _status.Text = refused;
                    }
                });

            return;
        }
    }

    /// <summary>
    /// Whether a scene shows pictures - what the last-campaign offer is about. <b>A free text is not
    /// one</b>: it needs no stock, so it can be laid with no campaign open, and "the table shows
    /// pictures from X" would then be said of a table that shows nothing from X (M5b8c: after a
    /// takeover). A picture with no campaign open can only have come with the table.
    /// </summary>
    internal static bool ShowsPictures(SceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return scene.Background is not null || scene.Items.Any(item => item is ImageItem);
    }

    /// <summary>The triggers of the stock notice that already spoke in this run - once per start (Part 7).</summary>
    private readonly HashSet<StockTrigger> _spoken = [];

    /// <summary>
    /// The stock reporting itself, in the box beside the stage, under the report of the opening it
    /// belongs to (Part 7). <b>"OK" acknowledges</b> the value it was told at - it comes again only a
    /// step worse; <b>"Don't report again"</b> switches that one trigger off, the other stays.
    /// </summary>
    private void Report(StockMeasure measure)
    {
        if (_desk.Open is not { } open)
        {
            return;
        }

        foreach (var trigger in _settings.Current.Stock.Due(measure.FreeBytes, measure.Pictures, open.Name, _spoken))
        {
            _spoken.Add(trigger);

            var value = trigger is StockTrigger.FreeSpace ? measure.FreeBytes : measure.Pictures;

            ControlLog.StockNoticed(_logger, open.Name, trigger, value);

            var text = trigger is StockTrigger.FreeSpace
                ? Strings.MainStockFreeSpace(measure.FreeBytes / (1024d * 1024 * 1024))
                : Strings.MainStockPictures(_desk.OpenLabel ?? open.Name, measure.Pictures, _settings.Current.Stock.PicturesAbove);

            Notify(
                text,
                dismissed: () => _settings.Update(current => current with
                {
                    Stock = trigger is StockTrigger.FreeSpace
                        ? current.Stock with { FreeSpaceAcknowledged = measure.FreeBytes }
                        : current.Stock.AcknowledgePictures(open.Name, measure.Pictures),
                }),
                Strings.MainStockDontReport,
                () => _settings.Update(current => current with
                {
                    Stock = trigger is StockTrigger.FreeSpace
                        ? current.Stock with { FreeSpaceOn = false }
                        : current.Stock with { PicturesOn = false },
                }));
        }
    }

    /// <summary>
    /// "Verify stock" offered once for a campaign that a move of the campaign folder copied (Part 7: a
    /// copy is checked by number and size; whoever wants to know exactly is offered the verify) - at its
    /// next opening, in the box beside the stage, below its report. The button runs the verify of the
    /// open campaign in the tidy-up view, which has the progress and the way to break it off.
    /// </summary>
    private void OfferVerify()
    {
        if (_desk.Open is not { } open || !_settings.Current.Stock.VerifyDue(open.Name))
        {
            return;
        }

        _settings.Update(current => current with { Stock = current.Stock.VerifyOffered(open.Name) });

        Notify(
            Strings.MainVerifyAfterMove(_desk.OpenLabel ?? open.Name),
            dismissed: null,
            Strings.TidyVerify,
            () =>
            {
                if (!ReferenceEquals(_desk.Open, open))
                {
                    _status.Text = Strings.MainReportOtherCampaign;
                    return;
                }

                Tidy();
                _ = _tidy?.VerifyAsync();
            });
    }

    /// <summary>The tidy-up view of the open campaign - one at a time, brought forward when it is open already.</summary>
    private void Tidy()
    {
        if (_tidy is { IsLoaded: true })
        {
            _ = _tidy.Activate();
            return;
        }

        _tidy = new TidyWindow(this, _desk, ShowInStock);
        _tidy.Show();
    }

    private TidyWindow? _tidy;

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

    /// <summary>Takes back the last act, and says which - the button said it before, the status line confirms it.</summary>
    private async Task UndoAsync()
    {
        var label = await _session.UndoAsync().ConfigureAwait(true);

        _status.Text = label is null ? Strings.MainNothingToUndo : Strings.MainUndone(label);
    }

    /// <summary>Does the last undone act again.</summary>
    private async Task RedoAsync()
    {
        var label = await _session.RedoAsync().ConfigureAwait(true);

        _status.Text = label is null ? Strings.MainNothingToRedo : Strings.MainRedone(label);
    }

    private static MenuItem Item(string header, Action chosen)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => chosen();

        return item;
    }
}
