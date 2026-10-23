using System.Globalization;
using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// What a number typed in the device band becomes (Part 6, Part 7): read in the DM's number format,
/// refused outside the parameter's range - the box then shows the value in force again and says the
/// range - and rounded to the places the parameter is shown with, so the device is told what the box says.
/// </summary>
internal static class ParameterInput
{
    /// <summary>The value to send, or null when the text is no number or lies outside the range.</summary>
    internal static double? Value(string typed, DisplayParameter parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(parameter);

        return double.TryParse(typed.Trim(), NumberStyles.Float, culture, out var value)
            && value >= parameter.Min && value <= parameter.Max
                ? Math.Round(value, parameter.Decimals)
                : null;
    }
}
