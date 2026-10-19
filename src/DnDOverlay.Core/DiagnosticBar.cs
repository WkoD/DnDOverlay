namespace DnDOverlay.Core;

/// <summary>
/// Where the diagnostic bar of a screen lies (Part 6). One rule, here, because the display draws it
/// and the settings will offer it - and the two must not disagree about which edge is allowed.
/// </summary>
public static class DiagnosticBar
{
    /// <summary>
    /// How far the bar keeps from the corners, as a share of the edge it lies on - the same as the
    /// fan, because the corners belong to Windows and to the rescue mark (Part 6).
    /// </summary>
    public const double CornerShare = 0.1;

    /// <summary>
    /// The edge the bar lies on: the one set, unless that is the park edge - then, and when nothing
    /// is set, the edge opposite the park edge.
    /// <para>
    /// <b>Never on the fan.</b> A bar on the park edge would cover the parked pictures exactly when
    /// somebody is looking for a problem (Part 6). So setting it there is not refused somewhere and
    /// forgotten elsewhere; it simply cannot happen, and a park edge moved onto the bar pushes the
    /// bar away by itself (step 37d).
    /// </para>
    /// </summary>
    public static ParkEdge EdgeOf(ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.DiagnosticEdge is { } wanted && wanted != context.ParkEdge
            ? wanted
            : Opposite(context.ParkEdge);
    }

    /// <summary>The edge across the screen from this one.</summary>
    public static ParkEdge Opposite(ParkEdge edge) => edge switch
    {
        ParkEdge.Left => ParkEdge.Right,
        ParkEdge.Right => ParkEdge.Left,
        ParkEdge.Top => ParkEdge.Bottom,
        _ => ParkEdge.Top,
    };
}
