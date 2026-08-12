using System.Text;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Tests.Logging;

/// <summary>
/// The catalogue's three fallback stages, the ring buffer and the one provider a process has
/// (Part 8).
/// </summary>
public sealed class ProcessLogTests
{
    private static readonly LogIdentity Identity = new("DnDOverlay.Display", "0.1.0-test", 1);

    [Fact]
    public void A_known_message_is_filled_in()
    {
        var rendered = LogCatalog.Render(Record(
            1024,
            "TokenRefused",
            [new LogValue("DeviceName", "TISCH-PC"), new LogValue("DeviceId", "aaaa-0001")]));

        Assert.Equal("TISCH-PC (aaaa-0001) presented a token this control does not know.", rendered);
    }

    /// <summary>
    /// The third stage is the point: mixed versions are exactly when a message is needed most, and
    /// an unknown identifier must never produce "unknown event" or an empty line (Part 8).
    /// </summary>
    [Fact]
    public void An_identifier_from_a_newer_counterpart_still_says_something()
    {
        var rendered = LogCatalog.Render(Record(
            2001,
            "AssetDownloadFailed",
            [new LogValue("AssetId", "ab12cd"), new LogValue("Attempt", "3")]));

        Assert.Equal("AssetDownloadFailed (AssetId=ab12cd, Attempt=3)", rendered);
    }

    [Fact]
    public void A_placeholder_without_a_value_stays_where_it_is()
    {
        var rendered = LogCatalog.Render(Record(1024, "TokenRefused", [new LogValue("DeviceName", "TISCH-PC")]));

        Assert.Equal("TISCH-PC ({DeviceId}) presented a token this control does not know.", rendered);
    }

    /// <summary>
    /// Exception texts and messages from foreign libraries travel as raw text and are shown
    /// unchanged - said out loud in Part 8 rather than treated as a stopgap.
    /// </summary>
    [Fact]
    public void Raw_text_travels_unchanged()
    {
        var record = Record(1039, "BeaconStopped", []) with { RawText = "Access to the path is denied." };

        Assert.Equal("The discovery beacon has stopped. — Access to the path is denied.", LogCatalog.Render(record));
    }

    /// <summary>
    /// Hardening, not tidiness (Part 4): a crafted device name or exception message would
    /// otherwise write lines of its own into the file - a forged header line among them.
    /// </summary>
    [Fact]
    public void Line_breaks_and_control_characters_cannot_forge_a_line()
    {
        var forged = LogText.Clean("TISCH-PC\r\n# DnDOverlay.Control 9.9.9 · protocol 1\u0007");

        Assert.Equal("TISCH-PC # DnDOverlay.Control 9.9.9 · protocol 1", forged);
        Assert.DoesNotContain('\n', forged);
        Assert.DoesNotContain('\r', forged);
    }

    [Fact]
    public void A_reader_gets_what_it_has_not_seen_and_nothing_twice()
    {
        var ring = new LogRing(capacity: 8);
        var mark = ring.Mark;

        ring.Add(Record(1, "One", []));
        ring.Add(Record(2, "Two", []));

        var first = ring.Since(mark, LogLevel.Trace, 10, out mark, out _);

        ring.Add(Record(3, "Three", []));

        var second = ring.Since(mark, LogLevel.Trace, 10, out mark, out _);

        Assert.Equal([1, 2], first.Select(record => record.EventId));
        Assert.Equal([3], second.Select(record => record.EventId));
        Assert.Empty(ring.Since(mark, LogLevel.Trace, 10, out _, out _));
    }

    /// <summary>
    /// After a long outage the buffer overflows, and the reader it was lost to is told how much.
    /// Counted per reader, because two readers at different speeds lose different amounts.
    /// </summary>
    [Fact]
    public void What_fell_out_of_the_buffer_is_counted()
    {
        var ring = new LogRing(capacity: 4);
        var mark = ring.Mark;

        for (var id = 1; id <= 10; id++)
        {
            ring.Add(Record(id, "Entry", []));
        }

        var taken = ring.Since(mark, LogLevel.Trace, 10, out _, out var lost);

        Assert.Equal(6, lost);
        Assert.Equal([7, 8, 9, 10], taken.Select(record => record.EventId));
    }

    /// <summary>
    /// The mark moves past everything LOOKED AT, not only past what was returned - otherwise
    /// entries below the level would be walked again on every pass, for ever.
    /// </summary>
    [Fact]
    public void Entries_below_the_level_are_not_walked_again()
    {
        var ring = new LogRing(capacity: 8);
        var mark = ring.Mark;

        ring.Add(Record(1, "Chatter", [], LogLevel.Debug));
        ring.Add(Record(2, "Trouble", [], LogLevel.Warning));

        var first = ring.Since(mark, LogLevel.Warning, 10, out mark, out _);

        ring.Add(Record(3, "More", [], LogLevel.Debug));

        Assert.Equal([2], first.Select(record => record.EventId));
        Assert.Empty(ring.Since(mark, LogLevel.Warning, 10, out _, out _));
    }

    // The four that follow call ILogger the ordinary way ON PURPOSE. What they check is the path a
    // caller outside our source generator takes - a framework message, a value that changes after
    // the fact - so going through [LoggerMessage] here would test the generator instead of the
    // provider. CA1848 and CA1873 are about the cost of that path, which is the subject, not a
    // defect.
#pragma warning disable CA1848, CA1873

    /// <summary>
    /// The switch sits inside the provider, because ILoggerFactory has AddProvider and no
    /// counterpart: a diagnostic log that comes and goes cannot be a provider that comes and goes,
    /// and the DM raises a display to Debug from the far side of the house (Part 6).
    /// </summary>
    [Fact]
    public void The_level_can_be_changed_while_running()
    {
        using var log = new ProcessLog(Identity, directory: null, LogFileLimits.Display, new ManualTime());

        var logger = log.CreateLogger("test");

        logger.LogDebug(new EventId(1, "Quiet"), "nothing to see");
        Assert.Empty(log.Ring.Recent(10));

        log.Level = LogLevel.Debug;
        logger.LogDebug(new EventId(1, "Quiet"), "now it counts");

        Assert.Single(log.Ring.Recent(10));
    }

    /// <summary>
    /// The ring buffer holds records for minutes. An object would keep whatever it points at alive
    /// - and one changed afterwards would make the buffer disagree with the file already written.
    /// </summary>
    [Fact]
    public void Values_are_taken_as_text_so_a_later_change_cannot_rewrite_history()
    {
        using var log = new ProcessLog(Identity, directory: null, LogFileLimits.Display, new ManualTime());

        var changing = new StringBuilder("TISCH-PC");

        log.CreateLogger("test").LogInformation(new EventId(1, "Named"), "Device {DeviceName}", changing);

        changing.Clear().Append("something else entirely");

        Assert.Equal("TISCH-PC", log.Ring.Recent(1).Single().Value("DeviceName"));
    }

    [Fact]
    public void A_screen_is_read_off_the_named_values()
    {
        using var log = new ProcessLog(Identity, directory: null, LogFileLimits.Display, new ManualTime());

        log.CreateLogger("test").LogInformation(
            new EventId(3003, "OverlayOpened"),
            "Overlay on {ScreenId} opened ({Mode}).",
            new ScreenId(@"\\?\DISPLAY#TEST#1"),
            "overlay");

        var record = log.Ring.Recent(1).Single();

        Assert.Equal(new ScreenId(@"\\?\DISPLAY#TEST#1"), record.Screen);
        Assert.Null(record.Source);
    }

    /// <summary>
    /// The control hosts Kestrel, so framework messages arrive in the same provider. They have no
    /// catalogue entry and should not get one; what they say travels as raw text, and the category
    /// stands in for the name they do not carry.
    /// </summary>
    [Fact]
    public void A_message_from_outside_travels_as_raw_text_under_its_category()
    {
        using var log = new ProcessLog(Identity, directory: null, LogFileLimits.Display, new ManualTime());

        log.CreateLogger("Microsoft.AspNetCore.Server.Kestrel")
            .LogWarning("Heartbeat took longer than {Interval}.", TimeSpan.FromSeconds(1));

        var record = log.Ring.Recent(1).Single();

        Assert.Equal("Microsoft.AspNetCore.Server.Kestrel", record.EventName);
        Assert.Equal("Heartbeat took longer than 00:00:01.", record.RawText);
        Assert.Contains("Heartbeat took longer than", LogCatalog.Render(record), StringComparison.Ordinal);
    }

#pragma warning restore CA1848, CA1873

    private static LogRecord Record(
        int id,
        string name,
        IReadOnlyList<LogValue> values,
        LogLevel level = LogLevel.Information)
    {
        var at = new DateTimeOffset(2026, 8, 12, 14, 31, 7, TimeSpan.FromHours(2));

        return new LogRecord(at, at, level, id, name, values);
    }
}
