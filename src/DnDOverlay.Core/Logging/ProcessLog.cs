using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Logging;

/// <summary>
/// The one log of a process: everything produced lands in the ring buffer and, where there is
/// one, in the file. Registered once with <c>ILoggerFactory</c> and never taken out again.
/// <para>
/// <b>That is not a detail of taste.</b> <c>ILoggerFactory</c> has <c>AddProvider</c> and no
/// counterpart, so a diagnostic log that comes and goes cannot be a provider that comes and goes
/// - the switch has to sit INSIDE it. It is a level, and it is settable while the process runs,
/// because a display PC is asked to raise it from the far side of the house while the fault is
/// happening (Part 6).
/// </para>
/// <para>
/// Both applications write a file, and the display's is on from the start rather than switched on
/// when it is needed: <b>a log that has to be turned on cannot record what happened before it was
/// turned on</b> - and a display PC's most valuable failures are its startup failures, on a
/// machine that has no keyboard. What differs between the two is the size budget and nothing
/// else (Part 6, Part 7).
/// </para>
/// </summary>
public sealed class ProcessLog : ILoggerProvider
{
    private readonly LogIdentity _identity;
    private readonly LogFile? _file;
    private readonly TimeProvider _time;

    /// <param name="directory">
    /// Where the file goes - always under the handed-in data root, never composed here (rule 10).
    /// <see langword="null"/> keeps everything in memory, which is what tests and a run with no
    /// writable root get.
    /// </param>
    public ProcessLog(
        LogIdentity identity,
        string? directory,
        LogFileLimits limits,
        TimeProvider time,
        int capacity = LogRing.DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(time);

        _identity = identity;
        _time = time;

        Ring = new LogRing(capacity);

        if (directory is null)
        {
            return;
        }

        _file = new LogFile(directory, identity.ShortName.ToLowerInvariant(), limits, identity, time);
        _file.Failed += Broke;
    }

    /// <summary>
    /// What is produced at all - one knob, and it decides for the ring buffer and the file alike.
    /// Settable while running: raising a display to <see cref="LogLevel.Debug"/> from the control
    /// is the documented way to look for a fault (Part 4, Part 8).
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>What the tray list, the log panel and the forwarding read from.</summary>
    public LogRing Ring { get; }

    /// <summary>The file being written, or null while there is none.</summary>
    public string? Path => _file?.Path;

    /// <summary>Raised after a record has been taken, so a forwarder need not poll.</summary>
    public event Action<LogRecord>? Added;

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    /// <summary>
    /// Takes one record - written here or forwarded from a device. Both go the same way, which is
    /// why the control's file contains every forwarded entry without a second code path (Part 8).
    /// </summary>
    public void Add(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Ring.Add(record);
        _file?.Write(record, LogCatalog.Render(record));

        Added?.Invoke(record);
    }

    public void Dispose()
    {
        if (_file is not null)
        {
            _file.Failed -= Broke;
            _file.Dispose();
        }
    }

    private bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= Level;

    /// <summary>
    /// The file gave up. This goes into the ring buffer and NOT back through <c>ILogger</c> -
    /// that would return straight into the sink that is failing. Whoever shows the ring puts it
    /// in front of the DM, which is what Part 6 asks for: a failed write must be visible, because
    /// from then on every further line is lost.
    /// </summary>
    private void Broke(string reason)
    {
        var now = _time.GetLocalNow();

        Ring.Add(new LogRecord(
            now,
            now,
            LogLevel.Error,
            4008,
            "LogFileFailed",
            [new LogValue("Path", LogText.Clean(_file?.Path ?? "?"))],
            reason));
    }

    /// <summary>
    /// The <c>ILogger</c> handed to callers. It holds no state of its own; the category comes
    /// along only to stand in as a name for messages from outside that carry no event name.
    /// </summary>
    private sealed class Writer(ProcessLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => log.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            // A framework message carries an identifier but not always a name; the category is
            // the honest stand-in. Without it such a line would render as an empty name in the
            // third fallback stage.
            var name = string.IsNullOrEmpty(eventId.Name) ? category : eventId.Name;
            var values = Named(state);

            var known = LogCatalog.Template(eventId.Id, name) is not null;

            // What a foreign library says travels as RAW TEXT and is shown unchanged (Part 8) -
            // there is no catalogue entry for it and there should not be one. Cleaned on the way
            // in, once, so neither the file nor the wire has to remember it again (Part 4).
            var raw = known
                ? exception?.Message
                : Both(formatter(state, exception), exception?.Message);

            var now = log._time.GetLocalNow();

            log.Add(new LogRecord(
                now,
                now,
                logLevel,
                eventId.Id,
                LogText.Clean(name),
                values,
                LogText.Clean(raw) is { Length: > 0 } cleaned ? cleaned : null,
                Source: null,
                Screen: Screen(values)));
        }

        private static string? Both(string first, string? second) =>
            string.IsNullOrEmpty(second) ? first : $"{first} - {second}";

        /// <summary>
        /// The named values of a message, turned into text right here.
        /// <para>
        /// Text rather than the objects themselves, because the ring buffer holds these for
        /// minutes: an object would keep whatever it points at alive, and one that is changed
        /// afterwards would make the buffer disagree with the file that was already written.
        /// </para>
        /// </summary>
        private static List<LogValue> Named<TState>(TState state)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                return [];
            }

            var values = new List<LogValue>(pairs.Count);

            foreach (var pair in pairs)
            {
                // The template itself is not a value - it is how the message was declared, and it
                // is already in the catalogue.
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                values.Add(new LogValue(
                    pair.Key,
                    LogText.Clean(Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture))));
            }

            return values;
        }

        /// <summary>
        /// Which screen a message is about, read off the named values by convention rather than
        /// through a second API. It is only ever the writer's OWN screen: a device names the
        /// screens it has, and the control has one bubble without subdivision (Part 8).
        /// </summary>
        private static ScreenId? Screen(IReadOnlyList<LogValue> values)
        {
            foreach (var value in values)
            {
                if (string.Equals(value.Name, "ScreenId", StringComparison.Ordinal))
                {
                    return new ScreenId(value.Text);
                }
            }

            return null;
        }
    }
}
