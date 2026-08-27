using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DnDOverlay.Control;

/// <summary>
/// The grips M2b and M2c need: get a picture in through any of the four entrances, put it on a
/// screen or behind it, switch the two layers, name an item, hold an animation still, take one
/// away.
/// <para>
/// <b>Scaffolding, and it says so.</b> The DM surface of Part 7 - tiles, stage, inventory - starts
/// in M4, and the inventory as a GRID with search, sorting and multi-select is M5b. What this
/// exists for is that every command can be reached by hand, because otherwise a hand-run has
/// nothing to press.
/// </para>
/// <para>
/// <b>It grew for M2c rather than being replaced by half an inventory.</b> The four entrances need
/// bodies - a drop target, a paste, an address field, a multiple selection - and each of them is
/// three lines here. A preliminary inventory would instead have had to answer four questions that
/// belong to M5b (docking, touch drag against scrolling, the selection model, the zoom steps), and
/// would then be either replaced or defended.
/// </para>
/// <para>
/// The price is named: check step 29b asks the inventory to filter and highlight a picture that is
/// already there. That half cannot be shown here, and it is not claimed - what M2c proves is the
/// half that carries: no second entry, and the name the DM gave it stays.
/// </para>
/// </summary>
internal sealed class StagePanel : StackPanel
{
    private readonly ISessionApi _session;
    private readonly Entrances _entrances;
    private readonly Func<ScreenRef?> _target;
    private readonly TextBlock _status;
    private readonly ILogger _logger;
    private readonly TextBox _address = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0) };

    private readonly ListBox _items = new() { MinHeight = 90, Margin = new Thickness(0, 0, 0, 8) };

    /// <summary>
    /// How many entries the list shows before it starts to scroll.
    /// <para>
    /// It grows inside a <see cref="StackPanel"/>, which offers its children all the height they
    /// ask for - so a screen carrying seven hundred pictures made a list seven hundred rows tall and
    /// pushed everything below it, the grips for the very items it was listing, out of the window.
    /// Found at the table (hand-run of M3b, step 0.5).
    /// </para>
    /// </summary>
    private const int VisibleItems = 10;

    /// <summary>At most one measurement waiting for the layout, so a hidden panel cannot spin.</summary>
    private bool _capping;
    private readonly CheckBox _images = new() { Content = "Images", Margin = new Thickness(0, 0, 16, 0), IsChecked = true };
    private readonly CheckBox _background = new() { Content = "Background", IsChecked = true };

    private readonly ComboBox _placement = new()
    {
        Width = 130,
        Margin = new Thickness(0, 0, 8, 0),
        ItemsSource = Enum.GetValues<PlacementMode>(),
    };

    /// <summary>
    /// The one grip that stays alive while a run is going, and therefore the one that cannot live
    /// inside the block that is switched off. Check step 29a asks for two hundred files to be broken
    /// off in the middle - the library has carried that promise since M2c and there was nothing to
    /// press, so the step could not be walked at all.
    /// </summary>
    private readonly StackPanel _breakOff = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 8),
        Visibility = Visibility.Collapsed,
    };

    private bool _settingSwitches;

    /// <summary>Alive exactly while a run is going, and the reason a second one cannot start.</summary>
    private CancellationTokenSource? _running;

    internal StagePanel(
        ISessionApi session,
        Entrances entrances,
        Func<ScreenRef?> target,
        TextBlock status,
        ILogger logger)
    {
        _session = session;
        _entrances = entrances;
        _target = target;
        _status = status;
        _logger = logger;

        Margin = new Thickness(0, 12, 0, 0);

        // The drop target is the panel itself. It filters no extension - it takes what it is given
        // and the ingest decides what it was, or a .rptok would be locked out by the very layer
        // that is meant to let it in (Part 7).
        AllowDrop = true;
        Drop += (_, e) => DroppedAsync(e);
        DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };

        Children.Add(Heading("Stage"));

        _breakOff.Children.Add(Button("Break off", (_, _) => _running?.Cancel()));
        Children.Add(_breakOff);

        Children.Add(Row(
            Button("Send a picture ...", (_, _) => ChosenAsync(background: false)),
            Button("Set as background ...", (_, _) => ChosenAsync(background: true)),
            Button("Clear background", (_, _) => Run(screen => _session.ClearBackgroundAsync(screen)))));

        // The other three entrances. A drop needs no button - the whole panel is one.
        Children.Add(Row(
            Button("Paste", (_, _) => PastedAsync(background: false)),
            Button("Paste as background", (_, _) => PastedAsync(background: true)),
            new TextBlock
            {
                Text = "... or drop files, a picture or a link anywhere here",
                Margin = new Thickness(4, 6, 0, 0),
            }));

        Children.Add(Row(
            new TextBlock { Text = "Address", Margin = new Thickness(0, 6, 8, 0) },
            _address,
            Button("Fetch", (_, _) => FetchedAsync(background: false))));

        Children.Add(Row(
            Button("Cover", (_, _) => Run(screen => _session.SetBackgroundFitAsync(screen, BackgroundFit.Cover))),
            Button("Contain", (_, _) => Run(screen => _session.SetBackgroundFitAsync(screen, BackgroundFit.Contain))),
            Button("Hold/run background", (_, _) => HoldBackgroundAsync())));

        _images.Click += (_, _) => Switch(_images, visible => _session.ToggleItemsAsync(Screen()!.Value, visible));
        _background.Click += (_, _) => Switch(_background, visible => _session.ToggleBackgroundAsync(Screen()!.Value, visible));

        Children.Add(Row(_images, _background));

        // The placement mode is a SCREEN SETTING and travels the ordinary way, through the
        // configuration - not as a scene operation. A grip for it belongs here because step 16
        // asks for Flow and Cascade side by side, and there was no way to switch (Part 6).
        _placement.SelectionChanged += (_, _) => Place();

        Children.Add(Row(new TextBlock { Text = "Placement", Margin = new Thickness(0, 5, 8, 0) }, _placement));

        Children.Add(Heading("Items on this screen"));
        Children.Add(_items);
        Children.Add(Row(
            Button("Remove", (_, _) => WithItem(item => _session.RemoveItemAsync(Screen()!.Value, item.ItemId))),
            Button("Name on/off", (_, _) => WithItem(item =>
                _session.SetShowNameAsync(Screen()!.Value, item.ItemId, !item.ShowName))),
            Button("Hold/run animation", (_, _) => WithItem(item =>
                _session.SetAnimationPausedAsync(Screen()!.Value, item.ItemId, !item.AnimationPaused)))));

        // The three commands M3a built. They had no caller outside the hub's own inbound path, and a
        // step counts as closed when a CALLER is named rather than a test (Guide C12) - besides
        // which the hand-run has to be able to lock something in order to find out that the table
        // will not move it (Part 11, step 21).
        Children.Add(Row(
            Button("Lock/unlock", (_, _) => WithItem(item =>
                _session.SetLockedAsync(Screen()!.Value, item.ItemId, !item.Locked))),
            Button("Unlock all", (_, _) => Run(screen => _session.UnlockAllAsync(screen))),
            Button("Park/unpark", (_, _) => WithItem(item =>
                _session.ParkItemAsync(Screen()!.Value, item.ItemId, !item.Parked)))));
    }

    /// <summary>
    /// Redraws from the authoritative scene. Called whenever a patch arrives, so what stands here
    /// is what the hub holds and never a second copy kept in step by hand (rule 1).
    /// </summary>
    internal async Task RefreshAsync()
    {
        // A gesture at the table arrives about twenty times a second, and each patch asks for a
        // redraw. Coalescing rather than queueing: whoever asks while one is running gets the ONE
        // that follows it, so the list always ends up showing the last state without twenty
        // rebuilds a second going through the hub - the control has to stay usable while the table
        // is busy (Part 1, order of precedence; Part 11, step 17).
        if (_redrawing)
        {
            _again = true;

            return;
        }

        _redrawing = true;

        try
        {
            await DrawAsync().ConfigureAwait(true);

            while (_again)
            {
                _again = false;
                await DrawAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _redrawing = false;
        }
    }

    private async Task DrawAsync()
    {
        if (Screen() is not { } screen)
        {
            _items.Items.Clear();
            return;
        }

        var scene = await _session.GetSceneAsync(screen).ConfigureAwait(true);

        // Guarded, because setting IsChecked raises Click's sibling events on some inputs and the
        // panel would then send the command it is merely displaying.
        _settingSwitches = true;
        _images.IsChecked = scene.ItemsVisible;
        _background.IsChecked = scene.BackgroundVisible;
        _settingSwitches = false;

        var selected = _items.SelectedIndex;

        _items.Items.Clear();

        foreach (var item in scene.Items.OfType<ImageItem>())
        {
            _items.Items.Add(new ItemEntry(item));
        }

        _items.SelectedIndex = selected >= 0 && selected < _items.Items.Count ? selected : 0;

        CapToVisibleItems();
    }

    /// <summary>
    /// Holds the list to <see cref="VisibleItems"/> rows and lets its own scrollbar take the rest.
    /// <para>
    /// The height is MEASURED off the first row rather than written down in device-independent
    /// pixels: the row follows the font and the font follows the system's text size, so a number
    /// here would be ten rows on this machine and six on the next one.
    /// </para>
    /// </summary>
    private void CapToVisibleItems() => CapToVisibleItems(mayWaitForLayout: true);

    private void CapToVisibleItems(bool mayWaitForLayout)
    {
        if (_items.Items.Count <= VisibleItems)
        {
            // Short enough to stand whole. Left uncapped so it shrinks back with its content.
            _items.MaxHeight = double.PositiveInfinity;
            return;
        }

        if (_items.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement { ActualHeight: > 0 } row)
        {
            _items.MaxHeight = (row.ActualHeight * VisibleItems) + Chrome();
            return;
        }

        if (mayWaitForLayout && !_capping)
        {
            // The rows are made during layout, so there is nothing to measure yet. Once.
            _capping = true;

            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                () =>
                {
                    _capping = false;
                    CapToVisibleItems(mayWaitForLayout: false);
                });

            return;
        }

        // The layout has been asked and produced nothing to measure - the panel is not on the
        // screen. An estimate from the font caps it anyway, because a list that is only capped when
        // it happens to be visible is not capped at all.
        _items.MaxHeight = (_items.FontFamily.LineSpacing * _items.FontSize * VisibleItems) + Chrome();
    }

    private double Chrome() =>
        _items.Padding.Top + _items.Padding.Bottom
        + _items.BorderThickness.Top + _items.BorderThickness.Bottom;

    private bool _redrawing;
    private bool _again;

    private ScreenRef? Screen() => _target();

    /// <summary>
    /// The background's own pause. It needs its own grip because the item buttons act on the
    /// selected item, and the background is not one - the operation takes a null item for exactly
    /// this (Part 7).
    /// </summary>
    private async void HoldBackgroundAsync()
    {
        if (Screen() is not { } screen)
        {
            _status.Text = "Pick a screen first.";
            return;
        }

        var scene = await _session.GetSceneAsync(screen).ConfigureAwait(true);

        if (scene.Background is not { } background)
        {
            _status.Text = "This screen has no background.";
            return;
        }

        await _session
            .SetAnimationPausedAsync(screen, item: null, !background.AnimationPaused)
            .ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    private async void Place()
    {
        if (_settingSwitches || Screen() is not { } screen || _placement.SelectedItem is not PlacementMode mode)
        {
            return;
        }

        await _session.ApplyConfigAsync(
            screen.Device,
            new ConfigUpdate([new ScreenConfigUpdate(screen.Screen, new ScreenSettings(Placement: mode))]))
            .ConfigureAwait(true);

        _status.Text = $"{screen.Screen} places new pictures in {mode}.";
    }

    /// <summary>
    /// Takes the grips away while a picture is being taken in, and puts the break-off in their
    /// place. Not decoration: the step costs seconds, and a control that looks idle invites a second
    /// picture to be started into the middle of the first.
    /// <para>
    /// Every child EXCEPT the break-off, rather than the panel itself. WPF coerces
    /// <see cref="UIElement.IsEnabled"/> down the tree, so a button under a disabled panel cannot be
    /// enabled again - and a run with no way out is what check step 29a could not walk.
    /// </para>
    /// </summary>
    private void Grips(bool enabled)
    {
        foreach (UIElement child in Children)
        {
            child.IsEnabled = enabled || ReferenceEquals(child, _breakOff);
        }

        _breakOff.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Entrance one: files, chosen. Several at a time, because that is the ordinary case.</summary>
    private async void ChosenAsync(bool background)
    {
        var dialog = new OpenFileDialog
        {
            Title = background ? "Picture for the background layer" : "Pictures for the table",
            Multiselect = !background,
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.avif;*.tif;*.tiff;*.psd;*.rptok|All files|*.*",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) is true)
        {
            await TakeInAsync(_entrances.FromFiles(dialog.FileNames), background).ConfigureAwait(true);
        }
    }

    /// <summary>Entrances two and three: a screenshot, or a fragment out of a browser.</summary>
    private async void PastedAsync(bool background)
    {
        IDataObject? clipboard;

        try
        {
            clipboard = System.Windows.Clipboard.GetDataObject();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard belongs to whoever last wrote to it and can refuse to be read at any
            // moment. A paste that cannot be read is an empty paste, never a crash.
            _status.Text = "The clipboard could not be read just now - try again.";
            return;
        }

        var sources = clipboard is null ? [] : _entrances.FromDataObject(clipboard);

        if (sources.Count == 0)
        {
            _status.Text = "There is no picture and no address in the clipboard.";
            return;
        }

        await TakeInAsync(sources, background).ConfigureAwait(true);
    }

    /// <summary>Entrance four: an address, led as tightly as Part 5 requires.</summary>
    private async void FetchedAsync(bool background)
    {
        var typed = _address.Text?.Trim();

        if (string.IsNullOrEmpty(typed))
        {
            _status.Text = "Put an address in the field first.";
            return;
        }

        await TakeInAsync(
            _entrances.FromDataObject(new DataObject(DataFormats.UnicodeText, typed)), background)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// A drop, read in the SAME format order as a paste (Part 7). Built as a plain file drop, a
    /// link dragged out of a chat window or an address bar would fall through silently.
    /// </summary>
    private async void DroppedAsync(DragEventArgs e)
    {
        var sources = _entrances.FromDataObject(e.Data);

        if (sources.Count == 0)
        {
            _status.Text = "Nothing in that drop could be taken in.";
            return;
        }

        await TakeInAsync(sources, background: false).ConfigureAwait(true);
    }

    /// <summary>
    /// The one path every entrance ends on: take in, then show. One picture or two hundred, the
    /// same way - a single paste is simply a run with one source in it, over in a blink (Part 7).
    /// </summary>
    private async Task TakeInAsync(IReadOnlyList<IntakeSource> sources, bool background)
    {
        if (Screen() is not { } screen)
        {
            _status.Text = "Pick a screen first.";
            return;
        }

        if (sources.Count == 0)
        {
            return;
        }

        // A run is already going. The grips are off, but a drop lands on the panel itself and the
        // panel has to stay alive to carry the break-off - so the guard is here rather than implied
        // by a disabled control.
        if (_running is not null)
        {
            _status.Text = "A run is going. Break it off first, or wait for it.";
            return;
        }

        // Taking pictures in costs SECONDS and the DM has to be able to see that it is happening.
        // Measured with the real files (hand-run of M2b, second round, step 17): a 24 MB PNG at
        // 4616x6000 spent 11.6 s being normalised before the pass-through, and 1.2 s after. A line
        // of text alone was there and was missed - so the grips go dead as well, which is the part
        // that cannot be overlooked.
        using var running = new CancellationTokenSource();

        _running = running;
        Grips(enabled: false);

        var clock = System.Diagnostics.Stopwatch.StartNew();

        IntakeReport report;

        try
        {
            var progress = new Progress<IntakeProgress>(step => _status.Text = Progressing(step));

            // OFF the UI thread. The stock's ingest is synchronous by construction - it hashes,
            // decodes, normalises, writes and thumbnails - and awaiting it here froze the whole
            // control until a 20 MB picture was through (hand-run of M2b, step 17). A library
            // returns a result and does not pick a thread; picking one is the caller's job
            // (rule 10).
            report = await Task.Run(() => _entrances.TakeInAsync(sources, progress, running.Token))
                .ConfigureAwait(true);
        }
        finally
        {
            _running = null;
            Grips(enabled: true);
        }

        ControlLog.IntakeFinished(
            _logger,
            sources.Count,
            clock.ElapsedMilliseconds,
            report.Taken.Count,
            report.AlreadyPresent.Count,
            report.Refused.Count,
            report.Cancelled ? ", broken off" : string.Empty);

        // One line per picture that was really taken in, each with what IT cost - the run's total
        // above cannot say which of two hundred took six of the eight seconds. Written only for
        // the newly stocked: an already-present picture was not taken in, and 2003 counts it.
        foreach (var taken in report.Taken)
        {
            ControlLog.AssetTakenIn(
                _logger,
                taken.Asset.Name,
                taken.Asset.AssetId.Value,
                taken.Asset.Meta.PixelWidth,
                taken.Asset.Meta.PixelHeight,
                taken.Asset.Meta.Bytes,
                taken.Milliseconds);
        }

        foreach (var failure in report.Refused)
        {
            // Named and readable, never a silent failure - the promise the whole refusal path
            // exists for (Part 5). In the trail as well, so a picture that "did not work" can be
            // looked up afterwards.
            ControlLog.AssetRefused(_logger, failure.Name, failure.Reason, failure.Detail);
        }

        var unused = await ShowAsync(screen, report, background).ConfigureAwait(true);

        _status.Text = unused is null ? Collected(report) : Collected(report) + " " + unused;

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Puts what came in onto the screen.
    /// <para>
    /// The background layer holds ONE picture. A run of several meant as a background therefore uses
    /// its first, and <b>says which and how many it did not use</b> - the rest were taken into the
    /// stock and are simply not on a screen. Returning quietly used to leave the collected message
    /// saying "5 taken in" beside a table showing one, with nothing accounting for the other four.
    /// </para>
    /// </summary>
    private async Task<string?> ShowAsync(ScreenRef screen, IntakeReport report, bool background)
    {
        // Everything that is now in the stock, IN THE ORDER THE DM CHOSE IT. A picture that is
        // already known goes on the screen like any other - a second sending of the same file is
        // exactly the wish to put it up again.
        //
        // It used to be "the new ones, then the known ones", which is how the report grouped them
        // (hand-run of M3): a picture already in the stock was placed last however early it was
        // picked, and since the hub hands each arrival the next depth up, it landed ON TOP of the
        // ones chosen after it. Choosing first has to mean lying underneath.
        var placeable = report.Placeable;

        if (background)
        {
            if (placeable.Count == 0)
            {
                return null;
            }

            await _session.SetBackgroundAsync(screen, placeable[0]).ConfigureAwait(true);

            if (placeable.Count == 1)
            {
                return null;
            }

            var others = placeable.Count - 1;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{placeable[0].Name} is the background; the other {others} went into the stock only.");
        }

        foreach (var asset in placeable)
        {
            await _session.AddItemAsync(screen, asset, position: null).ConfigureAwait(true);
        }

        return null;
    }

    private static string Progressing(IntakeProgress step) =>
        step.Total == 1
            ? $"Taking {step.Name} in - this can take a few seconds for a large picture ..."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{step.Done} of {step.Total} ... {step.Name}");

    /// <summary>
    /// ONE collected message, not one dialogue per file (Part 7). Two hundred messages to click
    /// away would be worse than the fault they report.
    /// </summary>
    private static string Collected(IntakeReport report)
    {
        var parts = new List<string>();

        if (report.Taken.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{report.Taken.Count} taken in"));
        }

        if (report.AlreadyPresent.Count > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture, $"{report.AlreadyPresent.Count} already in the stock"));
        }

        if (report.Refused.Count > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Refused.Count} refused ({string.Join("; ", report.Refused.Select(f => $"{f.Name}: {f.Detail}"))})"));
        }

        // The tolerated rank is reported rather than hidden: it went through, and the DM is told
        // that this format is not one of the six the build promises (Part 5).
        if (report.Tolerated.Count > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Tolerated.Count} in a format that is not assured ({string.Join(", ", report.Tolerated)})"));
        }

        return parts.Count == 0
            ? "Nothing came in."
            : string.Join(", ", parts) + (report.Cancelled ? " - broken off, what came in stays." : ".");
    }

    private void Switch(CheckBox box, Func<bool, Task> command)
    {
        if (_settingSwitches || Screen() is null)
        {
            return;
        }

        Run(_ => command(box.IsChecked is true));
    }

    private void WithItem(Func<ImageItem, Task> command)
    {
        if (_items.SelectedItem is not ItemEntry entry)
        {
            _status.Text = "Pick an item first.";
            return;
        }

        Run(_ => command(entry.Item));
    }

    private async void Run(Func<ScreenRef, Task> command)
    {
        if (Screen() is not { } screen)
        {
            _status.Text = "Pick a screen first.";
            return;
        }

        await command(screen).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private static TextBlock Heading(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4),
        };

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        button.Click += click;

        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        foreach (var child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }

    /// <summary>
    /// One line per item, and from M3 on it carries the VALUES.
    /// <para>
    /// <b>This line is what "done when" is measured against</b> (Part 10): every manipulation at the
    /// table has to be visible in the control at once, and until M4 there is no thumbnail to see it
    /// in - so position, angle, scale, <c>ZOrder</c>, locked and parked stand at the entry and change
    /// with every release. Without them the sentence would be checking a display that does not exist
    /// yet.
    /// </para>
    /// </summary>
    private sealed record ItemEntry(ImageItem Item)
    {
        internal ItemId ItemId => Item.ItemId;

        public override string ToString()
        {
            var marks = string.Concat(
                Item.ShowName ? " [name]" : string.Empty,
                Item.Locked ? " [locked]" : string.Empty,
                Item.Parked ? " [parked]" : string.Empty,
                Item.Meta.IsAnimated ? Item.AnimationPaused ? " [held]" : " [moving]" : string.Empty);

            // Two decimals on the normalised values: at 1920 pixels a hundredth is 19 of them, which
            // is what a hand-run needs to see move. More digits would turn a list into a wall.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Item.Name} - {Item.CenterX:F2},{Item.CenterY:F2} "
                + $"x{Item.Scale:F2} {Item.RotationDeg:F0}° z{Item.ZOrder} r{Item.Revision}{marks}");
        }
    }
}
