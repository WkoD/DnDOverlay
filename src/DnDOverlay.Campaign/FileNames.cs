namespace DnDOverlay.Campaign;

/// <summary>Why a typed name cannot become a file name - said to the DM, never silently mended (Part 3).</summary>
public enum NameProblem
{
    /// <summary>Nothing typed, or only spaces.</summary>
    Empty,

    /// <summary>One of <c>&lt; &gt; : " / \ | ? *</c> or a control character.</summary>
    ForbiddenCharacter,

    /// <summary><c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c>-<c>COM9</c>, <c>LPT1</c>-<c>LPT9</c>, with or without an extension.</summary>
    ReservedName,

    /// <summary>Ends in a dot or a space, or is <c>.</c> or <c>..</c>.</summary>
    BadEnding,

    /// <summary>Longer than <see cref="FileNames.MaxLength"/>.</summary>
    TooLong,
}

/// <summary>
/// The names that become file names - campaign, scene, layout (Part 3). <b>Checked when typed,
/// cleaned only when suggested</b>: where a person stands it is refused and the reason said, where
/// nobody stands it is adjusted.
/// <para>
/// <b>The list is fixed here and does not come from the framework.</b>
/// <c>Path.GetInvalidFileNameChars()</c> answers per platform - on Linux only <c>\0</c> and
/// <c>/</c> - so a test run there would check something other than what Windows enforces, and stay
/// green while the rule no longer held. The stricter rule everywhere keeps a campaign folder
/// exchangeable, the same reason names are compared ignoring case everywhere.
/// </para>
/// </summary>
public static class FileNames
{
    /// <summary>
    /// Generous for a name a person reads, and leaves room in a full path under 260 characters with
    /// a base path that can be moved (Part 3).
    /// </summary>
    public const int MaxLength = 100;

    /// <summary>
    /// How names are compared: ordinal and ignoring case. Windows does not tell <c>dorfkarte</c>
    /// from <c>Dorfkarte</c>, and a culture-aware comparison would keep <c>I</c> and <c>i</c> apart
    /// on a Turkish system - the silent overwrite the rule exists against (Part 3).
    /// </summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>What stands in the way of this typed name, or <see langword="null"/> when it may become a file name as it is.</summary>
    public static NameProblem? Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameProblem.Empty;
        }

        if (name.Any(character => char.IsControl(character) || Forbidden.Contains(character)))
        {
            return NameProblem.ForbiddenCharacter;
        }

        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
        {
            return NameProblem.BadEnding;
        }

        var stem = name.Split('.')[0].TrimEnd();

        if (Reserved.Contains(stem))
        {
            return NameProblem.ReservedName;
        }

        return name.Length > MaxLength ? NameProblem.TooLong : null;
    }

    /// <summary>
    /// A suggestion that is sure to pass <see cref="Check"/>: every forbidden character replaced by
    /// one <c>-</c> each and nothing drawn together, so <c>TISCH-PC//DISPLAY1</c> becomes
    /// <c>TISCH-PC--DISPLAY1</c> and the border between device and screen stays visible (Part 3).
    /// </summary>
    public static string Suggest(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var cleaned = new string([.. raw.Select(character =>
            char.IsControl(character) || Forbidden.Contains(character) ? '-' : character)]);

        cleaned = cleaned.TrimEnd('.', ' ');

        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength].TrimEnd('.', ' ');
        }

        if (cleaned.Length == 0 || cleaned is "." or "..")
        {
            cleaned = "-";
        }

        return Reserved.Contains(cleaned.Split('.')[0].TrimEnd()) ? "_" + cleaned : cleaned;
    }

    /// <summary>
    /// The first of <c>name</c>, <c>name (2)</c>, <c>name (3)</c>, ... that is not taken - how the
    /// application names things itself, where it cannot ask (Part 3).
    /// </summary>
    public static string Numbered(string name, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);

        var used = new HashSet<string>(taken, Comparer);

        if (!used.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} ({number})");

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The time stamp of a suggested name, without the colon a file name cannot carry: <c>2026-08-06 23-41</c>.</summary>
    public static string Stamp(DateTimeOffset at) =>
        at.ToString("yyyy-MM-dd HH-mm", System.Globalization.CultureInfo.InvariantCulture);
}
