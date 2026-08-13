using System.Text.Json;
using System.Text.RegularExpressions;

namespace DnDOverlay.Core.Configuration;

/// <summary>
/// Gets the <c>ControlId</c> back out of a <c>control.json</c> that could not be read.
/// <para>
/// It matters more than it looks. A display discards the beacons of every control it is not bound
/// to - the rule that keeps a forged beacon from stealing it - so a control that comes back with a
/// NEW identity is a stranger to its own devices: they never send a <c>Hello</c>, the control lists
/// nothing, and both sides are silent while behaving correctly. Keeping the identity turns a walk
/// through the flat into one grip at the control (Part 4, Part 6).
/// </para>
/// <para>
/// Only the identity is recovered, never the content. Paired devices, screen wishes and view state
/// stay lost: what we cannot safely interpret we do not interpret. The tokens are gone with them -
/// that the devices are still reachable without a walk is carried by the other half of the rule,
/// where an unknown token becomes a pairing request rather than a rejection.
/// </para>
/// </summary>
public static partial class ControlIdentity
{
    /// <summary>
    /// A control.json is a few kilobytes. Anything far beyond that is not one, and reading it into
    /// memory to hunt for a GUID would be the wrong answer to a file somebody replaced with
    /// something else entirely.
    /// </summary>
    private const int MostBytesWorthReading = 4 * 1024 * 1024;

    /// <summary>
    /// Tries the set-aside file. <see langword="false"/> means a fresh identity - which is a
    /// legitimate outcome and has to be reported rather than assumed.
    /// </summary>
    /// <param name="path">Where <see cref="ConfigurationLoad{T}.SetAside"/> put the old file.</param>
    public static bool TryRecover(string? path, out Guid controlId)
    {
        controlId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string text;

        try
        {
            var file = new FileInfo(path);

            if (!file.Exists || file.Length > MostBytesWorthReading)
            {
                return false;
            }

            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return TryParse(text, out controlId) || TryFind(text, out controlId);
    }

    /// <summary>
    /// The whole document, for the case that is not damaged at all: a schemaVersion NEWER than
    /// ours is refused although the file parses perfectly (Part 6). Reading one field out of it is
    /// not the interpretation the hard "no" forbids - an identity has no meaning to get wrong.
    /// </summary>
    private static bool TryParse(string text, out Guid controlId)
    {
        controlId = Guid.Empty;

        try
        {
            var value = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.ControlConfiguration);

            if (value is null || value.ControlId == Guid.Empty)
            {
                return false;
            }

            controlId = value.ControlId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// And the case that IS damaged: half written, truncated, hand-mangled. A parser has nothing
    /// left to work with, but the bytes are still there - and the identity is a shape that cannot
    /// be confused with anything else in this document. The known devices carry a
    /// <c>deviceId</c>, so the first hit is the one at the root.
    /// </summary>
    private static bool TryFind(string text, out Guid controlId)
    {
        controlId = Guid.Empty;

        var match = ControlIdPattern().Match(text);

        return match.Success
            && Guid.TryParse(match.Groups[1].ValueSpan, out controlId)
            && controlId != Guid.Empty;
    }

    [GeneratedRegex(
        "\"controlId\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ControlIdPattern();
}
