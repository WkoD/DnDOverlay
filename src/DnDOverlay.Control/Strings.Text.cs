namespace DnDOverlay.Control;

/// <summary>The texts around a free text on a screen (M5b9).</summary>
internal static partial class Strings
{
    internal static string TileMenuAddText => Text[nameof(TileMenuAddText)];

    internal static string TileMenuEditText => Text[nameof(TileMenuEditText)];

    internal static string TextDialogAddTitle => Text[nameof(TextDialogAddTitle)];

    internal static string TextDialogEditTitle => Text[nameof(TextDialogEditTitle)];

    internal static string TextDialogPrompt => Text[nameof(TextDialogPrompt)];

    internal static string TextDialogAdd => Text[nameof(TextDialogAdd)];

    internal static string TextDialogChange => Text[nameof(TextDialogChange)];

    internal static string TextDialogCancel => Text[nameof(TextDialogCancel)];

    internal static string RecordingAddText(string text) => Text.Format(nameof(RecordingAddText), text);
}
