using System.Windows;
using System.Windows.Controls;

namespace DnDOverlay.Control;

/// <summary>
/// The strip that carries what concerns the WHOLE table, above the stage - in M4 with one button
/// on it (Part 7).
/// <para>
/// <b>A strip with one button rather than a button with a strip</b>, and that is the decision in
/// it. Part 7 puts the view switch into the head of the campaign panel, beside undo and the
/// blackout, and gives the reason: that head is the one place that stays visible whatever tab is
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

    internal PanelHead()
    {
        Padding = new Thickness(0, 0, 0, 6);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        row.Children.Add(_view);

        Child = row;

        _view.Click += (_, _) => Toggled?.Invoke(this, EventArgs.Empty);

        Show(single: false);
    }

    /// <summary>The DM asked for the other view.</summary>
    internal event EventHandler? Toggled;

    /// <summary>
    /// What the button offers, which is the other view rather than the current one: a button that
    /// named the state it is in reads as a switch that has already been thrown.
    /// </summary>
    internal void Show(bool single) => _view.Content = single ? "Overview" : "Single view";
}
