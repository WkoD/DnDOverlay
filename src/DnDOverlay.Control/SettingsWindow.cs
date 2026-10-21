using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DnDOverlay.Control;

/// <summary>
/// The window <i>Settings</i> (Part 7): what is set once while setting up and then not touched for
/// months - the test being that it can stay shut a whole evening without anything missing.
/// <para>
/// <b>An ordinary window, not a dialog</b>: it can stand open beside the stage while the effect of a
/// setting is watched at the table. Every value takes effect when it is changed; there is no
/// "Apply", because a control that has to be confirmed is a second place where the value is not yet
/// what it says.
/// </para>
/// </summary>
internal sealed class SettingsWindow : Window, IDisposable
{
    private readonly ControlSettings _settings;
    private readonly CampaignDesk _desk;
    private readonly ISessionApi _session;
    private readonly ProcessLog _log;
    private readonly ILogger _logger;
    private readonly Action<string> _say;
    private readonly CultureInfo _system;
    private readonly NetworkPanel _network;
    private readonly TextBlock _place = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _moving = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _breakOff = new() { Content = "Break off", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly Button _change = new() { Content = "Change place ...", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };

    private CancellationTokenSource? _breaking;

    internal SettingsWindow(
        ControlSettings settings, CampaignDesk desk, ISessionApi session, ProcessLog log, ILogger logger, int port, CultureInfo system, Action<string> say)
    {
        _settings = settings;
        _desk = desk;
        _session = session;
        _log = log;
        _logger = logger;
        _say = say;
        _system = system;
        _network = new NetworkPanel(port, firstRun: false);

        Title = "Settings";
        Width = 720;
        Height = 760;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Heading("Language", first: true));
        panel.Children.Add(Speech());

        panel.Children.Add(Heading("Loading"));
        panel.Children.Add(Loading());

        panel.Children.Add(Heading("Pictures"));
        panel.Children.Add(Pictures());

        panel.Children.Add(Heading("Campaigns"));
        panel.Children.Add(Campaigns());

        panel.Children.Add(Heading("Log"));
        panel.Children.Add(Logging());

        panel.Children.Add(Heading("Network"));
        panel.Children.Add(Port(port));
        panel.Children.Add(_network);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Closing += (_, closing) =>
        {
            // A copy under way is broken off by its own button, not by closing the window over it:
            // the window is where its progress and its end are said.
            if (_breaking is not null)
            {
                closing.Cancel = true;
                _say("The campaigns are being moved - break the move off first, or wait for it.");
            }
        };

        Closed += (_, _) => Dispose();
    }

    public void Dispose()
    {
        _network.Dispose();
        _breaking?.Dispose();
    }

    /// <summary>What the blind grips load with - a double tap or a drag, for a scene and for a layout (Part 7).</summary>
    private Grid Loading()
    {
        var scene = Choice(
            [("Replace what lies there", SceneLoad.Replace), ("Add to what lies there", SceneLoad.Add)],
            _settings.Current.SceneLoad,
            chosen => _settings.Update(current => current with { SceneLoad = chosen }));

        var layout = Choice(
            [("Add", LayoutLoad.Add), ("Replace used screens", LayoutLoad.ReplaceUsed), ("Replace all screens", LayoutLoad.ReplaceAll)],
            _settings.Current.LayoutLoad,
            chosen => _settings.Update(current => current with { LayoutLoad = chosen }));

        return Rows(
            ("A double tap or drag loads a scene by", scene),
            ("... and a layout by", layout),
            (string.Empty, Note("The menu of a scene or layout always offers every way.")));
    }

    /// <summary>The name pattern for pasted pictures and the touch points on the table.</summary>
    private Grid Pictures()
    {
        var pattern = new TextBox { Text = _settings.Current.CountedNamePattern, Padding = new Thickness(4, 2, 4, 2), MinWidth = 220 };
        var problem = Note(Next(_settings.Current.CountedNamePattern));

        void Keep()
        {
            var typed = pattern.Text.Trim();

            if (!typed.Contains("{n}", StringComparison.Ordinal))
            {
                problem.Text = "The name needs {n} where the number goes.";
                return;
            }

            problem.Text = Next(typed);
            _settings.Update(current => current with { CountedNamePattern = typed });
        }

        string Next(string typed) =>
            "{n} becomes a number counting up - \"" + typed.Replace("{n}", _settings.Current.CountedNameNext.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal) + "\" is next.";

        pattern.LostFocus += (_, _) => Keep();
        pattern.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                Keep();
            }
        };

        var touch = new CheckBox { Content = "Show touch points from the tables in the thumbnails", IsChecked = _settings.Current.ShowTouchPoints };

        touch.Click += (_, _) =>
        {
            var reporting = touch.IsChecked is true;

            _settings.Update(current => current with { ShowTouchPoints = reporting });
            _ = _session.SetTouchPointsAsync(reporting);
        };

        return Rows(
            ("Name for a pasted picture without one", pattern),
            (string.Empty, problem),
            (string.Empty, touch));
    }

    /// <summary>Where the campaigns lie, and the way to move them (Part 7).</summary>
    private StackPanel Campaigns()
    {
        var open = new Button { Content = "Open folder", Padding = new Thickness(10, 3, 10, 3) };

        _change.Click += async (_, _) => await ChangePlaceAsync().ConfigureAwait(true);
        _breakOff.Click += (_, _) => _breaking?.Cancel();
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_desk.BasePath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_desk.BasePath}\"") { UseShellExecute = true });
        };

        ShowPlace();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        buttons.Children.Add(_change);
        buttons.Children.Add(open);

        return new StackPanel { Children = { _place, buttons, _progress, _moving, _breakOff } };
    }

    /// <summary>
    /// This control's language (Part 8), the devices have their own. The log takes it at once; windows
    /// and menus are built in code once, so they take it at the next start. Each language is named in
    /// itself - whoever looks for theirs reads it in theirs.
    /// </summary>
    private StackPanel Speech()
    {
        var note = Note("Log lines change at once, windows and menus at the next start.");
        var language = Choice(
            [("Automatic (as Windows)", UiLanguage.Automatic), ("English", UiLanguage.English), ("Deutsch", UiLanguage.German)],
            _settings.Current.Language,
            chosen =>
            {
                _log.Culture = UiLanguages.CultureOf(chosen, _system);
                _settings.Update(current => current with { Language = chosen });
            });

        return new StackPanel { Children = { Rows(("This control speaks", language)), note } };
    }

    /// <summary>The level of this control's own log - it takes effect at once (Part 8).</summary>
    private Grid Logging()
    {
        var level = Choice(
            [("Everything (Trace)", LogLevel.Trace), ("Debug", LogLevel.Debug), ("Information", LogLevel.Information), ("Warnings", LogLevel.Warning), ("Errors", LogLevel.Error)],
            _settings.Current.LogLevel,
            chosen =>
            {
                _log.Level = chosen;
                _settings.Update(current => current with { LogLevel = chosen });
            });

        return Rows(("This control writes", level));
    }

    /// <summary>
    /// The port - it had no place at all except the file (Part 7). Taken at the next start: the hub is
    /// listening on the old one now, and every paired display looks for it there.
    /// </summary>
    private Grid Port(int running)
    {
        var port = new TextBox { Text = _settings.Current.Port.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(4, 2, 4, 2), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var note = Note(_settings.Current.Port == running ? string.Empty : $"Taken at the next start - {running} until then.");

        void Keep()
        {
            if (!int.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var wanted) || wanted is < 1024 or > 65535)
            {
                note.Text = "A port is a number from 1024 to 65535.";
                return;
            }

            _settings.Update(current => current with { Port = wanted });
            note.Text = wanted == running
                ? string.Empty
                : $"Taken at the next start - {running} until then. A display finds the control again by itself; a firewall rule needs setting up for the new port.";
        }

        port.LostFocus += (_, _) => Keep();
        port.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                Keep();
            }
        };

        return Rows(("Port", port), (string.Empty, note));
    }

    private void ShowPlace() =>
        _place.Text = $"The campaigns lie in {_desk.BasePath}.";

    /// <summary>
    /// Moves the campaign folder (Part 7): the question names both places, how many campaigns and how
    /// big, warns of a place so deep that names get tight, and offers to take them along or only to
    /// change the place. The open campaign is closed first - the same as a switch.
    /// </summary>
    private async Task ChangePlaceAsync()
    {
        var picker = new OpenFolderDialog { Title = "New place for the campaigns" };

        if (picker.ShowDialog(this) is not true)
        {
            return;
        }

        var from = Path.GetFullPath(_desk.BasePath);
        var to = Path.GetFullPath(picker.FolderName);

        if (string.Equals(from.TrimEnd('\\'), to.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Inside(to, from) || Inside(from, to))
        {
            MessageBox.Show(this, "The new place must not lie inside the old one, nor the old inside the new.", "Move campaigns");
            return;
        }

        var plan = CampaignMove.Plan(from, to);

        if (plan.Collisions.Count > 0)
        {
            MessageBox.Show(
                this,
                $"Campaigns of the same name are already at the new place: {string.Join(", ", plan.Collisions)}.\n\nNothing was moved. Rename or remove them there first.",
                "Move campaigns");
            return;
        }

        var answer = MoveQuestion.Ask(this, from, to, plan, closing: _desk.Open is not null);

        if (answer is MoveAnswer.Cancel)
        {
            return;
        }

        await _desk.CloseAsync().ConfigureAwait(true);

        if (answer is MoveAnswer.OnlyPlace || plan.Campaigns.Count == 0)
        {
            Take(to);
            _say(plan.Campaigns.Count == 0
                ? $"The campaigns lie in {to} from now on."
                : $"The campaigns lie in {to} from now on. The {plan.Campaigns.Count} at {from} stay there and no longer appear in the switcher.");
            return;
        }

        await MoveAsync(from, to, plan).ConfigureAwait(true);
    }

    private async Task MoveAsync(string from, string to, MovePlan plan)
    {
        _breaking = new CancellationTokenSource();
        _change.IsEnabled = false;
        _moving.Text = "Moving ...";

        var progress = new Progress<(long Done, long Total)>(report =>
        {
            // Shown only once there is something to show: a rename is over before it could be read.
            _progress.Visibility = Visibility.Visible;
            _breakOff.Visibility = Visibility.Visible;
            _progress.Maximum = Math.Max(1, report.Total);
            _progress.Value = report.Done;
            _moving.Text = string.Create(CultureInfo.CurrentCulture, $"Copying: {report.Done / (1024d * 1024):0} of {report.Total / (1024d * 1024):0} MB.");
        });

        var token = _breaking.Token;

        try
        {
            var result = await Task.Run(() => CampaignMove.Move(from, to, progress, cancellationToken: token), token).ConfigureAwait(true);

            // The new place is taken into use FIRST; only then do the old copies go - there is no
            // moment in which the path points at something incomplete.
            Take(to);

            var left = CampaignMove.RemoveSources(result);

            ControlLog.CampaignsMoved(_logger, from, to, plan.Campaigns.Count, result.Copied);

            _moving.Text = left.Count == 0
                ? $"Moved {plan.Campaigns.Count} campaign(s) to {to}."
                : $"Moved {plan.Campaigns.Count} campaign(s) to {to}. Left at the old place, because in use: {string.Join(", ", left.Select(Path.GetFileName))} - delete it by hand.";
            _say(_moving.Text);
        }
        catch (OperationCanceledException)
        {
            ControlLog.CampaignsNotMoved(_logger, from, to, "Broken off.");
            _moving.Text = $"Broken off. Everything is at {from}, as before.";
        }
        catch (CampaignMoveException refused)
        {
            ControlLog.CampaignsNotMoved(_logger, from, to, refused.Message);
            _moving.Text = $"{refused.Message} Everything is at {from}, as before.";
        }
        finally
        {
            _breaking.Dispose();
            _breaking = null;
            _change.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
            _breakOff.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>The new place in use: kept in the configuration, and the library looks there.</summary>
    private void Take(string to)
    {
        _settings.Update(current => current with { CampaignsPath = to });
        _settings.Flush();
        _desk.Relocate(to);
        ShowPlace();
    }

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static ComboBox Choice<T>(IReadOnlyList<(string Label, T Value)> options, T current, Action<T> chosen)
    {
        var box = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };

        foreach (var (label, value) in options)
        {
            box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, current)) ?? box.Items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: T value })
            {
                chosen(value);
            }
        };

        return box;
    }

    private static Grid Rows(params (string Label, FrameworkElement Element)[] rows)
    {
        var grid = new Grid();

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = rows[index].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };

            Grid.SetRow(label, index);
            Grid.SetRow(rows[index].Element, index);
            Grid.SetColumn(rows[index].Element, 1);
            rows[index].Element.Margin = new Thickness(0, 0, 0, 6);

            grid.Children.Add(label);
            grid.Children.Add(rows[index].Element);
        }

        return grid;
    }

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };

    private static TextBlock Heading(string text, bool first = false) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, first ? 0 : 18, 0, 8) };
}

/// <summary>The three answers to the question before a move of the campaign folder.</summary>
internal enum MoveAnswer
{
    Cancel,
    Move,
    OnlyPlace,
}

/// <summary>
/// The question before the campaign folder moves (Part 7): both places, how many campaigns and how
/// big, the room left for names if the new place is deep, and two ways - take them along (preset), or
/// only change the place, which says plainly that the old ones stay and leave the switcher.
/// </summary>
internal sealed class MoveQuestion : Window
{
    private MoveAnswer _answer = MoveAnswer.Cancel;

    private MoveQuestion(Window owner, string from, string to, MovePlan plan, bool closing)
    {
        Owner = owner;
        Title = "Move campaigns";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var lines = new List<string>
        {
            $"From: {from}",
            $"To: {to}",
            string.Create(CultureInfo.CurrentCulture, $"There are {plan.Campaigns.Count} campaign(s), {plan.Bytes / (1024d * 1024 * 1024):0.0} GB."),
        };

        if (plan.RoomForNames < MovePlan.TightRoom)
        {
            lines.Add($"The new place is deep: only {plan.RoomForNames} characters remain for a campaign's name and a scene's name together. Longer names cannot be saved there.");
        }

        if (closing)
        {
            lines.Add("The open campaign is closed first: the table is cleared on every screen, and undo cannot go back past this.");
        }

        lines.Add("\"Only change the place\" leaves the campaigns where they are - they no longer appear in the switcher.");

        var move = new Button { Content = "Move campaigns", IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var only = new Button { Content = "Only change the place", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };

        move.Click += (_, _) => Answer(MoveAnswer.Move);
        only.Click += (_, _) => Answer(MoveAnswer.OnlyPlace);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };

        buttons.Children.Add(move);
        buttons.Children.Add(only);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };

        foreach (var line in lines)
        {
            panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        }

        panel.Children.Add(buttons);
        Content = panel;
    }

    internal static MoveAnswer Ask(Window owner, string from, string to, MovePlan plan, bool closing)
    {
        var question = new MoveQuestion(owner, from, to, plan, closing);

        question.ShowDialog();

        return question._answer;
    }

    private void Answer(MoveAnswer answer)
    {
        _answer = answer;
        DialogResult = true;
    }
}
