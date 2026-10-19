using System.Globalization;
using System.Text;

namespace DnDOverlay.Core;

/// <summary>
/// The search bar's filter (Part 7): a substring anywhere, ignoring case and diacritics, several
/// words meaning AND in any order, over the one name of an entry and nothing else.
/// <para>
/// <b>The one place in the program that compares culture-aware on purpose</b> - and so the only one
/// whose result may differ between platforms: .NET leans on ICU on Linux and on NLS on Windows. That
/// costs nothing, because a search decides nothing; the uniqueness comparison, which does, is ordinal
/// (Part 3). <b>Searching may be generous, comparing must be predictable.</b>
/// </para>
/// <para>
/// Names are normalised once when they come in, not on every keystroke: the list is long and the
/// filter must not slow it down.
/// </para>
/// </summary>
public static class NameFilter
{
    /// <summary>A name as the filter compares it - lower case, diacritics taken off, spaces collapsed.</summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', plain.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Whether an already normalised name passes this query: every word of the query somewhere in
    /// it. An empty query passes everything - a filter you cannot see must not hide pictures.
    /// </summary>
    public static bool Matches(string normalisedName, string query)
    {
        ArgumentNullException.ThrowIfNull(normalisedName);

        var words = Normalise(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.All(word => normalisedName.Contains(word, StringComparison.Ordinal));
    }
}
