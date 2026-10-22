namespace DnDOverlay.Core;

/// <summary>
/// The texts this library hands to a surface (Part 8) - parameter labels and choices, the reasons on a
/// tile, why a picture was refused. English in <c>Strings.resx</c>, German beside it, in the language of
/// the process that asks. Log sentences are not in here; they live in the log catalogue.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("DnDOverlay.Core.Strings", typeof(Strings).Assembly);

    internal static string ParameterPresentation => Text[nameof(ParameterPresentation)];

    internal static string ParameterScaleOnLoad => Text[nameof(ParameterScaleOnLoad)];

    internal static string ParameterScaleOnLoadUnit => Text[nameof(ParameterScaleOnLoadUnit)];

    internal static string ParameterMaxWidthOnLoad => Text[nameof(ParameterMaxWidthOnLoad)];

    internal static string ParameterMaxWidthOnLoadUnit => Text[nameof(ParameterMaxWidthOnLoadUnit)];

    internal static string ParameterPlacement => Text[nameof(ParameterPlacement)];

    internal static string ParameterDefaultRotationDeg => Text[nameof(ParameterDefaultRotationDeg)];

    internal static string ParameterDefaultRotationDegUnit => Text[nameof(ParameterDefaultRotationDegUnit)];

    internal static string ParameterParkEdge => Text[nameof(ParameterParkEdge)];

    internal static string ParameterMinScale => Text[nameof(ParameterMinScale)];

    internal static string ParameterMinScaleUnit => Text[nameof(ParameterMinScaleUnit)];

    internal static string ParameterMaxScale => Text[nameof(ParameterMaxScale)];

    internal static string ParameterMaxScaleUnit => Text[nameof(ParameterMaxScaleUnit)];

    internal static string ParameterMinVisiblePixels => Text[nameof(ParameterMinVisiblePixels)];

    internal static string ParameterMinVisiblePixelsUnit => Text[nameof(ParameterMinVisiblePixelsUnit)];

    internal static string ParameterImageTextSize => Text[nameof(ParameterImageTextSize)];

    internal static string ParameterImageTextSizeUnit => Text[nameof(ParameterImageTextSizeUnit)];

    internal static string ParameterRotationDeadZoneDeg => Text[nameof(ParameterRotationDeadZoneDeg)];

    internal static string ParameterRotationDeadZoneDegUnit => Text[nameof(ParameterRotationDeadZoneDegUnit)];

    internal static string ParameterRotationSnapToleranceDeg => Text[nameof(ParameterRotationSnapToleranceDeg)];

    internal static string ParameterRotationSnapToleranceDegUnit => Text[nameof(ParameterRotationSnapToleranceDegUnit)];

    internal static string ParameterArrivalHighlightSeconds => Text[nameof(ParameterArrivalHighlightSeconds)];

    internal static string ParameterArrivalHighlightSecondsUnit => Text[nameof(ParameterArrivalHighlightSecondsUnit)];

    internal static string ParameterInertia => Text[nameof(ParameterInertia)];

    internal static string ParameterScrollUpZoomsIn => Text[nameof(ParameterScrollUpZoomsIn)];

    internal static string ParameterDiagnosticEdge => Text[nameof(ParameterDiagnosticEdge)];

    internal static string ParameterKeepAwake => Text[nameof(ParameterKeepAwake)];

    internal static string ParameterRescueAfterSeconds => Text[nameof(ParameterRescueAfterSeconds)];

    internal static string ParameterRescueAfterSecondsUnit => Text[nameof(ParameterRescueAfterSecondsUnit)];

    internal static string ParameterForeignWindows => Text[nameof(ParameterForeignWindows)];

    internal static string ParameterLanguage => Text[nameof(ParameterLanguage)];

    internal static string ParameterLanguageUnit => Text[nameof(ParameterLanguageUnit)];

    internal static string ParameterLevel => Text[nameof(ParameterLevel)];

    internal static string ParameterForwardAtLeast => Text[nameof(ParameterForwardAtLeast)];

    internal static string ChoiceOverlay => Text[nameof(ChoiceOverlay)];

    internal static string ChoiceWindow => Text[nameof(ChoiceWindow)];

    internal static string ChoiceFlow => Text[nameof(ChoiceFlow)];

    internal static string ChoiceCascade => Text[nameof(ChoiceCascade)];

    internal static string ChoiceLeft => Text[nameof(ChoiceLeft)];

    internal static string ChoiceRight => Text[nameof(ChoiceRight)];

    internal static string ChoiceTop => Text[nameof(ChoiceTop)];

    internal static string ChoiceBottom => Text[nameof(ChoiceBottom)];

    internal static string ChoiceAutomatic => Text[nameof(ChoiceAutomatic)];

    internal static string ChoiceReportAndControl => Text[nameof(ChoiceReportAndControl)];

    internal static string ChoiceReportOnly => Text[nameof(ChoiceReportOnly)];

    internal static string ChoiceOff => Text[nameof(ChoiceOff)];

    internal static string ChoiceEnglish => Text[nameof(ChoiceEnglish)];

    internal static string ChoiceGerman => Text[nameof(ChoiceGerman)];

    internal static string ChoiceTrace => Text[nameof(ChoiceTrace)];

    internal static string ChoiceDebug => Text[nameof(ChoiceDebug)];

    internal static string ChoiceInformation => Text[nameof(ChoiceInformation)];

    internal static string ChoiceWarning => Text[nameof(ChoiceWarning)];

    internal static string ChoiceError => Text[nameof(ChoiceError)];

    internal static string ReasonNotPaired => Text[nameof(ReasonNotPaired)];

    internal static string ReasonDeviceNotConnected => Text[nameof(ReasonDeviceNotConnected)];

    internal static string ReasonScreenUnavailable => Text[nameof(ReasonScreenUnavailable)];

    internal static string ReasonControlWindow => Text[nameof(ReasonControlWindow)];

    internal static string ReasonHiddenAtDevice => Text[nameof(ReasonHiddenAtDevice)];

    internal static string ReasonMarkOne => Text[nameof(ReasonMarkOne)];

    internal static string ReasonMarkMany(int count) => Text.Format(nameof(ReasonMarkMany), count);

    internal static string LimitFileSize(long megabytes, long limit) => Text.Format(nameof(LimitFileSize), megabytes, limit);

    internal static string LimitEdge(int width, int height, int limit) => Text.Format(nameof(LimitEdge), width, height, limit);

    internal static string LimitPixels(long pixels, int frames, long limit) => Text.Format(nameof(LimitPixels), pixels, frames, limit);

    internal static string LimitFrames(int frames, int limit) => Text.Format(nameof(LimitFrames), frames, limit);
}
