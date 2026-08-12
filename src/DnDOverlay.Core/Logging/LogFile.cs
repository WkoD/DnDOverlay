using System.Globalization;
using System.Text;

namespace DnDOverlay.Core.Logging;

/// <summary>
/// The rolling file log: one line per message, rolled over by SIZE and by nothing else.
/// <para>
/// <b>There is no time limit, and there is no setting that could introduce one.</b> Play happens
/// in bursts, not on consecutive days, so "keep a few days" would throw away exactly the evening
/// one wants to compare against - the one three weeks ago - and keep six days on which the
/// application never ran (Part 8). A size budget does not know that mistake. Making it a property
/// of the construction rather than four flags set correctly is the whole reason this class is
/// hand-written.
/// </para>
/// <para>
/// It never throws. A logger that throws takes its caller with it, and on the display that caller
/// is the UI thread - a full disk would become a crash. The first failure is reported once,
/// through <see cref="Failed"/>, and after that it stays quiet and keeps the process running.
/// </para>
/// </summary>
public sealed class LogFile : IDisposable
{
    private const string LineTimestamp = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";
    private const string HeaderTimestamp = "yyyy-MM-dd'T'HH:mm:sszzz";

    /// <summary>
    /// No byte order mark. The file is appended to and read by everything from Notepad to grep;
    /// a BOM helps none of them and confuses some.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _baseName;
    private readonly LogFileLimits _limits;
    private readonly LogIdentity _identity;
    private readonly TimeProvider _time;

    private StreamWriter? _writer;
    private long _written;
    private int _number;
    private bool _opened;
    private bool _broken;

    public LogFile(
        string directory,
        string baseName,
        LogFileLimits limits,
        LogIdentity identity,
        TimeProvider time)
    {
        _directory = directory;
        _baseName = baseName;
        _limits = limits;
        _identity = identity;
        _time = time;
    }

    /// <summary>
    /// Raised once, the first time writing fails - never again for the same file, or a full disk
    /// would produce a message per line about not being able to produce messages.
    /// <para>
    /// It deliberately does NOT go back through <c>ILogger</c>: that would return straight into
    /// the sink that is failing. Whoever subscribes puts it into the ring buffer and in front of
    /// the DM (Part 6).
    /// </para>
    /// </summary>
    public event Action<string>? Failed;

    /// <summary>The file being written, or null while nothing has been opened.</summary>
    public string? Path { get; private set; }

    /// <summary>Appends one rendered line. Silent about its own failures after the first.</summary>
    public void Write(LogRecord record, string text)
    {
        ArgumentNullException.ThrowIfNull(record);

        var line = Line(record, text);

        lock (_gate)
        {
            if (_broken)
            {
                return;
            }

            // Counted in bytes rather than characters: the promise is about the size of the file,
            // and a German line with umlauts is longer than it looks.
            var bytes = Utf8.GetByteCount(line) + Environment.NewLine.Length;

            if (_writer is null)
            {
                Open();
            }
            else if (_written + bytes > _limits.BytesPerFile)
            {
                Roll();
            }

            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.WriteLine(line);
                _written += bytes;
            }
            catch (IOException exception)
            {
                Break(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>
    /// One line: timestamp, level, source, identifier as NUMBER AND NAME, the sentence, and the
    /// named values.
    /// <para>
    /// The last two columns are what make a line worth anything to somebody who does not read its
    /// language: the file is written in the UI language of whoever wrote it, so the identifier is
    /// the key - it is looked up in <c>docs/protocol.md</c> without reading a word (Part 8).
    /// </para>
    /// </summary>
    private string Line(LogRecord record, string text)
    {
        var line = new StringBuilder(text.Length + 96);

        line.Append(record.Received.ToString(LineTimestamp, CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(record.Level.ToString().PadRight(11))
            .Append("  ")
            .Append((record.Source?.Name ?? _identity.ShortName).PadRight(16))
            .Append("  ")
            .Append(record.EventId.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(record.EventName.PadRight(24))
            .Append("  ")
            .Append(text);

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

        // A forwarded entry carries two timestamps, and the difference is the point: sorted by
        // arrival, stamped by the device. Only shown when they actually differ, so an entry this
        // process wrote does not carry a column that always says the same thing (Part 8).
        if (record.Source is not null && record.At != record.Received)
        {
            line.Append("  [device ")
                .Append(record.At.ToString(LineTimestamp, CultureInfo.InvariantCulture))
                .Append(']');
        }

        return line.ToString();
    }

    /// <summary>Opens the newest file that still has room, or a fresh one, and writes the header.</summary>
    private void Open()
    {
        try
        {
            Directory.CreateDirectory(_directory);

            _number = Math.Max(Existing().LastOrDefault(), 1);

            var path = PathOf(_number);
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;

            // A file that is already full is not appended to - it would be over its limit before
            // the first line of this run.
            if (length >= _limits.BytesPerFile)
            {
                _number++;
                path = PathOf(_number);
                length = 0;
            }

            Start(path, length, _opened ? "new file after rollover" : "started");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Break(exception);
        }
    }

    /// <summary>Closes the current file, opens the next number and prunes the oldest away.</summary>
    private void Roll()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            _number++;

            Start(PathOf(_number), 0, "new file after rollover");
            Prune();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Break(exception);
        }
    }

    private void Start(string path, long length, string reason)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

        // Written through on every line. Buffering would trade away exactly the lines that matter
        // most - the ones just before a crash - and it buys microseconds we do not need.
        _writer = new StreamWriter(stream, Utf8) { AutoFlush = true };
        _written = length;
        _opened = true;
        Path = path;

        Header(reason);
    }

    /// <summary>
    /// Two lines that are not log entries: what wrote this, and when it started writing.
    /// <para>
    /// Written on every OPEN, not only when the file is created. Rotation follows size, not
    /// process starts, so without this the second and every later file would carry no statement
    /// about which build produced it - and the oldest retained file may well contain no start at
    /// all. On a restart into an existing file it is the mark that separates one run from the
    /// next, which after an update is the difference between two versions in one file and a
    /// riddle.
    /// </para>
    /// <para>
    /// They begin with <c>#</c> because Part 8 requires EVERY line to carry an identifier and its
    /// values, and these carry none - they are not events. The marker keeps them apart for the
    /// test and for anything that reads the file, instead of writing an exception into the rule.
    /// </para>
    /// </summary>
    private void Header(string reason)
    {
        if (_writer is null)
        {
            return;
        }

        var ui = Named(CultureInfo.CurrentUICulture);
        var system = Named(CultureInfo.InstalledUICulture);
        var now = _time.GetLocalNow().ToString(HeaderTimestamp, CultureInfo.InvariantCulture);

        string[] lines =
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"# {_identity.Application} {_identity.Version} · protocol {_identity.ProtocolVersion} · UI {ui} · system {system}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"# {now} · pid {Environment.ProcessId} · {reason}"),
        ];

        foreach (var line in lines)
        {
            _writer.WriteLine(line);
            _written += Utf8.GetByteCount(line) + Environment.NewLine.Length;
        }
    }

    private static string Named(CultureInfo culture) =>
        string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;

    /// <summary>Deletes everything beyond the retained count, oldest number first.</summary>
    private void Prune()
    {
        var numbers = Existing();

        for (var index = 0; index < numbers.Count - _limits.Keep; index++)
        {
            try
            {
                File.Delete(PathOf(numbers[index]));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file somebody has open in an editor is not worth ending the run over; it goes
                // on the next rollover.
            }
        }
    }

    /// <summary>The numbers of the files that are there, ascending.</summary>
    private List<int> Existing()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var numbers = new List<int>();

        foreach (var path in Directory.EnumerateFiles(_directory, $"{_baseName}-*.log"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var digits = name[(_baseName.Length + 1)..];

            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                numbers.Add(number);
            }
        }

        numbers.Sort();

        return numbers;
    }

    private string PathOf(int number) =>
        System.IO.Path.Combine(
            _directory,
            string.Create(CultureInfo.InvariantCulture, $"{_baseName}-{number:0000}.log"));

    private void Break(Exception exception)
    {
        _broken = true;
        _writer?.Dispose();
        _writer = null;

        Failed?.Invoke(LogText.Clean(exception.Message));
    }
}

/// <summary>
/// How much history is kept, in bytes and files. Both applications carry their own numbers
/// (Part 6, Part 7) - and neither carries an age.
/// </summary>
public readonly record struct LogFileLimits(long BytesPerFile, int Keep)
{
    /// <summary>
    /// The DM side: 10 MB in 10 files. Measured against the most expensive case rather than the
    /// ordinary one - an evening at Warning is a fraction of one file, while a display raised to
    /// Debug produces in minutes what an evening otherwise takes hours to (Part 8).
    /// </summary>
    public static LogFileLimits Control => new(10L * 1024 * 1024, 10);

    /// <summary>A display PC: 5 MB in 5 files.</summary>
    public static LogFileLimits Display => new(5L * 1024 * 1024, 5);
}

/// <summary>What the header says about the process doing the writing.</summary>
public sealed record LogIdentity(string Application, string Version, int ProtocolVersion)
{
    /// <summary>
    /// Reads name and version off an assembly. The INFORMATIONAL version is the one that matters:
    /// it carries the full SemVer that MinVer derived from the tag - the same number the update
    /// check compares against - where the assembly version has been flattened to four fields
    /// (Part 9). Build metadata after the <c>+</c> is dropped; it belongs to the build, not to the
    /// release.
    /// </summary>
    public static LogIdentity Of(System.Reflection.Assembly assembly, int protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?
            .InformationalVersion;

        var version = informational?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        return new LogIdentity(assembly.GetName().Name ?? "DnDOverlay", version, protocolVersion);
    }

    /// <summary>The column in every line: <c>Control</c> from <c>DnDOverlay.Control</c>.</summary>
    public string ShortName =>
        Application.LastIndexOf('.') is var dot && dot >= 0 && dot < Application.Length - 1
            ? Application[(dot + 1)..]
            : Application;
}
