using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The two bands on the right of the window <i>Devices</i> (Part 7): everything about a device,
/// everything about one of its screens - and the heading of each says how far it reaches.
/// <para>
/// <b>Rendered from the shared description</b> (<see cref="DisplayParameters"/>): which band a
/// parameter falls into is its scope, nothing kept by hand here. Changed values take effect at once -
/// a <c>ConfigUpdate</c> like any other - because the point of the window is to watch the table while
/// the value turns. Without the device connected they are set all the same and go out when it is.
/// </para>
/// <para>
/// <b>A value at its default stands back</b> - grey and in italics, with "default" after it - and one
/// that was set stands in full, with "Reset" beside it. The difference is readable without an
/// explanation, which is the rule (Part 7); what "default" means is "equal to the default" (DM,
/// 21.10.2026).
/// </para>
/// </summary>
internal sealed class DeviceBands
{
    private static readonly Brush Standing = SystemColors.ControlTextBrush;
    private static readonly Brush Receding = Brushes.Gray;

    private readonly ISessionApi _session;
    private readonly CampaignDesk _desk;
    private readonly Action<string> _say;
    private readonly Window _owner;
    private readonly ControlSettings _settings;

    internal DeviceBands(ISessionApi session, CampaignDesk desk, ControlSettings settings, Action<string> say, Window owner)
    {
        _settings = settings;
        _session = session;
        _desk = desk;
        _say = say;
        _owner = owner;
    }

    /// <summary>A value was changed or a device unpaired - the bands draw again.</summary>
    internal event EventHandler? Changed;

    /// <summary>Scenes were saved before an unpair - the tab Scenes has new entries.</summary>
    internal event EventHandler? ScenesSaved;

    /// <summary>Fills <paramref name="host"/> with the device band and, for a screen, the screen band beneath it.</summary>
    internal void Show(StackPanel host, DeviceView device, ScreenView? screen, IReadOnlyList<DeviceView> all)
    {
        host.Children.Clear();
        host.Children.Add(DeviceBand(device, all));

        if (screen is not null)
        {
            host.Children.Add(ScreenBand(device, screen, all));
        }
        else
        {
            host.Children.Add(Note(Strings.DeviceBandChooseScreen));
        }
    }

    private StackPanel DeviceBand(DeviceView device, IReadOnlyList<DeviceView> all)
    {
        var band = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var labels = device.Screens.Select(screen => screen.Info.Label).ToList();

        band.Children.Add(Heading(Strings.DeviceBandDeviceHeading(device.Name)));
        band.Children.Add(Note(labels.Count switch
        {
            0 => Strings.DeviceBandAppliesNoScreen,
            1 => Strings.DeviceBandAppliesOneScreen(labels[0]),
            _ => Strings.DeviceBandAppliesAllScreens(labels.Count, string.Join(", ", labels)),
        }));

        if (!device.Connected)
        {
            band.Children.Add(Note(Strings.DeviceBandNotConnected));
        }
        else if (device.AppVersion is { } version)
        {
            band.Children.Add(Note(Strings.DeviceBandVersion(version, device.ProtocolVersion)));
        }

        var effective = _session.DeviceSettingsOf(device.Device);
        var rows = new Grid();

        Columns(rows);
        DeviceNameRow(rows, device, all);

        foreach (var parameter in DisplayParameters.For(ParameterScope.Device))
        {
            Row(
                rows,
                parameter,
                DisplayParameters.Value(parameter, effective),
                DisplayParameters.FollowsDefault(parameter, effective),
                canReset: true,
                set: value => ApplyAsync(device, DisplayParameters.Set(parameter, value)),
                reset: () => ApplyAsync(device, DisplayParameters.Reset(parameter)));
        }

        band.Children.Add(rows);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var identify = Button(Strings.DeviceBandShowNames);
        var clear = Button(Strings.DeviceBandClearStore);
        var restart = Button(Strings.DeviceBandRestart);
        var unpair = Button(Strings.DeviceBandUnpair);

        identify.IsEnabled = device.Connected;
        clear.IsEnabled = device.Connected;
        restart.IsEnabled = device.Connected;

        clear.Click += async (_, _) =>
        {
            if (Ask(Strings.DeviceBandClearStoreQuestion(device.Name), Strings.DeviceBandClearStoreTitle))
            {
                await CommandAsync(device, DeviceCommand.ClearCache, Strings.DeviceBandClearingStore(device.Name)).ConfigureAwait(true);
            }
        };

        restart.Click += async (_, _) =>
        {
            // Asked only while something is shown: a restart puts every screen of the device out for a moment.
            var showing = 0;

            foreach (var screen in device.Screens)
            {
                var scene = await _session.GetSceneAsync(screen.Screen).ConfigureAwait(true);

                showing += scene.Items.Count > 0 || scene.Background is not null ? 1 : 0;
            }

            if (showing == 0 || Ask(Strings.DeviceBandRestartQuestion(device.Name, device.Screens.Count, showing), Strings.DeviceBandRestartTitle))
            {
                await CommandAsync(device, DeviceCommand.Restart, Strings.DeviceBandRestarting(device.Name)).ConfigureAwait(true);
            }
        };
        identify.Click += async (_, _) =>
        {
            await _session.IdentifyScreensAsync(device.Device).ConfigureAwait(true);
            _say(Strings.DeviceBandShowingNames(device.Name));
        };
        unpair.Click += async (_, _) => await UnpairAsync(device).ConfigureAwait(true);

        buttons.Children.Add(identify);
        buttons.Children.Add(clear);
        buttons.Children.Add(restart);
        buttons.Children.Add(unpair);
        band.Children.Add(buttons);

        return band;
    }

    private StackPanel ScreenBand(DeviceView device, ScreenView screen, IReadOnlyList<DeviceView> all)
    {
        var band = new StackPanel();
        var label = screen.Info.Label;

        band.Children.Add(Heading(Strings.DeviceBandScreenHeading(label)));
        band.Children.Add(Note(Strings.DeviceBandAppliesOnlyTo(label)));

        var context = _session.ContextOf(screen.Screen);
        var rows = new Grid();

        Columns(rows);
        NameRow(rows, device, screen, all);
        StateRow(rows, screen);

        foreach (var parameter in DisplayParameters.For(ParameterScope.Screen))
        {
            Row(
                rows,
                parameter,
                DisplayParameters.Value(parameter, context),
                DisplayParameters.FollowsDefault(parameter, context),
                DisplayParameters.CanReset(parameter),
                set: value => ApplyAsync(device, screen, DisplayParameters.Set(parameter, context, value)),
                reset: () => ApplyAsync(device, screen, DisplayParameters.Reset(parameter, context)));
        }

        band.Children.Add(rows);

        if (screen.Suppressed is not SuppressReason.Unavailable)
        {
            TakeOver(band, screen, all);
        }

        var resetAll = Button(Strings.DeviceBandResetAll);

        resetAll.Click += async (_, _) =>
        {
            var deltas = DisplayParameters.For(ParameterScope.Screen)
                .Where(parameter => DisplayParameters.CanReset(parameter) && !DisplayParameters.FollowsDefault(parameter, context))
                .Select(parameter => DisplayParameters.Reset(parameter, context))
                .Aggregate(ScreenSettings.None, (all, next) => ScreenSettings.Merge(all, next));

            await ApplyAsync(device, screen, deltas).ConfigureAwait(true);
        };

        resetAll.Margin = new Thickness(0, 8, 0, 0);
        band.Children.Add(resetAll);

        return band;
    }

    /// <summary>
    /// "Take over" (Part 3, "reassign screen"): this screen, which is there, takes over a missing one -
    /// its wish, settings, name, the arrangement on it, its tile place and view. Offered only while a
    /// missing screen exists; the question names what moves and that the old entry goes.
    /// </summary>
    private void TakeOver(StackPanel band, ScreenView screen, IReadOnlyList<DeviceView> all)
    {
        var missing = all.SelectMany(device => device.Screens)
            .Where(other => other.Suppressed is SuppressReason.Unavailable && other.Screen != screen.Screen)
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var choice = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0) };

        foreach (var old in missing)
        {
            choice.Items.Add(new ComboBoxItem { Content = old.Info.Label, Tag = old });
        }

        choice.SelectedIndex = 0;

        var take = Button(Strings.DeviceBandTakeOver);

        take.Click += async (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: ScreenView old })
            {
                return;
            }

            var answer = MessageBox.Show(
                _owner,
                Strings.DeviceBandTakeOverQuestion(screen.Info.Label, old.Info.Label),
                Strings.DeviceBandTakeOverTitle,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }

            if (!await _session.ReassignScreenAsync(old.Screen, screen.Screen).ConfigureAwait(true))
            {
                _say(Strings.DeviceBandTakeOverFailed(old.Info.Label));
                return;
            }

            Inherit(old.Screen, screen.Screen);
            _say(Strings.DeviceBandTookOver(screen.Info.Label, old.Info.Label));
            Changed?.Invoke(this, EventArgs.Empty);
        };

        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        row.Children.Add(new TextBlock { Text = Strings.DeviceBandTakeOverLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(choice);
        row.Children.Add(take);
        band.Children.Add(row);
    }

    /// <summary>The control's half of a take-over: the old screen's tile place and view rotation go to the new one.</summary>
    private void Inherit(ScreenRef old, ScreenRef fresh)
    {
        var from = new ScreenKey(old.Device.Value, old.Screen.Value);
        var to = new ScreenKey(fresh.Device.Value, fresh.Screen.Value);

        _settings.Update(configuration =>
        {
            var view = configuration.KnownScreens.FirstOrDefault(known => known.DeviceId == from.DeviceId && known.ScreenId == from.ScreenId)?.View;
            var order = configuration.TileOrder.Contains(from)
                ? configuration.TileOrder.Where(key => key != to).Select(key => key == from ? to : key)
                : configuration.TileOrder;

            return configuration with
            {
                TileOrder = [.. order],
                KnownScreens =
                [
                    .. configuration.KnownScreens
                        .Where(known => !(known.DeviceId == from.DeviceId && known.ScreenId == from.ScreenId))
                        .Select(known => known.DeviceId == to.DeviceId && known.ScreenId == to.ScreenId && view is { } turned ? known with { View = turned } : known),
                ],
            };
        });
    }

    /// <summary>
    /// The screen's own name - refused when another screen or a device carries it, across all devices
    /// (Part 3: two indistinguishable labels confuse in three places and cost a wrong decision in one).
    /// Empty gives the default back.
    /// </summary>
    private void NameRow(Grid rows, DeviceView device, ScreenView screen, IReadOnlyList<DeviceView> all)
    {
        var name = new TextBox { Text = screen.Info.CustomName ?? string.Empty, Padding = new Thickness(4, 2, 4, 2), MinWidth = 180 };
        var hint = new TextBlock { Text = Strings.DeviceBandEmptyIsDefaultName, Foreground = Receding, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) };

        async Task KeepAsync()
        {
            var typed = name.Text.Trim();

            if (string.Equals(typed, screen.Info.CustomName ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            var taken = all.Select(other => other.Name)
                .Concat(all.SelectMany(other => other.Screens).Where(other => other.Screen != screen.Screen).Select(other => other.Info.Label));

            if (typed.Length > 0 && taken.Contains(typed, StringComparer.OrdinalIgnoreCase))
            {
                _say(Strings.DeviceBandScreenNameTaken(typed));
                name.Text = screen.Info.CustomName ?? string.Empty;
                return;
            }

            // Empty travels as an empty name: null means "unchanged" on the wire, and an empty one
            // is how the device takes its name away again.
            await ApplyAsync(device, screen, new ScreenSettings(CustomName: typed)).ConfigureAwait(true);
        }

        name.LostFocus += async (_, _) => await KeepAsync().ConfigureAwait(true);
        name.KeyDown += async (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                await KeepAsync().ConfigureAwait(true);
            }
        };

        Place(rows, Strings.DeviceBandName, name, hint, reset: null);
    }

    /// <summary>
    /// The device's name - in the lists, and at the device, whose screens' default labels follow it
    /// (Part 7). Refused when another device or a screen carries it, across all devices (Part 3). Set
    /// while the device is off, it goes out when it connects.
    /// </summary>
    private void DeviceNameRow(Grid rows, DeviceView device, IReadOnlyList<DeviceView> all)
    {
        var name = new TextBox { Text = device.Name, Padding = new Thickness(4, 2, 4, 2), MinWidth = 180 };

        async Task KeepAsync()
        {
            var typed = name.Text.Trim();

            if (typed.Length == 0 || string.Equals(typed, device.Name, StringComparison.Ordinal))
            {
                name.Text = device.Name;
                return;
            }

            var taken = all.Where(other => other.Device != device.Device).Select(other => other.Name)
                .Concat(all.SelectMany(other => other.Screens).Select(screen => screen.Info.Label));

            if (taken.Contains(typed, StringComparer.OrdinalIgnoreCase))
            {
                _say(Strings.DeviceBandDeviceNameTaken(typed));
                name.Text = device.Name;
                return;
            }

            if (!await _session.SendDeviceCommandAsync(device.Device, DeviceCommand.Rename, typed).ConfigureAwait(true))
            {
                name.Text = device.Name;
                return;
            }

            _settings.Update(configuration => configuration with
            {
                KnownDevices = [.. configuration.KnownDevices.Select(known => known.DeviceId == device.Device.Value ? known with { Name = typed } : known)],
            });

            _say(device.Connected ? Strings.DeviceBandRenamed(device.Name, typed) : Strings.DeviceBandRenamedLater(device.Name, typed));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        name.LostFocus += async (_, _) => await KeepAsync().ConfigureAwait(true);
        name.KeyDown += async (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                await KeepAsync().ConfigureAwait(true);
            }
        };

        Place(rows, Strings.DeviceBandName, name, null, reset: null);
    }

    private async Task CommandAsync(DeviceView device, DeviceCommand command, string done)
    {
        _say(await _session.SendDeviceCommandAsync(device.Device, command).ConfigureAwait(true)
            ? done
            : Strings.DeviceBandNotConnectedSay(device.Name));
    }

    private bool Ask(string question, string title) =>
        MessageBox.Show(_owner, question, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void StateRow(Grid rows, ScreenView screen)
    {
        var state = new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };

        foreach (var wish in new[] { ScreenState.Enabled, ScreenState.Diagnostic, ScreenState.Disabled, ScreenState.Inactive })
        {
            state.Items.Add(new ComboBoxItem { Content = StateLabel(wish), Tag = wish });
        }

        state.SelectedItem = state.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, screen.State));
        state.SelectionChanged += async (_, _) =>
        {
            if (state.SelectedItem is ComboBoxItem { Tag: ScreenState wish } && wish != screen.State)
            {
                await _session.SetScreenStateAsync(screen.Screen, wish).ConfigureAwait(true);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };

        Place(rows, Strings.DeviceBandState, state, null, reset: null);
    }

    /// <summary>One parameter: its label, its value in the editor its kind asks for, the unit, and "Reset" when it was set.</summary>
    private static void Row(
        Grid rows, DisplayParameter parameter, object value, bool atDefault, bool canReset, Func<object, Task> set, Func<Task> reset)
    {
        FrameworkElement editor = parameter.Kind switch
        {
            ParameterKind.Toggle => Toggle((bool)value, set),
            ParameterKind.Choice => Choice(parameter, (string)value, set),
            _ => Number(parameter, (double)value, set),
        };

        if (editor is System.Windows.Controls.Control control)
        {
            control.Foreground = atDefault ? Receding : Standing;
            control.FontStyle = atDefault ? FontStyles.Italic : FontStyles.Normal;
        }

        var after = new TextBlock
        {
            Text = atDefault ? AtDefault(parameter.Unit) : parameter.Unit ?? string.Empty,
            Foreground = Receding,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 6),
        };

        Button? back = null;

        if (!atDefault && canReset)
        {
            back = new Button { Content = Strings.DeviceBandReset, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 6), ToolTip = Strings.DeviceBandResetTip };
            back.Click += async (_, _) => await reset().ConfigureAwait(true);
        }

        Place(rows, parameter.Label, editor, after, back);
    }

    private static TextBox Number(DisplayParameter parameter, double value, Func<object, Task> set)
    {
        var format = "F" + parameter.Decimals.ToString(CultureInfo.InvariantCulture);
        var box = new TextBox { Text = value.ToString(format, CultureInfo.CurrentCulture), Padding = new Thickness(4, 2, 4, 2), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var shown = box.Text;

        async Task KeepAsync()
        {
            if (box.Text == shown)
            {
                return;
            }

            if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var typed)
                || typed < parameter.Min || typed > parameter.Max)
            {
                box.ToolTip = Strings.DeviceBandRange(parameter.Min.ToString(format, CultureInfo.CurrentCulture), parameter.Max.ToString(format, CultureInfo.CurrentCulture));
                box.Text = shown;
                return;
            }

            shown = box.Text;
            await set(Math.Round(typed, parameter.Decimals)).ConfigureAwait(true);
        }

        box.LostFocus += async (_, _) => await KeepAsync().ConfigureAwait(true);
        box.KeyDown += async (_, key) =>
        {
            if (key.Key is System.Windows.Input.Key.Enter)
            {
                await KeepAsync().ConfigureAwait(true);
            }
        };

        return box;
    }

    private static CheckBox Toggle(bool value, Func<object, Task> set)
    {
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };

        box.Click += async (_, _) => await set(box.IsChecked is true).ConfigureAwait(true);

        return box;
    }

    private static ComboBox Choice(DisplayParameter parameter, string value, Func<object, Task> set)
    {
        var box = new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };

        // The words in the language of this control, the value as the files and the wire carry it.
        foreach (var choice in parameter.Choices ?? [])
        {
            var item = new ComboBoxItem { Content = DisplayParameters.ChoiceLabel(choice), Tag = choice };

            box.Items.Add(item);

            if (choice == value)
            {
                box.SelectedItem = item;
            }
        }

        box.SelectionChanged += async (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string chosen } && chosen != value)
            {
                await set(chosen).ConfigureAwait(true);
            }
        };

        return box;
    }

    private async Task ApplyAsync(DeviceView device, ScreenView screen, ScreenSettings settings)
    {
        if (settings.IsEmpty)
        {
            return;
        }

        await _session.ApplyConfigAsync(device.Device, new ConfigUpdate([new ScreenConfigUpdate(screen.Screen.Screen, settings)])).ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyAsync(DeviceView device, DeviceSettings settings)
    {
        await _session.ApplyConfigAsync(device.Device, new ConfigUpdate([], settings)).ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "Unpair" - the one grip in the device management behind which an arrangement disappears for
    /// good (Part 7): afterwards the hub cannot address its screens, and undo does not reach behind it.
    /// So the question names screens and pictures, and offers to save every occupied screen as a scene
    /// first - not as a layout, which would address exactly the screens that are about to be gone.
    /// </summary>
    private async Task UnpairAsync(DeviceView device)
    {
        var occupied = new List<(ScreenView Screen, SceneState Scene)>();

        foreach (var screen in device.Screens)
        {
            var scene = await _session.GetSceneAsync(screen.Screen).ConfigureAwait(true);

            if (scene.Items.Count > 0 || scene.Background is not null)
            {
                occupied.Add((screen, scene));
            }
        }

        var answer = UnpairQuestion.Ask(_owner, device, occupied, _desk);

        if (answer is null)
        {
            return;
        }

        if (answer.Count > 0 && _desk.Open is { } open)
        {
            foreach (var (screen, scene, name) in answer)
            {
                open.Shelf.SaveScene(name, screen.Info.Size, scene, overwrite: false);
            }

            ScenesSaved?.Invoke(this, EventArgs.Empty);
        }

        await _session.UnpairAsync(device.Device).ConfigureAwait(true);

        _say(answer.Count == 0
            ? Strings.DeviceBandUnpaired(device.Name)
            : Strings.DeviceBandUnpairedSaved(device.Name, string.Join(", ", answer.Select(saved => saved.Name))));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Columns(Grid rows)
    {
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    private static void Place(Grid rows, string label, FrameworkElement editor, FrameworkElement? after, Button? reset)
    {
        var row = rows.RowDefinitions.Count;

        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), TextWrapping = TextWrapping.Wrap };

        if (editor.Margin == default)
        {
            editor.Margin = new Thickness(0, 0, 0, 6);
        }

        foreach (var (element, column) in new (FrameworkElement? Element, int Column)[] { (text, 0), (editor, 1), (after, 2), (reset, 3) })
        {
            if (element is null)
            {
                continue;
            }

            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            rows.Children.Add(element);
        }
    }

    private static string AtDefault(string? unit) => unit is null ? Strings.DeviceBandDefault : Strings.DeviceBandUnitDefault(unit);

    /// <summary>A screen state as the DM reads it - the enum name stays what is stored and compared.</summary>
    internal static string StateLabel(ScreenState state) => state switch
    {
        ScreenState.Enabled => Strings.DeviceBandStateEnabled,
        ScreenState.Diagnostic => Strings.DeviceBandStateDiagnostic,
        ScreenState.Disabled => Strings.DeviceBandStateDisabled,
        ScreenState.Inactive => Strings.DeviceBandStateInactive,
        _ => state.ToString(),
    };

    private static Button Button(string text) =>
        new() { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) };

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) };
}

/// <summary>
/// The question before an unpair (Part 7): what goes - screens and pictures - and, for every occupied
/// screen, a name field to save it as a scene first, in ONE dialog. Answers with the scenes to save
/// (empty for "unpair without saving"), or null for "cancel".
/// </summary>
internal sealed class UnpairQuestion : Window
{
    private readonly List<(ScreenView Screen, SceneState Scene, TextBox Name, TextBlock Problem)> _fields = [];
    private readonly CampaignDesk _desk;
    private IReadOnlyList<(ScreenView Screen, SceneState Scene, string Name)>? _answer;

    private UnpairQuestion(Window owner, DeviceView device, IReadOnlyList<(ScreenView Screen, SceneState Scene)> occupied, CampaignDesk desk)
    {
        _desk = desk;
        Owner = owner;
        Title = Strings.DevicesUnpairTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var pictures = occupied.Sum(pair => pair.Scene.Items.Count + (pair.Scene.Background is null ? 0 : 1));
        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Text(occupied.Count == 0
            ? Strings.DevicesUnpairQuestion(device.Name, device.Screens.Count)
            : Strings.DevicesUnpairQuestionOccupied(device.Name, device.Screens.Count, pictures, occupied.Count)));

        var stamp = FileNames.Stamp(DateTimeOffset.Now);

        foreach (var (screen, scene) in occupied)
        {
            var name = new TextBox { Text = FileNames.Suggest(Strings.DevicesUnpairSceneName(screen.Info.Label, stamp)), Padding = new Thickness(4, 2, 4, 2) };
            var problem = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

            panel.Children.Add(Text(Strings.DevicesUnpairSceneFrom(screen.Info.Label)));
            panel.Children.Add(name);
            panel.Children.Add(problem);
            _fields.Add((screen, scene, name, problem));
        }

        var save = new Button { Content = Strings.DevicesUnpairSaveAndUnpair, IsDefault = occupied.Count > 0, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var plain = new Button { Content = occupied.Count == 0 ? Strings.DevicesUnpairPlain : Strings.DevicesUnpairWithoutSaving, IsDefault = occupied.Count == 0, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.DevicesUnpairCancel, IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };

        save.Visibility = occupied.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (occupied.Count > 0 && desk.Open is null)
        {
            save.IsEnabled = false;
            panel.Children.Add(Text(Strings.DevicesUnpairNoCampaign));
        }

        save.Click += (_, _) => Save();
        plain.Click += (_, _) =>
        {
            _answer = [];
            DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

        buttons.Children.Add(save);
        buttons.Children.Add(plain);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    internal static IReadOnlyList<(ScreenView Screen, SceneState Scene, string Name)>? Ask(
        Window owner, DeviceView device, IReadOnlyList<(ScreenView Screen, SceneState Scene)> occupied, CampaignDesk desk)
    {
        var question = new UnpairQuestion(owner, device, occupied, desk);

        return question.ShowDialog() == true ? question._answer : null;
    }

    /// <summary>Checks every typed name - a file name, not taken, not twice in this dialog - and only then answers.</summary>
    private void Save()
    {
        if (_desk.Open is { Unnamed: true } && !CampaignNaming.Ask(this, _desk))
        {
            return;
        }

        if (_desk.Open is not { } open)
        {
            return;
        }

        var taken = open.Shelf.Scenes.Select(scene => scene.Name).ToHashSet(FileNames.Comparer);
        var ok = true;

        foreach (var (_, _, name, problem) in _fields)
        {
            var typed = name.Text.Trim();

            problem.Text = FileNames.Check(typed) is { } bad
                ? NameDialog.Explain(bad)
                : !taken.Add(typed) ? Strings.DevicesUnpairSceneExists(typed) : string.Empty;

            ok &= problem.Text.Length == 0;
        }

        if (!ok)
        {
            return;
        }

        _answer = [.. _fields.Select(field => (field.Screen, field.Scene, field.Name.Text.Trim()))];
        DialogResult = true;
    }

    private static TextBlock Text(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
}
