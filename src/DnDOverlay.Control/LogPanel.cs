using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The log panel (Part 7, Part 8): our own lines and the forwarded ones in ONE stream, with the
/// filter bar at its head - a bubble for the control, and for every device a group: the device first,
/// its screens behind it.
/// <para>
/// <b>A view onto the ring that is already there</b> - never a second place where lines are kept or
/// worded. A line reads <c>time · level · source · id name · sentence</c> and stops there: the named
/// values belong in the file, which goes into a bug report; the DM reads the sentence (Part 8).
/// </para>
/// <para>
/// <b>Colour speeds reading and carries nothing alone</b>: a narrow bar at each line's leading edge in
/// its source's colour, and the source's name as text in every line - a copied line has no colour.
/// The control has a colour outside the devices' circle; every device a hue, its screens shades of it.
/// Given out per start in the order of the device list and held for the life of the process.
/// </para>
/// <para>
/// <b>The filter is a view and is not remembered</b> - a filter set three weeks ago would be lines
/// missing without the reason in view (Part 8).
/// </para>
/// </summary>
internal sealed class LogPanel : DockPanel, IDisposable
{
    /// <summary>Lines held - the size of the ring behind them; what falls out of it is in the file.</summary>
    private const int Capacity = LogRing.DefaultCapacity;

    /// <summary>Lines taken per pass, so a device raised to Debug never holds the UI thread long.</summary>
    private const int PerPass = 200;

    private static readonly SolidColorBrush Paper = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush Ink = new(Color.FromRgb(0xDC, 0xDC, 0xDC));

    /// <summary>The control's own colour, outside the devices' circle - the one source that always speaks.</summary>
    private static readonly Color Own = Color.FromRgb(0xC8, 0xC8, 0xC8);

    /// <summary>
    /// Six hues that differ in lightness too, not only in hue, so they part under red-green weakness;
    /// beyond six they repeat, and then the name carries it (Part 8).
    /// </summary>
    private static readonly Color[] Hues =
    [
        Color.FromRgb(0x2F, 0x6F, 0xD0),
        Color.FromRgb(0xF2, 0x9E, 0x38),
        Color.FromRgb(0x2E, 0x8B, 0x57),
        Color.FromRgb(0xE6, 0xD3, 0x5A),
        Color.FromRgb(0x9B, 0x4D, 0xCA),
        Color.FromRgb(0x5F, 0xD0, 0xF0),
    ];

    private readonly ProcessLog _log;
    private readonly string _own;
    private readonly LogFilter _filter = new();
    private readonly WrapPanel _bar = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _lines = new();
    private readonly List<LogRecord> _records = [];
    private readonly Dictionary<DeviceId, int> _hueOf = [];

    private IReadOnlyList<DeviceView> _devices = [];
    private long _mark;
    private int _queued;
    private bool _following = true;
    private bool _disposed;

    internal LogPanel(ProcessLog log, string own)
    {
        _log = log;
        _own = own;

        Background = SystemColors.ControlBrush;

        _scroll = new ScrollViewer
        {
            Content = _lines,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Paper,
            Padding = new Thickness(4, 6, 8, 6),
        };

        _scroll.ScrollChanged += OnScrolled;

        // At the head and not scrolling with the lines - it wraps between groups when the panel is narrow.
        SetDock(_bar, Dock.Top);
        Children.Add(_bar);
        Children.Add(_scroll);

        Bubbles();

        // The mark before the subscription: a record between the two lies after the mark and is found
        // by the first drain; one after it queues a drain that finds nothing new (Part 8).
        _mark = Math.Max(0, _log.Ring.Mark - Capacity);
        _log.Added += OnAdded;

        Drain();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.Added -= OnAdded;
        _scroll.ScrollChanged -= OnScrolled;
    }

    /// <summary>
    /// The devices, for the bubbles: every known source, not only the ones that spoke - a bar that grew
    /// with each line would jump under the finger. Dimmed is only what is not connected.
    /// </summary>
    internal void Know(IReadOnlyList<DeviceView> devices)
    {
        _devices = devices;

        foreach (var device in devices.Where(device => !_hueOf.ContainsKey(device.Device)))
        {
            _hueOf[device.Device] = _hueOf.Count;
        }

        Bubbles();
        Refilter();
    }

    private void Bubbles()
    {
        _bar.Children.Clear();

        _bar.Children.Add(Group(
        [
            Bubble(_own, Own, _filter.ControlHidden ? Showing.None : Showing.All, dim: false, () => _filter.ToggleControl(), () => _filter.Only(null, null, Screens())),
        ]));

        foreach (var device in _devices)
        {
            var hue = HueOf(device.Device);
            var screens = device.Screens.Select(screen => screen.Screen.Screen).ToList();
            var bubbles = new List<Border>
            {
                Bubble(device.Name, hue, _filter.Of(device.Device, screens), !device.Connected, () => _filter.ToggleDevice(device.Device, screens), () => _filter.Only(device.Device, null, Screens())),
            };

            for (var index = 0; index < device.Screens.Count; index++)
            {
                var screen = device.Screens[index];

                bubbles.Add(Bubble(
                    screen.Info.Label,
                    Shade(hue, index),
                    _filter.Shows(device.Device, screen.Screen.Screen) ? Showing.All : Showing.None,
                    !device.Connected,
                    () => _filter.ToggleScreen(device.Device, screen.Screen.Screen, screens),
                    () => _filter.Only(device.Device, screen.Screen.Screen, Screens())));
            }

            _bar.Children.Add(Group(bubbles));
        }
    }

    /// <summary>A device and its screens as ONE element with inner separators - the shape says which screen hangs on which machine.</summary>
    private static Border Group(List<Border> bubbles)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        for (var index = 0; index < bubbles.Count; index++)
        {
            if (index > 0)
            {
                row.Children.Add(new Rectangle { Width = 1, Fill = SystemColors.ControlDarkBrush, Margin = new Thickness(2, 6, 2, 6) });
            }

            row.Children.Add(bubbles[index]);
        }

        return new Border
        {
            Child = row,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 12, 6),
        };
    }

    /// <summary>
    /// One bubble: filled when shown, outlined when hidden, half filled for a device with some screens
    /// hidden; a tap switches it, the right button or a long press opens "Only these" and "Show all".
    /// At least 40 DIP to hit (rule 6).
    /// </summary>
    private Border Bubble(string label, Color color, Showing showing, bool dim, Action toggle, Action only)
    {
        var brush = new SolidColorBrush(color);
        var bubble = new Border
        {
            MinHeight = 40,
            MinWidth = 40,
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(18),
            BorderBrush = brush,
            BorderThickness = new Thickness(2),
            Background = showing switch
            {
                Showing.All => brush,
                Showing.Partly => new LinearGradientBrush(
                    [new GradientStop(color, 0), new GradientStop(color, 0.5), new GradientStop(Colors.Transparent, 0.5), new GradientStop(Colors.Transparent, 1)],
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 0)),
                _ => Brushes.Transparent,
            },
            Opacity = dim ? 0.45 : 1,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = showing is Showing.All && Lightness(color) > 0.6 ? Brushes.Black : showing is Showing.All ? Brushes.White : SystemColors.ControlTextBrush,
            },
            ToolTip = dim ? Strings.LogPanelNotConnected(label) : label,
        };

        var menu = new ContextMenu();
        var onlyThese = new MenuItem { Header = Strings.LogPanelOnlyThese };
        var all = new MenuItem { Header = Strings.LogPanelShowAll };

        onlyThese.Click += (_, _) => Changed(only);
        all.Click += (_, _) => Changed(_filter.ShowAll);
        menu.Items.Add(onlyThese);
        menu.Items.Add(all);
        bubble.ContextMenu = menu;

        bubble.MouseLeftButtonUp += (_, _) => Changed(toggle);

        return bubble;
    }

    private void Changed(Action change)
    {
        change();
        Bubbles();
        Refilter();
    }

    private Dictionary<DeviceId, IReadOnlyCollection<ScreenId>> Screens() =>
        _devices.ToDictionary(device => device.Device, device => (IReadOnlyCollection<ScreenId>)[.. device.Screens.Select(screen => screen.Screen.Screen)]);

    private IReadOnlyCollection<ScreenId> Known(DeviceId device) =>
        _devices.FirstOrDefault(known => known.Device == device)?.Screens.Select(screen => screen.Screen.Screen).ToList() ?? [];

    private void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0)
        {
            return;
        }

        _following = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
    }

    private void OnAdded(LogRecord record)
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1)
        {
            return;
        }

        // Background priority: a view of the load must not become part of it (Part 1).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Drain);
    }

    private void Drain()
    {
        Interlocked.Exchange(ref _queued, 0);

        if (_disposed)
        {
            return;
        }

        var records = _log.Ring.Since(_mark, LogLevel.Trace, PerPass, out var next, out var lost);

        _mark = next;

        if (lost > 0)
        {
            _lines.Children.Add(Plain(Strings.LogPanelLinesDropped(lost)));
        }

        foreach (var record in records)
        {
            _records.Add(record);

            if (_filter.Shows(record, Known))
            {
                _lines.Children.Add(Line(record));
            }
        }

        if (_records.Count > Capacity)
        {
            _records.RemoveRange(0, _records.Count - Capacity);
        }

        while (_lines.Children.Count > Capacity)
        {
            _lines.Children.RemoveAt(0);
        }

        if (_following)
        {
            _scroll.ScrollToEnd();
        }

        if (records.Count == PerPass)
        {
            OnAdded(records[^1]);
        }
    }

    /// <summary>Draws the stream again for the filter as it stands now - the lines are all still here.</summary>
    private void Refilter()
    {
        _lines.Children.Clear();

        foreach (var record in _records.Where(record => _filter.Shows(record, Known)))
        {
            _lines.Children.Add(Line(record));
        }

        if (_following)
        {
            _scroll.ScrollToEnd();
        }
    }

    /// <summary>
    /// One line: the colour bar of its source, then time, level as a WORD, source as text, id and name,
    /// the sentence - from <see cref="LogCatalog.Render"/> and from nowhere else (Part 8).
    /// </summary>
    private Grid Line(LogRecord record)
    {
        var (name, color) = SourceOf(record);
        var text = new StringBuilder(96)
            .Append(record.Received.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(record.Level.ToString().PadRight(11))
            .Append("  ")
            .Append(name)
            .Append("  ")
            .Append(record.EventId.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(record.EventName)
            .Append("  ")
            .Append(LogCatalog.Render(record, _log.Culture))
            .ToString();

        var line = new Grid { Margin = new Thickness(0, 0, 0, 1) };

        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Rectangle { Fill = new SolidColorBrush(color) };
        var words = Plain(text);

        words.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(words, 1);

        line.Children.Add(bar);
        line.Children.Add(words);

        return line;
    }

    /// <summary>The source's name as it stands on the tiles - device, and screen where the line has one the control knows - and its colour.</summary>
    private (string Name, Color Color) SourceOf(LogRecord record)
    {
        if (record.Source is not { } source)
        {
            return (_own, Own);
        }

        var hue = HueOf(source.Device);
        var device = _devices.FirstOrDefault(known => known.Device == source.Device);

        if (record.Screen is { } screen && device is not null)
        {
            for (var index = 0; index < device.Screens.Count; index++)
            {
                if (device.Screens[index].Screen.Screen == screen)
                {
                    return (device.Screens[index].Info.Label, Shade(hue, index));
                }
            }
        }

        return (device?.Name ?? source.Name, hue);
    }

    private Color HueOf(DeviceId device)
    {
        if (!_hueOf.TryGetValue(device, out var index))
        {
            index = _hueOf.Count;
            _hueOf[device] = index;
        }

        return Hues[index % Hues.Length];
    }

    /// <summary>A screen's shade of its device's hue - three steps toward white, then they repeat.</summary>
    private static Color Shade(Color hue, int screen)
    {
        var mix = 0.25 + (0.18 * (screen % 3));

        return Color.FromRgb(
            (byte)(hue.R + ((255 - hue.R) * mix)),
            (byte)(hue.G + ((255 - hue.G) * mix)),
            (byte)(hue.B + ((255 - hue.B) * mix)));
    }

    private static double Lightness(Color color) => ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;

    private static TextBlock Plain(string text) =>
        new()
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
        };
}
