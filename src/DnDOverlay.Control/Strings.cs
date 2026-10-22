using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// The control's visible texts (Part 8) - <c>Strings.resx</c> in English, <c>Strings.de.resx</c> in
/// German. One member per entry, in partial halves by area (<c>Strings.Settings.cs</c> and so on): a
/// text used here and missing there is a test failure, and so is an entry nobody uses. Log sentences
/// are not in here; they live in the log catalogue.
/// </summary>
internal static partial class Strings
{
    private static readonly ResourceText Text = new("DnDOverlay.Control.Strings", typeof(Strings).Assembly);
}
