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
    private readonly CheckBox _images = new() { Content = "Images", Margin = new Thickness(0, 0, 16, 0), IsChecked = true };
    private readonly CheckBox _background = new() { Content = "Background", IsChecked = true };

    private readonly ComboBox _placement = new()
    {
        Width = 130,
        Margin = new Thickness(0, 0, 8, 0),
        ItemsSource = Enum.GetValues<PlacementMode>(),
    };

    private bool _settingSwitches;

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
    }

    /// <summary>
    /// Redraws from the authoritative scene. Called whenever a patch arrives, so what stands here
    /// is what the hub holds and never a second copy kept in step by hand (rule 1).
    /// </summary>
    internal async Task RefreshAsync()
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
    }

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
    /// Takes the grips away while a picture is being taken in, and gives back what they were so the
    /// caller can put them back exactly. Not decoration: the step costs seconds, and a control that
    /// looks idle invites a second picture to be started into the middle of the first.
    /// </summary>
    private bool StopTheGrips()
    {
        var before = IsEnabled;
        IsEnabled = false;

        return before;
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

        // Taking pictures in costs SECONDS and the DM has to be able to see that it is happening.
        // Measured with the real files (hand-run of M2b, second round, step 17): a 24 MB PNG at
        // 4616×6000 spent 11.6 s being normalised before the pass-through, and 1.2 s after. A line
        // of text alone was there and was missed - so the grips go dead as well, which is the part
        // that cannot be overlooked and which also stops a second run being started into the middle
        // of the first.
        var grips = StopTheGrips();
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
            report = await Task.Run(() => _entrances.TakeInAsync(sources, progress, CancellationToken.None))
                .ConfigureAwait(true);
        }
        finally
        {
            IsEnabled = grips;
        }

        ControlLog.IntakeFinished(
            _logger,
            sources.Count,
            clock.ElapsedMilliseconds,
            report.Taken.Count,
            report.AlreadyPresent.Count,
            report.Refused.Count,
            report.Cancelled ? ", broken off" : string.Empty);

        foreach (var failure in report.Refused)
        {
            // Named and readable, never a silent failure - the promise the whole refusal path
            // exists for (Part 5). In the trail as well, so a picture that "did not work" can be
            // looked up afterwards.
            ControlLog.AssetRefused(_logger, failure.Name, failure.Reason, failure.Detail);
        }

        await ShowAsync(screen, report, background).ConfigureAwait(true);

        _status.Text = Collected(report);

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Puts what came in onto the screen. The background layer holds ONE picture, so a run of many
    /// meant as a background shows its first and says so rather than quietly dropping the rest.
    /// </summary>
    private async Task ShowAsync(ScreenRef screen, IntakeReport report, bool background)
    {
        foreach (var asset in report.Taken.Concat(report.AlreadyPresent))
        {
            ControlLog.AssetTakenIn(
                _logger,
                asset.Name,
                asset.AssetId.Value,
                asset.Meta.PixelWidth,
                asset.Meta.PixelHeight,
                asset.Meta.Bytes,
                0);

            if (background)
            {
                await _session.SetBackgroundAsync(screen, asset).ConfigureAwait(true);
                return;
            }

            await _session.AddItemAsync(screen, asset, position: null).ConfigureAwait(true);
        }
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

    private sealed record ItemEntry(ImageItem Item)
    {
        internal ItemId ItemId => Item.ItemId;

        public override string ToString()
        {
            var marks = string.Concat(
                Item.ShowName ? " [name]" : string.Empty,
                Item.Meta.IsAnimated ? Item.AnimationPaused ? " [held]" : " [moving]" : string.Empty);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Item.Name} - {Item.Meta.PixelWidth}x{Item.Meta.PixelHeight} {Item.Meta.Format}{marks}");
        }
    }
}
