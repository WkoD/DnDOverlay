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
    private readonly Button _breakOff = new() { Content = Strings.SettingsBreakOff, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly Button _change = new() { Content = Strings.SettingsChangePlace, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };

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

        Title = Strings.SettingsTitle;
        Width = 720;
        Height = 760;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Heading(Strings.SettingsHeadingLanguage, first: true));
        panel.Children.Add(Speech());

        panel.Children.Add(Heading(Strings.SettingsHeadingLoading));
        panel.Children.Add(Loading());

        panel.Children.Add(Heading(Strings.SettingsHeadingPictures));
        panel.Children.Add(Pictures());

        panel.Children.Add(Heading(Strings.SettingsHeadingCampaigns));
        panel.Children.Add(Campaigns());
        panel.Children.Add(Reporting());

        panel.Children.Add(Heading(Strings.SettingsHeadingLog));
        panel.Children.Add(Logging());

        panel.Children.Add(Heading(Strings.SettingsHeadingNetwork));
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
                _say(Strings.SettingsMoveUnderWay);
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
            [(Strings.SettingsSceneReplace, SceneLoad.Replace), (Strings.SettingsSceneAdd, SceneLoad.Add)],
            _settings.Current.SceneLoad,
            chosen => _settings.Update(current => current with { SceneLoad = chosen }));

        var layout = Choice(
            [(Strings.SettingsLayoutAdd, LayoutLoad.Add), (Strings.SettingsLayoutReplaceUsed, LayoutLoad.ReplaceUsed), (Strings.SettingsLayoutReplaceAll, LayoutLoad.ReplaceAll)],
            _settings.Current.LayoutLoad,
            chosen => _settings.Update(current => current with { LayoutLoad = chosen }));

        return Rows(
            (Strings.SettingsSceneLoadsBy, scene),
            (Strings.SettingsLayoutLoadsBy, layout),
            (string.Empty, Note(Strings.SettingsLoadMenuNote)));
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
                problem.Text = Strings.SettingsPatternNeedsNumber;
                return;
            }

            problem.Text = Next(typed);
            _settings.Update(current => current with { CountedNamePattern = typed });
        }

        string Next(string typed) =>
            Strings.SettingsPatternNext(typed.Replace("{n}", _settings.Current.CountedNameNext.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal));

        pattern.LostFocus += (_, _) => Keep();
        pattern.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                Keep();
            }
        };

        var touch = new CheckBox { Content = Strings.SettingsTouchPoints, IsChecked = _settings.Current.ShowTouchPoints };

        touch.Click += (_, _) =>
        {
            var reporting = touch.IsChecked is true;

            _settings.Update(current => current with { ShowTouchPoints = reporting });
            _ = _session.SetTouchPointsAsync(reporting);
        };

        return Rows(
            (Strings.SettingsPatternLabel, pattern),
            (string.Empty, problem),
            (string.Empty, touch));
    }

    /// <summary>Where the campaigns lie, and the way to move them (Part 7).</summary>
    private StackPanel Campaigns()
    {
        var open = new Button { Content = Strings.SettingsOpenFolder, Padding = new Thickness(10, 3, 10, 3) };

        _change.Click += async (_, _) => await ChangePlaceAsync().ConfigureAwait(true);
        _breakOff.Click += (_, _) => _breaking?.Cancel();
        open.Click += (_, _) =>
        {
            // The default place is made on first use. A place that cannot be reached - a stick pulled,
            // a share offline, the default place under a folder that is gone - is said, and nothing is
            // created in its stead: a new, empty base there would look like campaigns lost, and with
            // the drive letter gone the call threw out of the click (Part 3, "nicht erreichbar"; M5
            // review). The same rule as the library's, from the desk.
            if (!_desk.EnsureBaseFolder())
            {
                _moving.Text = Strings.SettingsFolderUnreachable(_desk.BasePath);
                return;
            }

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
        var note = Note(Strings.SettingsLanguageNote);
        var language = Choice(
            [(Strings.SettingsLanguageAutomatic, UiLanguage.Automatic), ("English", UiLanguage.English), ("Deutsch", UiLanguage.German)],
            _settings.Current.Language,
            chosen =>
            {
                _log.Culture = UiLanguages.CultureOf(chosen, _system);
                _settings.Update(current => current with { Language = chosen });
            });

        return new StackPanel { Children = { Rows((Strings.SettingsLanguageLabel, language)), note } };
    }

    /// <summary>
    /// When the stock reports itself (Part 7): the two thresholds, each with its switch - the same
    /// switch "Don't report again" turns off in the notice. Measured when a campaign opens.
    /// </summary>
    private StackPanel Reporting()
    {
        var stock = _settings.Current.Stock;
        var space = new CheckBox { Content = Strings.SettingsStockFreeSpace, IsChecked = stock.FreeSpaceOn, VerticalAlignment = VerticalAlignment.Center };
        var spaceValue = new TextBox { Text = (stock.FreeSpaceBelowBytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.CurrentCulture), Width = 60, Margin = new Thickness(6, 0, 6, 0) };
        var pictures = new CheckBox { Content = Strings.SettingsStockPictures, IsChecked = stock.PicturesOn, VerticalAlignment = VerticalAlignment.Center };
        var picturesValue = new TextBox { Text = stock.PicturesAbove.ToString(CultureInfo.CurrentCulture), Width = 60, Margin = new Thickness(6, 0, 6, 0) };
        var note = Note(Strings.SettingsStockNote);
        var problem = Note(string.Empty);

        space.Click += (_, _) => _settings.Update(current => current with { Stock = current.Stock with { FreeSpaceOn = space.IsChecked is true } });
        pictures.Click += (_, _) => _settings.Update(current => current with { Stock = current.Stock with { PicturesOn = pictures.IsChecked is true } });

        // A value that is refused does not stay in the box: the window says every value takes effect
        // as it is changed, so what it shows has to be what is in force - the value in force comes
        // back, and the line beneath says why, as the port and the pattern do (M5 review). Kept on
        // Enter as well as on leaving the field, like its neighbours.
        void KeepSpace()
        {
            if (double.TryParse(spaceValue.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var gigabytes) && gigabytes > 0)
            {
                _settings.Update(current => current with { Stock = current.Stock with { FreeSpaceBelowBytes = (long)(gigabytes * 1024 * 1024 * 1024) } });
                problem.Text = string.Empty;
                return;
            }

            spaceValue.Text = (_settings.Current.Stock.FreeSpaceBelowBytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.CurrentCulture);
            problem.Text = Strings.SettingsStockThresholdInvalid;
        }

        void KeepPictures()
        {
            if (int.TryParse(picturesValue.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var count) && count > 0)
            {
                _settings.Update(current => current with { Stock = current.Stock with { PicturesAbove = count } });
                problem.Text = string.Empty;
                return;
            }

            picturesValue.Text = _settings.Current.Stock.PicturesAbove.ToString(CultureInfo.CurrentCulture);
            problem.Text = Strings.SettingsStockThresholdInvalid;
        }

        spaceValue.LostFocus += (_, _) => KeepSpace();
        picturesValue.LostFocus += (_, _) => KeepPictures();
        spaceValue.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                KeepSpace();
            }
        };
        picturesValue.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                KeepPictures();
            }
        };

        static StackPanel Line(CheckBox box, TextBox value, string unit) =>
            new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0),
                Children = { box, value, new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center } },
            };

        return new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                Line(space, spaceValue, Strings.SettingsStockFreeSpaceUnit),
                Line(pictures, picturesValue, Strings.SettingsStockPicturesUnit),
                problem,
                note,
            },
        };
    }

    /// <summary>The level of this control's own log - it takes effect at once (Part 8).</summary>
    private Grid Logging()
    {
        var level = Choice(
            [(Strings.SettingsLogTrace, LogLevel.Trace), (Strings.SettingsLogDebug, LogLevel.Debug), (Strings.SettingsLogInformation, LogLevel.Information), (Strings.SettingsLogWarnings, LogLevel.Warning), (Strings.SettingsLogErrors, LogLevel.Error)],
            _settings.Current.LogLevel,
            chosen =>
            {
                _log.Level = chosen;
                _settings.Update(current => current with { LogLevel = chosen });
            });

        return Rows((Strings.SettingsLogLabel, level));
    }

    /// <summary>
    /// The port - it had no place at all except the file (Part 7). Taken at the next start: the hub is
    /// listening on the old one now, and every paired display looks for it there.
    /// </summary>
    private Grid Port(int running)
    {
        var port = new TextBox { Text = _settings.Current.Port.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(4, 2, 4, 2), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var note = Note(_settings.Current.Port == running ? string.Empty : Strings.SettingsPortLater(running));

        void Keep()
        {
            if (!int.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var wanted) || wanted is < 1024 or > 65535)
            {
                note.Text = Strings.SettingsPortRange;
                return;
            }

            _settings.Update(current => current with { Port = wanted });
            note.Text = wanted == running
                ? string.Empty
                : Strings.SettingsPortChanged(running);
        }

        port.LostFocus += (_, _) => Keep();
        port.KeyDown += (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                Keep();
            }
        };

        return Rows((Strings.SettingsPortLabel, port), (string.Empty, note));
    }

    private void ShowPlace() =>
        _place.Text = Strings.SettingsPlace(_desk.BasePath);

    /// <summary>
    /// Moves the campaign folder (Part 7): the question names both places, how many campaigns and how
    /// big, warns of a place so deep that names get tight, and offers to take them along or only to
    /// change the place. The open campaign is closed first - the same as a switch.
    /// </summary>
    private async Task ChangePlaceAsync()
    {
        // While pictures come in, nobody moves the folder they are written into (Part 7) - refused
        // before a place is even picked, with the same reason a switch gets.
        if (_desk.Refusal is { } running)
        {
            _moving.Text = running;
            return;
        }

        var picker = new OpenFolderDialog { Title = Strings.SettingsPickerTitle };

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
            MessageBox.Show(this, Strings.SettingsMoveInside, Strings.SettingsMoveCaption);
            return;
        }

        // The place in use cannot be reached: its campaigns cannot be moved now, and the plan would
        // count none - the place would change quietly and leave them behind. Said, and asked (Part 3).
        if (!_desk.Reachable)
        {
            var anyway = MessageBox.Show(
                this,
                Strings.SettingsOldPlaceUnreachable(from, to),
                Strings.SettingsMoveCaption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (anyway != MessageBoxResult.Yes)
            {
                return;
            }

            if (await _desk.CloseAsync().ConfigureAwait(true) is { } stillTaking)
            {
                _moving.Text = stillTaking;
                return;
            }

            Take(to);
            _say(Strings.SettingsTakenUnreachable(to, from));

            return;
        }

        var plan = CampaignMove.Plan(from, to);

        if (plan.Collisions.Count > 0)
        {
            MessageBox.Show(
                this,
                Strings.SettingsMoveCollisions(string.Join(", ", plan.Collisions)),
                Strings.SettingsMoveCaption);
            return;
        }

        var answer = MoveQuestion.Ask(this, from, to, plan, closing: _desk.Open is not null);

        if (answer is MoveAnswer.Cancel)
        {
            return;
        }

        // An intake begun while the question stood is refused here: the open campaign stays open.
        if (await _desk.CloseAsync().ConfigureAwait(true) is { } refused)
        {
            _moving.Text = refused;
            return;
        }

        if (answer is MoveAnswer.OnlyPlace || plan.Campaigns.Count == 0)
        {
            Take(to);
            _say(plan.Campaigns.Count == 0
                ? Strings.SettingsTaken(to)
                : Strings.SettingsTakenLeftBehind(to, plan.Campaigns.Count, from));
            return;
        }

        await MoveAsync(from, to, plan).ConfigureAwait(true);
    }

    private async Task MoveAsync(string from, string to, MovePlan plan)
    {
        _breaking = new CancellationTokenSource();
        _change.IsEnabled = false;
        _moving.Text = Strings.SettingsMoving;

        var progress = new Progress<(long Done, long Total)>(report =>
        {
            // Shown only once there is something to show: a rename is over before it could be read.
            _progress.Visibility = Visibility.Visible;
            _breakOff.Visibility = Visibility.Visible;
            _progress.Maximum = Math.Max(1, report.Total);
            _progress.Value = report.Done;
            _moving.Text = Strings.SettingsCopying(report.Done / (1024d * 1024), report.Total / (1024d * 1024));
        });

        var token = _breaking.Token;

        try
        {
            var result = await Task.Run(() => CampaignMove.Move(from, to, progress, cancellationToken: token), token).ConfigureAwait(true);

            // The new place is taken into use FIRST; only then do the old copies go - there is no
            // moment in which the path points at something incomplete.
            Take(to);

            // A copy is checked by number and size, not content - so each copied campaign is offered
            // "Verify stock" at its next opening, once (Part 7; DM decision). A rename on the same
            // drive moved no byte and leaves nothing to verify.
            if (result.Copied)
            {
                _settings.Update(current => current with { Stock = current.Stock.Moved(plan.Campaigns) });
            }

            var left = CampaignMove.RemoveSources(result);

            ControlLog.CampaignsMoved(_logger, from, to, plan.Campaigns.Count, result.Copied);

            _moving.Text = left.Count == 0
                ? Strings.SettingsMoved(plan.Campaigns.Count, to)
                : Strings.SettingsMovedLeftBehind(plan.Campaigns.Count, to, string.Join(", ", left.Select(Path.GetFileName)));
            _say(_moving.Text);
        }
        catch (OperationCanceledException)
        {
            ControlLog.CampaignsNotMoved(_logger, from, to, "Broken off.");
            _moving.Text = Strings.SettingsMoveBrokenOff(from);
        }
        catch (CampaignMoveException refused)
        {
            ControlLog.CampaignsNotMoved(_logger, from, to, refused.Message);
            _moving.Text = Strings.SettingsMoveRefused(refused.Message, from);
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
        Title = Strings.MoveTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var lines = new List<string>
        {
            Strings.MoveFrom(from),
            Strings.MoveTo(to),
            Strings.MoveSize(plan.Campaigns.Count, plan.Bytes / (1024d * 1024 * 1024)),
        };

        if (plan.RoomForNames < MovePlan.TightRoom)
        {
            lines.Add(Strings.MoveDeep(plan.RoomForNames));
        }

        if (closing)
        {
            lines.Add(Strings.MoveClosing);
        }

        lines.Add(Strings.MoveOnlyPlaceNote);

        var move = new Button { Content = Strings.MoveMove, IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var only = new Button { Content = Strings.MoveOnlyPlace, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.MoveCancel, IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };

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
