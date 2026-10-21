using System.Globalization;
using System.Reflection;
using System.Resources;

namespace DnDOverlay.Core;

/// <summary>
/// An application's visible texts from its <c>Strings.resx</c> (Part 8): English as the neutral
/// resource, German as the satellite beside it, looked up in the language the process was started
/// with. A key with no entry comes back as itself - a visible gap is found at once, an empty label is not.
/// </summary>
public sealed class ResourceText(string baseName, Assembly assembly)
{
    private readonly ResourceManager _manager = new(baseName, assembly);

    /// <summary>The text of one entry.</summary>
    public string this[string key] => _manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>One entry with its numbered places filled - numbers and dates in the formats the user is used to.</summary>
    public string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, this[key], values);
}
