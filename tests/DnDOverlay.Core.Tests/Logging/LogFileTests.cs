
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Tests.Logging;

/// <summary>
/// The file log: rolled over by size and by nothing else, a header on every file, and never
/// throwing (Part 8).
/// </summary>
public sealed class LogFileTests : IDisposable
{
    private static readonly LogIdentity Identity = new("DnDOverlay.Control", "0.1.0-test", 1);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dndoverlay-log");
    private readonly ManualTime _time = new();

    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// Kept small so a test does not have to write megabytes; the mechanism is the subject, not
    /// the number.
    /// </summary>
    private LogFile Open(int bytes = 1024, int keep = 3) =>
        new(_directory.FullName, "control", new LogFileLimits(bytes, keep), Identity, _time);

    [Fact]
    public void A_line_carries_the_identifier_as_number_and_name_and_its_values()
    {
        using var file = Open();

        file.Write(Record(1024, "TokenRefused", LogLevel.Warning), "A token this control does not know.");

        var line = Lines().Single();

        Assert.Contains("1024 TokenRefused", line, StringComparison.Ordinal);
        Assert.Contains("Warning", line, StringComparison.Ordinal);
        Assert.Contains("A token this control does not know.", line, StringComparison.Ordinal);
        Assert.Contains("{DeviceName=TISCH-PC}", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file is written in the language of whoever wrote it, so a reader who does not read it
    /// needs to know what he has in front of him before the first line (Part 8).
    /// </summary>
    [Fact]
    public void Every_file_begins_with_a_header_that_is_not_a_log_line()
    {
        using var file = Open();

        file.Write(Record(1024, "TokenRefused", LogLevel.Warning), "text");

        var all = ReadLines(Written().Single());

        Assert.StartsWith("# DnDOverlay.Control 0.1.0-test | protocol 1 | UI ", all[0], StringComparison.Ordinal);
        Assert.Contains("| started", all[1], StringComparison.Ordinal);
        Assert.DoesNotContain("#", all[2], StringComparison.Ordinal);
    }

    /// <summary>
    /// Rotation follows size, so the file that survives may contain no process start at all -
    /// which is exactly why the header is written per FILE and not per start.
    /// </summary>
    [Fact]
    public void Rolling_over_writes_a_header_again_and_says_why()
    {
        using var file = Open(bytes: 400);

        for (var index = 0; index < 12; index++)
        {
            file.Write(Record(1024, "TokenRefused", LogLevel.Warning), new string('x', 120));
        }

        var files = Written();

        Assert.True(files.Count > 1, "the size limit should have produced more than one file");

        foreach (var path in files)
        {
            Assert.StartsWith("# DnDOverlay.Control", ReadLines(path)[0], StringComparison.Ordinal);
        }

        Assert.Contains("new file after rollover", ReadLines(files[^1])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_beyond_the_retained_count_survives()
    {
        using var file = Open(bytes: 400, keep: 3);

        for (var index = 0; index < 60; index++)
        {
            file.Write(Record(1024, "TokenRefused", LogLevel.Warning), new string('x', 120));
        }

        Assert.True(Written().Count <= 3, $"{Written().Count} files survived, at most 3 were allowed");
    }

    /// <summary>
    /// The promise of Part 8, and the one this class is hand-written for: play happens in bursts,
    /// so "keep a few days" would throw away the evening one wants to compare against and keep six
    /// days on which nothing ran. There is no time limit here, and no setting that could add one.
    /// </summary>
    [Fact]
    public void A_month_passing_deletes_nothing()
    {
        using var file = Open();

        file.Write(Record(1024, "TokenRefused", LogLevel.Warning), "before");

        var before = Written();

        _time.Advance(TimeSpan.FromDays(31));

        file.Write(Record(1024, "TokenRefused", LogLevel.Warning), "after");

        Assert.Equal(before, Written());
        Assert.Contains(Lines(), line => line.Contains("before", StringComparison.Ordinal));
    }

    /// <summary>
    /// A restart appends, and the second header is what tells one run from the next - after an
    /// update, two versions in one file would otherwise be a riddle.
    /// </summary>
    [Fact]
    public void A_second_run_appends_and_marks_itself()
    {
        using (var first = Open())
        {
            first.Write(Record(1024, "TokenRefused", LogLevel.Warning), "first run");
        }

        using (var second = Open())
        {
            second.Write(Record(1024, "TokenRefused", LogLevel.Warning), "second run");
        }

        var all = ReadLines(Written().Single());

        Assert.Equal(2, all.Count(line => line.StartsWith("# DnDOverlay.Control", StringComparison.Ordinal)));
        Assert.Single(Written());
    }

    /// <summary>
    /// A logger that throws takes its caller with it, and on the display that caller is the UI
    /// thread - a full disk would become a crash. The failure is reported ONCE and the process
    /// carries on (Part 6).
    /// </summary>
    [Fact]
    public void An_unwritable_place_is_reported_once_and_costs_nothing_else()
    {
        // A path whose parent is a FILE cannot become a directory, on any platform.
        var blocked = Path.Combine(_directory.FullName, "blocked");
        File.WriteAllText(blocked, "not a directory");

        using var file = new LogFile(
            Path.Combine(blocked, "logs"),
            "control",
            LogFileLimits.Control,
            Identity,
            _time);

        var reported = 0;
        file.Failed += _ => reported++;

        for (var index = 0; index < 5; index++)
        {
            file.Write(Record(1024, "TokenRefused", LogLevel.Warning), "text");
        }

        Assert.Equal(1, reported);
    }

    private static LogRecord Record(int id, string name, LogLevel level) =>
        new(
            new DateTimeOffset(2026, 8, 12, 14, 31, 7, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 12, 14, 31, 7, TimeSpan.FromHours(2)),
            level,
            id,
            name,
            [new LogValue("DeviceName", "TISCH-PC")]);

    private List<string> Written() =>
        [.. Directory.EnumerateFiles(_directory.FullName, "control-*.log").Order(StringComparer.Ordinal)];

    private List<string> Lines() =>
        [.. Written()
            .SelectMany(ReadLines)
            .Where(line => !line.StartsWith('#'))];

    /// <summary>
    /// Reads a file that is still being written. <see cref="File.ReadAllLines(string)"/> declares
    /// a share mode that excludes an open write handle, so it fails on Windows while the process
    /// is running - which is how every log file behaves and is worth knowing rather than working
    /// around in the writer.
    /// </summary>
    private static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
