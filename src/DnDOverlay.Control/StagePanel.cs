using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Win32;

namespace DnDOverlay.Control;

/// <summary>
/// The grips M2b needs: put a picture on a screen or behind it, switch the two layers, name an
/// item, hold an animation still, take one away.
/// <para>
/// <b>Scaffolding, and it says so.</b> The DM surface of Part 7 - tiles, stage, inventory - starts
/// in M4. What this exists for is that every command of M2b can be reached by hand, because
/// otherwise the hand-run of M2b has no way to be run at all: the libraries were finished and the
/// table had one button, which sent a hard-coded demo picture.
/// </para>
/// <para>
/// A file dialog is the only way in here, on purpose. Drop, screenshot paste, browser paste
/// and URL import are M2c and bring their own rules with them - a dialog cannot stand in for the
/// address check, so it does not pretend to.
/// </para>
/// </summary>
internal sealed class StagePanel : StackPanel
{
    private readonly ISessionApi _session;
    private readonly IAssetSink _stock;
    private readonly Func<ScreenRef?> _target;
    private readonly TextBlock _status;

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

    internal StagePanel(ISessionApi session, IAssetSink stock, Func<ScreenRef?> target, TextBlock status)
    {
        _session = session;
        _stock = stock;
        _target = target;
        _status = status;

        Margin = new Thickness(0, 12, 0, 0);

        Children.Add(Heading("Stage"));
        Children.Add(Row(
            Button("Send a picture ...", (_, _) => PutAsync(background: false)),
            Button("Set as background ...", (_, _) => PutAsync(background: true)),
            Button("Clear background", (_, _) => Run(screen => _session.ClearBackgroundAsync(screen)))));

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

    private async void PutAsync(bool background)
    {
        if (Screen() is not { } screen)
        {
            _status.Text = "Pick a screen first.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = background ? "Picture for the background layer" : "Picture for the table",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.avif;*.tif;*.tiff;*.psd;*.rptok|All files|*.*",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) is not true)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(dialog.FileName);

        var taken = await _stock
            .IngestAsync(await File.ReadAllBytesAsync(dialog.FileName).ConfigureAwait(true), name)
            .ConfigureAwait(true);

        if (taken is IngestResult.Refused refused)
        {
            // Named and readable, never a silent failure - the promise the whole refusal path
            // exists for (Part 5).
            _status.Text = $"{name} was refused: {refused.Reason} - {refused.Detail}";
            return;
        }

        var stocked = (IngestResult.Taken)taken;

        if (background)
        {
            await _session.SetBackgroundAsync(screen, stocked.Asset).ConfigureAwait(true);
        }
        else
        {
            await _session.AddItemAsync(screen, stocked.Asset, position: null).ConfigureAwait(true);
        }

        // The tolerated rank is reported rather than hidden: it went through, and the DM is told
        // that this format is not one of the six the build promises (Part 5).
        var standing = stocked.Standing is FormatStanding.Tolerated ? " (format not promised)" : string.Empty;
        var known = stocked.AlreadyPresent ? ", already in the stock" : string.Empty;

        _status.Text = $"{stocked.Asset.Name} sent{standing}{known}.";

        await RefreshAsync().ConfigureAwait(true);
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
