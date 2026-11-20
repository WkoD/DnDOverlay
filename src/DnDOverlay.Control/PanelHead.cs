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
        Text = Strings.PanelHeadHint,
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

    /// <summary>
    /// The head's first line. <b>☰ and the view switch are docked first, so they keep their room
    /// whatever the panel's width</b> - in a plain row they came last and were cut off once the panel
    /// was narrower than campaign, hint and switch together: on the SP7 at half the window, with no
    /// campaign open, ☰ - and with it "Devices ..." and the pairing - could not be reached at all
    /// (hand run on the development PC, 20.11.2026). The hint takes what is left and wraps.
    /// </summary>
    private readonly DockPanel _row = new();

    /// <summary>
    /// Undo and redo on a line of their own (Part 7: in the head, beside the view switch) - each label
    /// says what the next press does, and that is the only way through a long timeline. Greyed, never
    /// gone, when there is nothing.
    /// </summary>
    private readonly Grid _times = new() { Margin = new Thickness(0, 6, 0, 0) };

    private readonly Button _undo = new() { Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly Button _redo = new() { Padding = new Thickness(8, 3, 8, 3), HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _undoText = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _redoText = new() { TextTrimming = TextTrimming.CharacterEllipsis };

    private string? _undoLabel;
    private string? _redoLabel;

    internal PanelHead()
    {
        Padding = new Thickness(0, 0, 0, 6);

        _undo.Content = _undoText;
        _redo.Content = _redoText;
        _undo.Click += (_, _) => UndoAsked?.Invoke(this, EventArgs.Empty);
        _redo.Click += (_, _) => RedoAsked?.Invoke(this, EventArgs.Empty);
        _undo.SizeChanged += (_, _) => Label();
        _redo.SizeChanged += (_, _) => Label();
        Grid.SetColumn(_redo, 1);
        _times.Children.Add(_undo);
        _times.Children.Add(_redo);
        Times(vertical: false);
        Timeline(undo: null, redo: null);

        Arrange(vertical: false);

        _campaign.Click += (_, _) => CampaignAsked?.Invoke(this, EventArgs.Empty);
        _menu.Click += (_, _) => MenuAsked?.Invoke(this, EventArgs.Empty);

        Child = new StackPanel { Children = { _row, _times } };

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

    /// <summary>The DM pressed undo.</summary>
    internal event EventHandler? UndoAsked;

    /// <summary>The DM pressed redo.</summary>
    internal event EventHandler? RedoAsked;

    /// <summary>What the next undo and redo do - null where there is nothing, which greys the button.</summary>
    internal void Timeline(string? undo, string? redo)
    {
        _undoLabel = undo;
        _redoLabel = redo;
        _undo.IsEnabled = undo is not null;
        _redo.IsEnabled = redo is not null;
        _undo.ToolTip = undo is null ? Strings.PanelHeadNothingToUndo : Strings.PanelHeadUndoTip(undo);
        _redo.ToolTip = redo is null ? Strings.PanelHeadNothingToRedo : Strings.PanelHeadRedoTip(redo);
        Label();
    }

    /// <summary>
    /// The labels for the room there is: the whole act where it fits, cut to its verb where the panel
    /// is narrow (step 40d) - "Add" says more than "Add \"Grimm…".
    /// </summary>
    private void Label()
    {
        var verbLast = bool.TryParse(Strings.PanelHeadVerbLast, out var last) && last;

        _undoText.Text = "↶ " + Fitted(_undoLabel, Strings.PanelHeadUndo, _undo.ActualWidth, verbLast);
        _redoText.Text = "↷ " + Fitted(_redoLabel, Strings.PanelHeadRedo, _redo.ActualWidth, verbLast);
    }

    /// <summary>
    /// One line always - a free text brings its line breaks into its label, and the button would grow
    /// as tall as the text. Narrow, the verb alone, from where the language puts it (Part 7: "cut to
    /// the verb"): first in English ("Add"), last in German, whose steps end on the participle
    /// ("hinzugefügt") - the first word there would be the picture's name or a number.
    /// </summary>
    internal static string Fitted(string? label, string none, double width, bool verbLast)
    {
        if (label is null)
        {
            return none;
        }

        var line = string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (width is not (> 0 and < 150))
        {
            return line;
        }

        return verbLast ? line[(line.LastIndexOf(' ') + 1)..] : line.Split(' ', 2)[0];
    }

    private void Times(bool vertical)
    {
        _times.ColumnDefinitions.Clear();
        _times.RowDefinitions.Clear();

        if (vertical)
        {
            _times.RowDefinitions.Add(new RowDefinition());
            _times.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(_redo, 0);
            Grid.SetRow(_redo, 1);
            _undo.Margin = new Thickness(0, 0, 0, 4);
            _times.MaxWidth = 180;
        }
        else
        {
            _times.ColumnDefinitions.Add(new ColumnDefinition());
            _times.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(_redo, 1);
            Grid.SetRow(_redo, 0);
            _undo.Margin = new Thickness(0, 0, 4, 0);
            _times.MaxWidth = double.PositiveInfinity;
        }
    }

    /// <summary>
    /// Whether the head stands on the left over the height of a lying panel rather than across the top
    /// of an upright one (Part 7).
    /// </summary>
    internal bool Vertical
    {
        set
        {
            Arrange(value);
            Padding = value ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 0, 6);
            Times(value);
        }
    }

    /// <summary>
    /// Lays the first line out across the top (☰ and the view switch docked right, the campaign left,
    /// the hint filling the rest) or down the side of a lying panel (one under the other, ☰ last).
    /// </summary>
    private void Arrange(bool vertical)
    {
        _row.Children.Clear();
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.MaxWidth = vertical ? 180 : double.PositiveInfinity;

        if (vertical)
        {
            _row.LastChildFill = false;

            foreach (var child in new FrameworkElement[] { _campaign, _hint, _view, _menu })
            {
                DockPanel.SetDock(child, Dock.Top);
                child.Margin = new Thickness(0, 0, 0, 6);
                _row.Children.Add(child);
            }

            return;
        }

        _row.LastChildFill = true;
        DockPanel.SetDock(_menu, Dock.Right);
        DockPanel.SetDock(_view, Dock.Right);
        DockPanel.SetDock(_campaign, Dock.Left);
        _menu.Margin = new Thickness(8, 0, 0, 0);
        _view.Margin = new Thickness(8, 0, 0, 0);
        _campaign.Margin = new Thickness(0, 0, 8, 0);
        _hint.Margin = new Thickness(0, 0, 4, 0);
        _row.Children.Add(_menu);
        _row.Children.Add(_view);
        _row.Children.Add(_campaign);
        _row.Children.Add(_hint);
    }

    /// <summary>
    /// Which campaign is open, or <see langword="null"/> for none. With none open,
    /// <paramref name="unreachable"/> says why there may be none to open - the campaigns folder cannot
    /// be reached - in place of the two ways on, which would both lead nowhere (Part 3).
    /// </summary>
    internal void Campaign(string? label, string? unreachable = null)
    {
        var lost = label is null && unreachable is not null;

        _campaign.Content = (label ?? (lost ? Strings.PanelHeadUnreachable : Strings.PanelHeadNoCampaign)) + "  ▾";
        _hint.Text = lost ? unreachable : Strings.PanelHeadHint;
        _hint.Visibility = label is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// What the button offers, which is the other view rather than the current one: a button that
    /// named the state it is in reads as a switch that has already been thrown.
    /// </summary>
    internal void Show(bool single) => _view.Content = single ? Strings.PanelHeadOverview : Strings.PanelHeadSingleView;
}
