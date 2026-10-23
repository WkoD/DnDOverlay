using System.Globalization;

namespace DnDOverlay.Control;

/// <summary>
/// What the typed fields of the window <i>Settings</i> accept (Part 7). Every value takes effect when it
/// is changed, so a value refused here never reaches control.json - the window shows the value in force
/// again and says why (M5 review).
/// </summary>
internal static class SettingsInput
{
    /// <summary>The lowest port the hub may listen on - below lie the ports Windows keeps for its services.</summary>
    internal const int LowestPort = 1024;

    /// <summary>The port, digits only, from <see cref="LowestPort"/> to 65535 - or null.</summary>
    internal static int? Port(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        return int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= LowestPort and <= 65535
            ? port
            : null;
    }

    /// <summary>
    /// The pattern of counted names: it must hold <c>{n}</c>, or every pasted screenshot would get the
    /// same name - or null.
    /// </summary>
    internal static string? Pattern(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var pattern = typed.Trim();

        return pattern.Contains("{n}", StringComparison.Ordinal) ? pattern : null;
    }

    /// <summary>The free-space threshold, typed in GB in the DM's own number format, as bytes - more than nothing, or null.</summary>
    internal static long? FreeSpaceBytes(string typed, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(typed);

        return double.TryParse(typed.Trim(), NumberStyles.Float, culture, out var gigabytes) && gigabytes > 0
            ? (long)(gigabytes * 1024 * 1024 * 1024)
            : null;
    }

    /// <summary>The picture-count threshold, a whole number above nothing - or null.</summary>
    internal static int? PictureCount(string typed, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(typed);

        return int.TryParse(typed.Trim(), NumberStyles.None, culture, out var count) && count > 0 ? count : null;
    }

    /// <summary>
    /// Whether a new place for the campaigns is the place they are in already - ignoring case and a
    /// trailing backslash, as Windows does. Then there is nothing to move.
    /// </summary>
    internal static bool SamePlace(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return string.Equals(from.TrimEnd('\\'), to.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether one place lies inside the other - either way round. Refused before anything is copied: a
    /// move into a folder of its own, or around itself, has the copy and its source overlap (Part 7).
    /// </summary>
    internal static bool Nested(string from, string to) => Inside(to, from) || Inside(from, to);

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
