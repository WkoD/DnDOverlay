using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Logging;

/// <summary>
/// One log message, as it is kept: a stable identifier plus named values, never a finished
/// sentence (Part 8). What it says in words is decided when it is written or shown, in the
/// language of whoever does it - which is the whole reason this type carries no text.
/// <para>
/// Everything in it is a STRING by the time it gets here, and that is deliberate on two counts.
/// The ring buffer holds these for minutes, so a live object would keep whatever it points at
/// alive with it - and worse, it could be changed after the fact and make the buffer disagree
/// with the file. Foreign text is also cleaned exactly once, here at the boundary
/// (<see cref="LogText.Clean"/>), so no later reader has to remember to.
/// </para>
/// </summary>
/// <param name="At">
/// When it happened, on the clock of whoever wrote it. For a forwarded entry that is the DEVICE's
/// clock, and an unattended display PC without internet and with a flat coin cell can be hours
/// out (Part 8).
/// </param>
/// <param name="Received">
/// When it arrived here. Equal to <paramref name="At"/> for an entry this process wrote, and the
/// one field the stream is SORTED by: a device with a wrong clock would otherwise scatter its
/// lines through the nowhere of the list instead of standing next to ours.
/// </param>
/// <param name="Source">
/// Which device it came from, or <see langword="null"/> for an entry this process wrote.
/// <para>
/// The source is <b>who wrote it</b>, never who is talked about. A hub line that names a device -
/// <c>TokenRefused</c>, say - belongs to the control, and the device is one of its
/// <paramref name="Values"/>; anything else would file pairing decisions under the device that
/// was just turned away (Part 8).
/// </para>
/// </param>
/// <param name="Screen">
/// The screen this is about, where there is one. Optional on purpose: many messages belong to the
/// device - connection, token, update - and only some to a single screen. It is set by the
/// process that OWNS that screen; a screen identifier the receiving side does not know falls back
/// to the device rather than being discarded (Part 8).
/// </param>
public sealed record LogRecord(
    DateTimeOffset At,
    DateTimeOffset Received,
    LogLevel Level,
    int EventId,
    string EventName,
    IReadOnlyList<LogValue> Values,
    string? RawText = null,
    LogSource? Source = null,
    ScreenId? Screen = null)
{
    /// <summary>The value of one named placeholder, or null when it was not supplied.</summary>
    public string? Value(string name)
    {
        foreach (var value in Values)
        {
            if (string.Equals(value.Name, name, StringComparison.Ordinal))
            {
                return value.Text;
            }
        }

        return null;
    }
}

/// <summary>One named value of a message, already cleaned and turned into text.</summary>
public readonly record struct LogValue(string Name, string Text);

/// <summary>
/// The device a forwarded entry came from, with the name it had at the time.
/// <para>
/// The name is kept alongside the identifier rather than looked up when the line is shown, and
/// that is on purpose: a log says what was true when it was written. Renaming a device later must
/// not rewrite last month's lines - and the file, once written, could not be rewritten anyway.
/// </para>
/// </summary>
public readonly record struct LogSource(DeviceId Device, string Name);

/// <summary>
/// Makes foreign text safe to write. Called once, where the text enters
/// (<see cref="LogRecord"/>), so that neither the file, the ring buffer nor the wire has to
/// remember it again.
/// </summary>
public static class LogText
{
    /// <summary>
    /// Replaces line breaks and control characters with a single space.
    /// <para>
    /// This is hardening, not tidiness (Part 4). A log line is one line, and both an exception
    /// message and a device name reach us from outside - a crafted one could otherwise write
    /// lines of its own into the file, a forged header line among them, and nothing downstream
    /// would be able to tell them from ours.
    /// </para>
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        Span<char> cleaned = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        var length = 0;
        var wasSpace = false;

        foreach (var character in text)
        {
            var space = char.IsControl(character) || character == ' ';

            if (space)
            {
                // Runs collapse, so a replaced line break does not leave a gap where a reader
                // would suspect something was cut out.
                if (!wasSpace && length > 0)
                {
                    cleaned[length++] = ' ';
                }

                wasSpace = true;
                continue;
            }

            cleaned[length++] = character;
            wasSpace = false;
        }

        while (length > 0 && cleaned[length - 1] == ' ')
        {
            length--;
        }

        return new string(cleaned[..length]);
    }
}
