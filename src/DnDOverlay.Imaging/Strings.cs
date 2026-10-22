using DnDOverlay.Core;

namespace DnDOverlay.Imaging;

/// <summary>
/// The reasons this library gives for refusing a file (Part 5, Part 8) - the DM reads them in the
/// status line. English in <c>Strings.resx</c>, German beside it.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("DnDOverlay.Imaging.Strings", typeof(Strings).Assembly);

    internal static string NoImage => Text[nameof(NoImage)];

    internal static string HeicRefused => Text[nameof(HeicRefused)];

    internal static string NotAnImageFormat => Text[nameof(NotAnImageFormat)];

    internal static string DecodingStopped => Text[nameof(DecodingStopped)];

    internal static string FormatUnreadable => Text[nameof(FormatUnreadable)];

    internal static string NotReadableAsImage(string reason) => Text.Format(nameof(NotReadableAsImage), reason);

    internal static string TokenNoContent => Text[nameof(TokenNoContent)];

    internal static string TokenSomethingElse => Text[nameof(TokenSomethingElse)];

    internal static string TokenNoPicture => Text[nameof(TokenNoPicture)];

    internal static string ArchiveUnreadable => Text[nameof(ArchiveUnreadable)];

    internal static string ArchiveTooManyEntries(int entries, int limit) => Text.Format(nameof(ArchiveTooManyEntries), entries, limit);

    internal static string TokenDescriptionUnreadable => Text[nameof(TokenDescriptionUnreadable)];
}
