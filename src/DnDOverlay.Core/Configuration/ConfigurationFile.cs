using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DnDOverlay.Core.Configuration;

/// <summary>
/// One configuration file on disk, written the way Part 6 prescribes for everything durable:
/// atomically, debounced, carrying its schema version, and never able to stop the start.
/// <para>
/// The rules live here once rather than at every call site. "Atomic" would otherwise be a word
/// each caller redeems its own way - and the difference would never show on a development
/// machine.
/// </para>
/// <para>
/// It takes no <c>ILogger</c>: Core has no package references at all, and the architecture test
/// keeps it that way. What happened comes back as a <see cref="ConfigurationOutcome"/> and the
/// application says it in its own words - which is also the right split, because only the
/// application knows whether losing this file cost an identity or nothing (Part 8).
/// </para>
/// </summary>
public sealed class ConfigurationFile<T> : IDisposable, IAsyncDisposable
    where T : class, IConfigurationDocument
{
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();

    private ITimer? _timer;
    private T? _pending;
    private bool _disposed;

    /// <param name="path">The file. Its directory is created on the first write, not before.</param>
    /// <param name="typeInfo">Source-generated, so there is no type resolution at run time.</param>
    /// <param name="time">
    /// Handed in (rule 10). The debounce runs on this clock, which is what makes "one write for
    /// twenty changes" a test rather than a stopwatch.
    /// </param>
    /// <param name="debounce">
    /// How long a change waits for its neighbours. Zero writes through, which is what a test
    /// that is not about debouncing wants.
    /// </param>
    public ConfigurationFile(string path, JsonTypeInfo<T> typeInfo, TimeProvider time, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Where the file lies.</summary>
    public string Path { get; }

    /// <summary>
    /// Reads the file, or replaces it if it cannot be read.
    /// <para>
    /// A version NEWER than ours is treated exactly like an unreadable file, and deliberately so.
    /// The hard "no" of Part 3 protects a campaign, whose content cannot be reconstructed; a
    /// configuration can be recreated with defaults, and refusing to start is the one outcome a
    /// display PC must never have.
    /// </para>
    /// </summary>
    /// <param name="createDefault">A fresh document, as on a new installation.</param>
    public ConfigurationLoad<T> Load(Func<T> createDefault)
    {
        ArgumentNullException.ThrowIfNull(createDefault);

        if (!File.Exists(Path))
        {
            return new ConfigurationLoad<T>(createDefault(), ConfigurationOutcome.Created, SetAside: null);
        }

        try
        {
            var text = File.ReadAllBytes(Path);
            var value = JsonSerializer.Deserialize(text, _typeInfo);

            if (value is not null && value.SchemaVersion <= ConfigurationSchema.Version)
            {
                return new ConfigurationLoad<T>(value, ConfigurationOutcome.Loaded, SetAside: null);
            }
        }
        catch (JsonException)
        {
            // Half written, hand-mangled, or simply not our format. Same answer either way.
        }
        catch (IOException)
        {
            // Locked or unreadable. Starting matters more than this file.
        }

        return new ConfigurationLoad<T>(createDefault(), ConfigurationOutcome.Replaced, SetAsideBroken());
    }

    /// <summary>
    /// Remembers a change and schedules the write. Cheap enough to call on every keystroke -
    /// that is what the debounce is for.
    /// </summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _pending = value;

            if (_debounce <= TimeSpan.Zero)
            {
                WriteNow();
                return;
            }

            if (_timer is null)
            {
                _timer = _time.CreateTimer(_ => Flush(), state: null, _debounce, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Writes anything outstanding at once.
    /// <para>
    /// This is what <c>SessionEnding</c> calls. Without it the one change that is lost is the
    /// last one - typically the display PC whose screen was just renamed and which is then
    /// switched off (Part 6).
    /// </para>
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            WriteNow();
        }
    }

    /// <summary>Writes anything outstanding, then stops. Never leaves a change behind.</summary>
    public void Dispose()
    {
        var timer = Retire();

        timer?.Dispose();
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        var timer = Retire();

        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ITimer? Retire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _disposed = true;
            WriteNow();

            var timer = _timer;
            _timer = null;

            return timer;
        }
    }

    private void WriteNow()
    {
        if (_pending is null)
        {
            return;
        }

        var value = _pending;
        _pending = null;

        var directory = System.IO.Path.GetDirectoryName(Path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write beside it, then replace. File.Move maps onto a single rename call of the
        // operating system and is atomic everywhere; File.Replace is built around Windows
        // semantics, carries a backup copy and does not give the same guarantee elsewhere.
        // Pinned here because "atomic" is otherwise a word every call site redeems its own way,
        // and the difference would never show on a development machine (Part 6).
        var temporary = Path + ".tmp";

        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _typeInfo));
        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>
    /// Moves the unreadable file out of the way and returns where it went. It is kept rather
    /// than deleted: it is the only evidence of what went wrong, and it costs nothing.
    /// </summary>
    private string? SetAsideBroken()
    {
        // A file name, so no colons (Part 3).
        var stamp = _time.GetLocalNow().ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        var broken = $"{Path}.broken {stamp}";

        try
        {
            File.Move(Path, broken, overwrite: true);
            return broken;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
