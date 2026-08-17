using System.Globalization;
using System.Resources;
using System.Text;

namespace DnDOverlay.Core.Logging;

/// <summary>
/// Turns a <see cref="LogRecord"/> into a sentence, in the language of whoever is asking.
/// <para>
/// The catalogue is keyed by the <b>number</b>, not by the name, and the reason is in the
/// catalogue itself: <c>DataRootChosen</c> exists twice, as 4001 in the control and 4002 in the
/// display, and the two say different things ("No control.json yet" against "No display.json
/// yet"). The identifier is the contract; a shared key would make a line ambiguous about who
/// wrote it (Part 8). The name rides along in the key so the file stays readable.
/// </para>
/// </summary>
public static class LogCatalog
{
    private static readonly ResourceManager Messages =
        new("DnDOverlay.Core.Logging.LogMessages", typeof(LogCatalog).Assembly);

    /// <summary>The resource key of one event: <c>E1001_DisplayConnected</c>.</summary>
    public static string Key(int eventId, string eventName) =>
        string.Create(CultureInfo.InvariantCulture, $"E{eventId}_{eventName}");

    /// <summary>
    /// The message template, or <see langword="null"/> when this build has no entry for it.
    /// <para>
    /// The step down from a translation to the neutral English text is the framework's:
    /// <see cref="ResourceManager"/> walks from the asked-for culture to the neutral resource by
    /// itself. What is added here is the step below that - returning null instead of throwing, so
    /// that an identifier from a NEWER counterpart falls through to the third stage rather than
    /// taking the line with it (Part 8).
    /// </para>
    /// </summary>
    public static string? Template(int eventId, string eventName, CultureInfo? culture = null)
    {
        try
        {
            return Messages.GetString(Key(eventId, eventName), culture ?? CultureInfo.CurrentUICulture);
        }
        catch (MissingManifestResourceException)
        {
            // The catalogue itself is missing - a broken build rather than a missing entry. It
            // still must not cost the line: what a reader needs is in the record either way.
            return null;
        }
    }

    /// <summary>
    /// Renders one record, stepping down and never failing:
    /// <list type="number">
    /// <item>the translated text in the asked-for language,</item>
    /// <item>the neutral English text from the catalogue,</item>
    /// <item>the event name and its values in plain text.</item>
    /// </list>
    /// <para>
    /// The third stage is the point. An unknown identifier must NEVER produce "unknown event" or
    /// an empty line - mixed versions are exactly when the message is needed most, and a name
    /// plus its arguments is enough to work with (Part 8).
    /// </para>
    /// </summary>
    public static string Render(LogRecord record, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        var template = Template(record.EventId, record.EventName, culture);

        var text = template is null
            ? Spelled(record)
            : Filled(template, record);

        // Exception texts and messages from foreign libraries travel as raw text and are shown
        // unchanged - said out loud in Part 8 rather than treated as a stopgap. They are already
        // clean; LogText.Clean ran where they came in.
        return string.IsNullOrEmpty(record.RawText)
            ? text
            : $"{text} - {record.RawText}";
    }

    /// <summary>Stage 1 and 2: the template with its placeholders filled in.</summary>
    private static string Filled(string template, LogRecord record)
    {
        var filled = new StringBuilder(template.Length + 32);

        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '{')
            {
                filled.Append(template[index]);
                continue;
            }

            var close = template.IndexOf('}', index + 1);
            var name = close < 0 ? null : template[(index + 1)..close];

            // A placeholder without a value stays as it stands. Showing "{Attempt}" says that
            // something was meant to be here; dropping it silently would read as a finished
            // sentence that happens to be missing its subject.
            if (name is null || record.Value(name) is not { } value)
            {
                filled.Append(template[index]);
                continue;
            }

            filled.Append(value);
            index = close;
        }

        return filled.ToString();
    }

    /// <summary>Stage 3: <c>AssetDownloadFailed (AssetId=ab12cd…, Attempt=3)</c>.</summary>
    private static string Spelled(LogRecord record)
    {
        if (record.Values.Count == 0)
        {
            return record.EventName;
        }

        var spelled = new StringBuilder(record.EventName).Append(" (");

        for (var index = 0; index < record.Values.Count; index++)
        {
            if (index > 0)
            {
                spelled.Append(", ");
            }

            spelled.Append(record.Values[index].Name).Append('=').Append(record.Values[index].Text);
        }

        return spelled.Append(')').ToString();
    }
}
