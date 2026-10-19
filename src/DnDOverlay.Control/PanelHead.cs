using System.Windows;
using System.Windows.Controls;

namespace DnDOverlay.Control;

/// <summary>
/// The strip that carries what concerns the WHOLE table, above the stage - in M4 with one button
/// on it (Part 7).
/// <para>
/// <b>A strip with one button rather than a button with a strip</b>, and that is the decision in
/// it. Part 7 puts the view switch into the head of the campaign panel, beside undo, and gives
/// the reason: that head is the one place that stays visible whatever tab is
/// open and however far the panel is retracted. The panel itself is M5b - but the property that
/// earns the place is already true of the strip, so the strip is built now and filled later.
/// </para>
/// <para>
/// <b>It will have to turn.</b> Docked left or right the head lies at the top, docked top or bottom
/// it lies at the LEFT with the tabs upright beside it (Prüfschritt 40d). Nothing here does that
/// yet; what it does is keep the arrangement in one place, so that giving it a direction is a
/// change to this file rather than to the window.
/// </para>
/// </summary>
internal sealed class PanelHead : Border
{
    private readonly Button _view = new()
    {
        Padding = new Thickness(10, 3, 10, 3),
        MinWidth = 130,
    };

    /// <summary>
    /// The campaign: its name and the way to the others, at the START of the head - it is no grip in
    /// play but the frame around it (Part 7). With none open it says so and names the two ways on,
    /// rather than looking empty.
    /// </summary>
    private readonly Button _campaign = new()
    {
        Padding = new Thickness(10, 3, 10, 3),
        MinWidth = 180,
        Margin = new Thickness(0, 0, 8, 0),
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    private readonly TextBlock _hint = new()
    {
        Text = "Open one, or add pictures to begin a new one.",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 12, 0),
        Opacity = 0.75,
    };

    /// <summary>
    /// The menu of the rarer things - clear all, send to all, and set apart below it what has
    /// nothing to do with play (Part 7). Here because this head is the one place that stays visible.
    /// </summary>
    private readonly Button _menu = new()
    {
        Content = "☰",
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(8, 0, 0, 0),
    };

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };

    internal PanelHead()
    {
        Padding = new Thickness(0, 0, 0, 6);

        _row.Children.Add(_campaign);
        _row.Children.Add(_hint);
        _row.Children.Add(_view);
        _row.Children.Add(_menu);

        _campaign.Click += (_, _) => CampaignAsked?.Invoke(this, EventArgs.Empty);
        _menu.Click += (_, _) => MenuAsked?.Invoke(this, EventArgs.Empty);

        Child = _row;

        _view.Click += (_, _) => Toggled?.Invoke(this, EventArgs.Empty);

        Show(single: false);
    }

    /// <summary>The DM asked for the other view.</summary>
    internal event EventHandler? Toggled;

    /// <summary>The DM tapped the campaign - the switcher opens.</summary>
    internal event EventHandler? CampaignAsked;

    /// <summary>The DM tapped ☰.</summary>
    internal event EventHandler? MenuAsked;

    /// <summary>The ☰ button, for the menu to open under.</summary>
    internal UIElement MenuButton => _menu;

    /// <summary>
    /// Whether the head stands on the left over the height of a lying panel rather than across the top
    /// of an upright one (Part 7).
    /// </summary>
    internal bool Vertical
    {
        set
        {
            _row.Orientation = value ? Orientation.Vertical : Orientation.Horizontal;
            _hint.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _hint.MaxWidth = value ? 180 : double.PositiveInfinity;
            Padding = value ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 0, 6);

            foreach (var child in _row.Children.OfType<FrameworkElement>())
            {
                child.Margin = value ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 8, 0);
            }
        }
    }

    /// <summary>Which campaign is open, or <see langword="null"/> for none.</summary>
    internal void Campaign(string? label)
    {
        _campaign.Content = (label ?? "No campaign open") + "  ▾";
        _hint.Visibility = label is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// What the button offers, which is the other view rather than the current one: a button that
    /// named the state it is in reads as a switch that has already been thrown.
    /// </summary>
    internal void Show(bool single) => _view.Content = single ? "Overview" : "Single view";
}
