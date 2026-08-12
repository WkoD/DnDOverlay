using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control;

/// <summary>
/// The one owner of <c>control.json</c> in this process.
/// <para>
/// It exists because the file has several CALLERS and may have only one writer. Pairing appends a
/// device, the screen inventory writes back wishes and parameters, and the view state follows in
/// M4 - each of them holding its own copy of the document would mean the last save silently
/// dropping whatever the others had changed. The symptom would be a device that has to be paired
/// again after a screen was renamed, and nothing would point at the cause.
/// </para>
/// <para>
/// The rules of Part 6 - atomic, debounced, schema version, never able to stop the start - stay
/// where they are, in <see cref="ConfigurationFile{T}"/>. This only keeps the current document
/// and serialises the changes to it.
/// </para>
/// </summary>
internal sealed class ControlSettings
{
    private readonly ConfigurationFile<ControlConfiguration> _file;
    private readonly Lock _gate = new();

    private ControlConfiguration _current;

    internal ControlSettings(ConfigurationFile<ControlConfiguration> file, ControlConfiguration current)
    {
        _file = file;
        _current = current;
    }

    /// <summary>What is in the file right now, as far as this process is concerned.</summary>
    internal ControlConfiguration Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Changes the document and queues the write. The change is a FUNCTION rather than a value,
    /// so it is applied to whatever is current instead of to a copy somebody read a while ago.
    /// </summary>
    internal void Update(Func<ControlConfiguration, ControlConfiguration> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        ControlConfiguration updated;

        lock (_gate)
        {
            updated = change(_current);
            _current = updated;
        }

        _file.Save(updated);
    }

    /// <summary>
    /// Writes anything outstanding at once - past the debounce, for the one change whose loss
    /// would cost a pairing (Part 6).
    /// </summary>
    internal void Flush() => _file.Flush();
}
