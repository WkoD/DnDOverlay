namespace DnDOverlay.Core;

/// <summary>
/// What counts as a tap, and what counts as the second of two.
/// <para>
/// <b>It lives here because it is the same grip on two surfaces.</b> A double tap turns a picture
/// to whoever tapped, at the table and in the thumbnail, and Prüfschritt 22 signs the milestone off
/// on exactly that: "the same three grips in the thumbnail → identical behaviour, no relearning".
/// Two sets of these four numbers would be two feels, and the one that drifted would be the one
/// nobody measured.
/// </para>
/// <para>
/// <b>Decided at the END of a gesture rather than on touch-down.</b> A tap IS a manipulation - one
/// that moved almost nothing - so asking afterwards never has to fight the platform's input
/// promotion.
/// </para>
/// <para>
/// The clock is handed in and must be monotonic. It only ever measures the distance between two of
/// its own readings, and a wall clock stepping back mid-evening would make a double tap
/// unreachable (rule 10: Core has no clock).
/// </para>
/// </summary>
public sealed class Tapping
{
    /// <summary>How far a gesture may travel and still be a tap, in DIP.</summary>
    public const double TravelDip = 12;

    /// <summary>How long it may last, in milliseconds. Longer is a hold, not a tap.</summary>
    public const long HeldMs = 300;

    /// <summary>How near the second tap has to land, in DIP on each axis.</summary>
    public const double NearDip = 40;

    /// <summary>How soon the second has to follow, in milliseconds.</summary>
    public const long TwiceMs = 400;

    private long _last;
    private double _lastX;
    private double _lastY;

    /// <summary>Whether a gesture that has ended was a tap at all.</summary>
    public static bool IsTap(double travelDip, long heldMs) =>
        travelDip <= TravelDip && heldMs <= HeldMs;

    /// <summary>
    /// Whether this tap was the second of a pair, and remembers it if it was not.
    /// <para>
    /// <b>A third tap does not turn the picture again:</b> the pair is spent. Otherwise holding one
    /// finger down and tapping with another would spin a picture on the table.
    /// </para>
    /// </summary>
    public bool Twice(long nowMs, double xDip, double yDip)
    {
        var twice = nowMs - _last <= TwiceMs
            && Math.Abs(xDip - _lastX) <= NearDip
            && Math.Abs(yDip - _lastY) <= NearDip;

        _last = twice ? 0 : nowMs;
        _lastX = xDip;
        _lastY = yDip;

        return twice;
    }
}
