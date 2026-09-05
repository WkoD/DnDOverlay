using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// One screen on the stage: a head of exactly one line, the thumbnail, and the row of buttons -
/// three areas of fixed height, so the tile is the same size whether it has twelve reasons or none
/// (Part 7).
/// <para>
/// <b>Two channels, two statements, never mixed.</b> A FRAME around the tile means "this is where
/// the next grip lands" and nothing else; the colour of the head means "this is how the screen
/// stands" and arrives in M5a with the state selector. Nothing else on the stage is framed or
/// coloured.
/// </para>
/// <para>
/// <b>The row of buttons is deliberately incomplete in M4.</b> The state selector is M5a, the focus
/// M5b, the underlay M5b - four of the eight controls Part 7 draws have no builder here, and a
/// disabled button standing in for them would be a promise rather than an order of work
/// (checks/M4.md). What has a caller is here; the rest arrives with what it does.
/// </para>
/// </summary>
internal sealed class ScreenTile : Border
{
    private readonly ISessionApi _session;
    private readonly TileHead _head = new();
    private readonly SceneThumbnail _thumbnail;
    private readonly Loading _loading;
    private readonly CheckBox _images = new() { Content = "Images", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _background = new() { Content = "Background", Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _unlock = new() { Content = "Unlock all", Padding = new Thickness(8, 2, 8, 2) };

    private readonly Border _frame;

    private bool _setting;

    /// <summary>How tall a thumbnail is in the overview, in DIP. The width follows its shape.</summary>
    private const double Small = 150;

    internal ScreenTile(ScreenRef screen, ISessionApi session, Pictures pictures)
    {
        Screen = screen;
        _session = session;
        _thumbnail = new SceneThumbnail(pictures);
        _loading = new Loading(pictures);

        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;
        Padding = new Thickness(6);
        Margin = new Thickness(0, 0, 8, 8);
        Background = Brushes.Transparent;

        var layers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        layers.Children.Add(_images);
        layers.Children.Add(_background);

        var grips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        grips.Children.Add(_unlock);

        // The scene and what has not arrived yet, one over the other. Two layers rather than one
        // drawing, because they run at different speeds: the scene is bundled to one pass, the
        // fill may never be (Part 7, rank 3 before 4).
        var layered = new Grid();

        layered.Children.Add(_thumbnail);
        layered.Children.Add(_loading);

        _frame = new Border
        {
            Child = layered,
            Height = Small,
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gainsboro,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var panel = new StackPanel();

        panel.Children.Add(_head);
        panel.Children.Add(_frame);
        panel.Children.Add(grips);
        panel.Children.Add(layers);

        Child = panel;

        _images.Click += async (_, _) => await ToggleAsync(images: true).ConfigureAwait(true);
        _background.Click += async (_, _) => await ToggleAsync(images: false).ConfigureAwait(true);
        _unlock.Click += async (_, _) =>
            await _session.UnlockAllAsync(Screen, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Which screen this tile is. It is the address of everything the tile does.</summary>
    internal ScreenRef Screen { get; }

    /// <summary>
    /// The head, and the one place a tile may be dragged by (Part 7). The tile's face is taken -
    /// items lie on it and the selection frame is drawn from it - while the head is the one strip
    /// that exists on every tile and is always the same size.
    /// </summary>
    internal UIElement Handle => _head;

    /// <summary>
    /// Whether this tile is the one open on its own - then the thumbnail takes the room it is
    /// given instead of its own height, and the head and the buttons stay exactly as they are
    /// (Part 7: "one tile filling the format, with the same head and the same buttons").
    /// </summary>
    internal bool Opened
    {
        set
        {
            _frame.Height = value ? double.NaN : Small;
            _frame.VerticalAlignment = value ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            _frame.HorizontalAlignment = value ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }
    }

    /// <summary>
    /// Whether this is the screen the next blind grip lands on - the paste hotkey, the double tap
    /// in the stock, the pre-selected entry in every target list (Part 7).
    /// </summary>
    internal bool Active
    {
        set => BorderBrush = value ? Brushes.SteelBlue : Brushes.Transparent;
    }

    /// <summary>
    /// What this tile shows from now on. The scene is drawn at the next render pass rather than
    /// here, so twenty arriving patches cost one drawing (<see cref="Redraw"/>).
    /// </summary>
    internal void Show(string label, SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        _head.Show(label, screen.Size.Width, screen.Size.Height);
        _thumbnail.Show(scene, screen, view);
        _loading.Show(scene, screen, view);

        // Set without sending: these are switches that carry their own state (Part 7), and a
        // checkbox that fires its own click handler on being told the truth would send the DM's
        // last command back at him inverted.
        _setting = true;
        _images.IsChecked = scene.ItemsVisible;
        _background.IsChecked = scene.BackgroundVisible;
        _setting = false;

        Redraw.Ask(_thumbnail);
    }

    /// <summary>
    /// What the device of this screen is loading. Straight through to the layer that draws it -
    /// nothing is bundled on the way, which is the whole reason that layer exists.
    /// </summary>
    internal void Report(IReadOnlyList<AssetLoad> loads) => _loading.Report(loads);

    private async Task ToggleAsync(bool images)
    {
        if (_setting)
        {
            return;
        }

        var wanted = (images ? _images.IsChecked : _background.IsChecked) ?? true;

        if (images)
        {
            await _session.ToggleItemsAsync(Screen, wanted, CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await _session.ToggleBackgroundAsync(Screen, wanted, CancellationToken.None).ConfigureAwait(true);
        }
    }
}
