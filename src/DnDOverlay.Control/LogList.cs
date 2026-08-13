using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DnDOverlay.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The running log, on screen. A view onto the ring buffer that is already there - never a second
/// place where messages are kept, and never a second place where they are worded.
/// <para>
/// It exists this early for one reason: acceptance step 49 asks that a fault raised at a display
/// <i>appears in the control</i>, and in the two-machine run of M1c the control is the only live
/// window onto the other machine - that one's file lies over there. The file here already carries
/// every forwarded line (Part 8); what this adds is seeing them without leaving the program.
/// </para>
/// <para>
/// <b>This is not the log panel of Part 7.</b> The filter bar with its bubbles, the source colours,
/// docking and a remembered state are M5b and belong beside the campaign panel. What makes a flat
/// list bearable until then is the promise that already holds: the source stands in every line as
/// text.
/// </para>
/// </summary>
internal sealed class LogList : DockPanel, IDisposable
{
    /// <summary>
    /// How many lines are held. The same size as the ring behind it, because holding more would be
    /// a claim to history the buffer cannot back - what falls out of it is in the file.
    /// </summary>
    private const int Capacity = LogRing.DefaultCapacity;

    /// <summary>
    /// Lines taken per pass. A display raised to <see cref="LogLevel.Debug"/> produces hundreds a
    /// second (Part 4), and draining all of them in one go would be a pause on the UI thread. What
    /// is left over is picked up by the next pass; the mark makes that free.
    /// </summary>
    private const int PerPass = 200;

    private static readonly SolidColorBrush Paper = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush Ink = new(Color.FromRgb(0xDC, 0xDC, 0xDC));

    private readonly ProcessLog _log;
    private readonly string _own;
    private readonly ScrollViewer _scroll;
    private readonly ItemsControl _lines = new();

    private long _mark;
    private int _queued;
    private bool _following = true;
    private bool _disposed;

    /// <param name="own">
    /// What to call a line this process wrote. Handed in rather than taken from the record, because
    /// a record from here carries no source at all - the absence IS the statement (Part 8).
    /// </param>
    internal LogList(ProcessLog log, string own)
    {
        _log = log;
        _own = own;

        _scroll = new ScrollViewer
        {
            Content = _lines,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Paper,
            Padding = new Thickness(8, 6, 8, 6),
        };

        _scroll.ScrollChanged += OnScrolled;

        var heading = new TextBlock
        {
            Text = "Log",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4),
        };

        SetDock(heading, Dock.Top);
        Children.Add(heading);
        Children.Add(_scroll);

        // The mark is taken BEFORE subscribing, and that order is the whole correctness argument.
        // A record arriving between the two is not signalled but lies after the mark, so the first
        // drain finds it; one arriving after the subscription queues a drain that finds nothing new.
        // Neither a gap nor a double is possible, and no lock is needed for it (Part 8).
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
    /// Follows the end until the reader scrolls away from it, and follows again once they come
    /// back. Without this a running list is unreadable - the one behaviour it genuinely needs.
    /// </summary>
    private void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        // Only a scroll the reader made says anything about what they want; the offset changing
        // because a line was appended does not.
        if (e.ExtentHeightChange != 0)
        {
            return;
        }

        _following = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
    }

    /// <summary>
    /// Raised on whatever thread wrote the line - Kestrel's, the hub's, a device's forwarding -
    /// so nothing is touched here beyond asking the UI thread for a pass. Passes coalesce: under a
    /// flood the list falls behind by lines, never by queued work items.
    /// </summary>
    private void OnAdded(LogRecord record)
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1)
        {
            return;
        }

        // Background, so drawing the log can never compete with the DM's own input. It is the
        // priority order of Part 1 in miniature: a view of the load must not become part of it.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Drain);
    }

    private void Drain()
    {
        // Cleared first, so a record arriving during this pass queues the next one instead of
        // being swallowed by a flag that is still set.
        Interlocked.Exchange(ref _queued, 0);

        if (_disposed)
        {
            return;
        }

        var records = _log.Ring.Since(_mark, LogLevel.Trace, PerPass, out var next, out var lost);

        _mark = next;

        // Said rather than hidden. A number that is silently wrong is worse than none, and this one
        // can only ever mean the list could not keep up with the buffer (Part 8).
        if (lost > 0)
        {
            Append(string.Create(CultureInfo.InvariantCulture, $"... {lost} lines dropped before this view saw them"));
        }

        foreach (var record in records)
        {
            Append(Line(record));
        }

        while (_lines.Items.Count > Capacity)
        {
            _lines.Items.RemoveAt(0);
        }

        if (_following)
        {
            _scroll.ScrollToEnd();
        }

        // The pass was full, so there may be more waiting. Asking for another is what keeps a
        // flood moving without ever holding the thread for longer than one pass.
        if (records.Count == PerPass)
        {
            OnAdded(records[^1]);
        }
    }

    private void Append(string text) =>
        _lines.Items.Add(new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
        });

    /// <summary>
    /// One line, arranged like the one in the file: time, level as a WORD, source, the identifier
    /// as number and name, the sentence, then the named values.
    /// <para>
    /// The sentence comes from <see cref="LogCatalog.Render"/> and from nowhere else. Wording it
    /// here would be a second renderer, and the two would disagree exactly where the fallback
    /// stages matter - on an identifier one side does not know (Part 8).
    /// </para>
    /// </summary>
    private string Line(LogRecord record)
    {
        var line = new StringBuilder(96);

        line.Append(record.Received.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(record.Level.ToString().PadRight(11))
            .Append("  ")
            .Append((record.Source?.Name ?? _own).PadRight(16))
            .Append("  ")
            .Append(record.EventId.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(record.EventName.PadRight(24))
            .Append("  ")
            .Append(LogCatalog.Render(record));

        if (record.Values.Count > 0)
        {
            line.Append("  {");

            for (var index = 0; index < record.Values.Count; index++)
            {
                if (index > 0)
                {
                    line.Append(", ");
                }

                line.Append(record.Values[index].Name).Append('=').Append(record.Values[index].Text);
            }

            line.Append('}');
        }

        return line.ToString();
    }
}
