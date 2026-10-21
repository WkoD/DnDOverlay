using System.Globalization;

namespace DnDOverlay.Core;

/// <summary>
/// The language of an application's surface (Part 8). <b>Each application has its own</b> - a German
/// display beside an English control is a valid pair, and neither decides what the other shows.
/// </summary>
public enum UiLanguage
{
    /// <summary>The language Windows shows - English when it is neither of the two.</summary>
    Automatic,
    English,
    German,
}

/// <summary>From the setting to the culture the resources are looked up in.</summary>
public static class UiLanguages
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    /// <summary>
    /// The culture for a setting. <paramref name="system"/> is what Windows showed when the process
    /// started - taken before anything overrides it. Automatic hands it on as it is: the resources
    /// step down from <c>de-AT</c> to <c>de</c> and from anything else to the neutral English by
    /// themselves (Part 8).
    /// </summary>
    public static CultureInfo CultureOf(UiLanguage language, CultureInfo system) =>
        language switch
        {
            UiLanguage.English => English,
            UiLanguage.German => German,
            _ => system,
        };
}
