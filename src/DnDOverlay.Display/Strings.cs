using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The display's visible texts (Part 8) - <c>Strings.resx</c> in English, <c>Strings.de.resx</c> in
/// German. One member per entry: a text used here and missing there is a test failure, and so is an
/// entry nobody uses. Log sentences are not in here; they live in the log catalogue.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("DnDOverlay.Display.Strings", typeof(Strings).Assembly);

    internal static string SetupTitle => Text[nameof(SetupTitle)];

    internal static string SetupWaiting => Text[nameof(SetupWaiting)];

    internal static string SetupAddressUnknown => Text[nameof(SetupAddressUnknown)];

    internal static string SetupHint => Text[nameof(SetupHint)];

    internal static string RescueAsk => Text[nameof(RescueAsk)];

    internal static string RescueHide => Text[nameof(RescueHide)];

    internal static string RescueCancel => Text[nameof(RescueCancel)];

    internal static string PictureNotLoaded => Text[nameof(PictureNotLoaded)];

    internal static string PictureNotRead => Text[nameof(PictureNotRead)];

    internal static string DiagnosticFramesTooFew => Text[nameof(DiagnosticFramesTooFew)];

    internal static string SetupCode(string code) => Text.Format(nameof(SetupCode), code);

    internal static string RescueAbsence(string absence) => Text.Format(nameof(RescueAbsence), absence);

    internal static string RescueOffer(string control) => Text.Format(nameof(RescueOffer), control);

    internal static string NoControlFor(int minutes) => Text.Format(nameof(NoControlFor), minutes);

    internal static string DiagnosticConnected(string control, string milliseconds, string device) =>
        Text.Format(nameof(DiagnosticConnected), control, milliseconds, device);

    internal static string DiagnosticNoControl(string device) => Text.Format(nameof(DiagnosticNoControl), device);

    internal static string DiagnosticFrames(double median, double? p95, double? max, int frames, int stalls) =>
        Text.Format(nameof(DiagnosticFrames), median, p95, max, frames, stalls);

    internal static string DiagnosticBattery(int percent, bool pluggedIn) =>
        Text.Format(nameof(DiagnosticBattery), percent, pluggedIn ? " ⚡" : string.Empty);

    internal static string DiagnosticLoad(
        string frames, double cpu, int loading, int waiting, int pictures, long storeMb, long memoryMb, long capMb, string battery) =>
        Text.Format(nameof(DiagnosticLoad), frames, cpu, loading, waiting, pictures, storeMb, memoryMb, capMb, battery);
}
