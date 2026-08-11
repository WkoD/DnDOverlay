using System.Text;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// The write rules from Part 6, checked once rather than at every call site: atomic, debounced,
/// carrying a schema version, and never able to stop the start.
/// </summary>
public sealed class ConfigurationFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DnDOverlay.Tests",
        Guid.NewGuid().ToString("N"));

    private string Path0 => Path.Combine(_directory, "control.json");

    [Fact]
    public void A_missing_file_is_created_not_mourned()
    {
        using var file = Store(TimeProvider.System);

        var loaded = file.Load(() => new ControlConfiguration { Port = 47800 });

        Assert.Equal(ConfigurationOutcome.Created, loaded.Outcome);
        Assert.Null(loaded.SetAside);
        Assert.False(File.Exists(Path0));
    }

    [Fact]
    public void What_was_written_comes_back()
    {
        var id = Guid.NewGuid();

        using (var file = Store(TimeProvider.System))
        {
            file.Save(new ControlConfiguration { ControlId = id, Port = 47999 });
            file.Flush();
        }

        using var reopened = Store(TimeProvider.System);
        var loaded = reopened.Load(() => new ControlConfiguration());

        Assert.Equal(ConfigurationOutcome.Loaded, loaded.Outcome);
        Assert.Equal(id, loaded.Value.ControlId);
        Assert.Equal(47999, loaded.Value.Port);
        Assert.Equal(ConfigurationSchema.Version, loaded.Value.SchemaVersion);
    }

    /// <summary>
    /// Twenty changes, one file. Without the debounce every keystroke in a settings field would
    /// be its own write - which is why the clock is handed in: this is a test, not a stopwatch.
    /// </summary>
    [Fact]
    public void Twenty_changes_in_a_row_are_one_write()
    {
        var time = new ManualTime();
        using var file = Store(time, TimeSpan.FromSeconds(2));

        for (var port = 1; port <= 20; port++)
        {
            file.Save(new ControlConfiguration { Port = port });
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        // Still nothing: every change pushed the timer out again.
        Assert.False(File.Exists(Path0));

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(File.Exists(Path0));
        Assert.Equal(20, Read().Port);
    }

    /// <summary>
    /// The last change is the one a debounce loses - typically the display PC whose screen was
    /// just renamed and which is then switched off (Part 6).
    /// </summary>
    [Fact]
    public void Nothing_outstanding_survives_the_end()
    {
        var time = new ManualTime();

        using (var file = Store(time, TimeSpan.FromSeconds(2)))
        {
            file.Save(new ControlConfiguration { Port = 4711 });
            Assert.False(File.Exists(Path0));
        }

        Assert.Equal(4711, Read().Port);
    }

    /// <summary>
    /// No half-written file under a valid name. The temporary file is written first and then
    /// renamed, so a reader either sees the old content or the new one.
    /// </summary>
    [Fact]
    public void No_temporary_file_is_left_behind()
    {
        using var file = Store(TimeProvider.System);

        file.Save(new ControlConfiguration());
        file.Flush();

        Assert.True(File.Exists(Path0));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    /// <summary>
    /// The one outcome a display PC must never have is "does not start" - on a machine with no
    /// keyboard (Part 6). The broken file is kept, because it is the only evidence.
    /// </summary>
    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    public void An_unreadable_file_is_set_aside_and_the_start_goes_on(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path0, content, Encoding.UTF8);

        using var file = Store(TimeProvider.System);
        var loaded = file.Load(() => new ControlConfiguration { Port = 47800 });

        Assert.Equal(ConfigurationOutcome.Replaced, loaded.Outcome);
        Assert.Equal(47800, loaded.Value.Port);
        Assert.NotNull(loaded.SetAside);
        Assert.True(File.Exists(loaded.SetAside));
        Assert.Equal(content, File.ReadAllText(loaded.SetAside));
    }

    /// <summary>
    /// A file from a NEWER build is treated exactly like an unreadable one. The hard "no" of
    /// Part 3 protects a campaign, whose content cannot be reconstructed; a configuration can be
    /// recreated with defaults, and refusing to start would be the worse trade.
    /// </summary>
    [Fact]
    public void A_file_from_a_newer_build_is_set_aside_too()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path0,
            $$"""{"schemaVersion":{{ConfigurationSchema.Version + 1}},"port":40000}""",
            Encoding.UTF8);

        using var file = Store(TimeProvider.System);
        var loaded = file.Load(() => new ControlConfiguration { Port = 47800 });

        Assert.Equal(ConfigurationOutcome.Replaced, loaded.Outcome);
        Assert.Equal(47800, loaded.Value.Port);
    }

    /// <summary>
    /// The two documents share one version number, because they share one cluster: there is ONE
    /// moment at which a migration happens (rule 6).
    /// </summary>
    [Fact]
    public void Both_documents_carry_the_same_schema_version()
    {
        Assert.Equal(ConfigurationSchema.Version, new ControlConfiguration().SchemaVersion);
        Assert.Equal(ConfigurationSchema.Version, new DisplayConfiguration().SchemaVersion);
    }

    /// <summary>
    /// display.json is read and edited by humans on a machine with no development tools - the
    /// installer writes into it and the DM may look at it (Part 9).
    /// </summary>
    [Fact]
    public void The_file_is_written_so_a_human_can_read_it()
    {
        var text = WriteDisplay(new DisplayConfiguration { Host = "dm-surface" });

        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.Contains("\"host\": \"dm-surface\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every settable value stands in the file even when it is unset, so that opening it shows
    /// what CAN be set (Part 6). Found on a real first run, not in a test: with nulls suppressed
    /// display.json came out as two lines and told a reader nothing.
    /// </summary>
    [Fact]
    public void An_untouched_file_still_names_everything_that_can_be_set()
    {
        var text = WriteDisplay(new DisplayConfiguration());

        foreach (var key in new[] { "schemaVersion", "deviceId", "deviceName", "host", "controlId" })
        {
            Assert.Contains($"\"{key}\"", text, StringComparison.Ordinal);
        }
    }

    private string WriteDisplay(DisplayConfiguration value)
    {
        var path = Path.Combine(_directory, "display.json");

        using (var file = new ConfigurationFile<DisplayConfiguration>(
            path,
            ConfigurationJsonContext.Default.DisplayConfiguration,
            TimeProvider.System))
        {
            file.Save(value);
            file.Flush();
        }

        return File.ReadAllText(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ConfigurationFile<ControlConfiguration> Store(TimeProvider time, TimeSpan? debounce = null) =>
        new(Path0,
            ConfigurationJsonContext.Default.ControlConfiguration,
            time,
            debounce ?? TimeSpan.Zero);

    private ControlConfiguration Read()
    {
        using var reopened = Store(TimeProvider.System);

        return reopened.Load(() => throw new InvalidOperationException("expected a file")).Value;
    }
}
