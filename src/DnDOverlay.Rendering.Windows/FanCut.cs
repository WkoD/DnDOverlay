using System.Windows.Media;
using DnDOverlay.Core;

namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// The fade that makes a cut fan card read as <i>"there is more of this"</i> rather than as the edge
/// of the picture.
/// <para>
/// <b>What is shared here is one decision, not one brush.</b> Where the fades sit - and above all
/// that the two of them may never meet in the middle and cancel the card out - is a rule about the
/// fan, and it now has to hold on two surfaces: the table trims a card with an opacity mask on its
/// element, the thumbnail with one pushed onto its drawing context. The MAPPING differs, because a
/// WPF element has a bounding box of its own and a drawing does not, so each caller wraps these
/// stops in the brush it needs. Two stop collections would have been the second answer to the same
/// question (rule 9).
/// </para>
/// <para>
/// The fractions themselves come from <see cref="Parking.CutOf"/>, in Core - this is only the step
/// from those numbers to something a renderer can use.
/// </para>
/// </summary>
public static class FanCut
{
    /// <summary>
    /// The gradient of one cut, from its head end to its tail end. Callers check
    /// <see cref="Parking.Cut.IsWhole"/> first: an uncut card wants no mask at all rather than a
    /// mask that happens to be opaque everywhere.
    /// </summary>
    public static GradientStopCollection Stops(Parking.Cut cut)
    {
        // Never let the two fades meet in the middle and cancel the picture out. A card whose
        // window is thinner than two fades is exactly the case this guards - and it is the normal
        // case on a long card in a full fan, not an exotic one.
        var middle = (cut.From + cut.To) / 2;
        var stops = new GradientStopCollection();

        if (cut.From > 0)
        {
            stops.Add(new GradientStop(Colors.Transparent, cut.From));
            stops.Add(new GradientStop(Colors.Black, Math.Min(cut.From + cut.Fade, middle)));
        }
        else
        {
            stops.Add(new GradientStop(Colors.Black, 0));
        }

        if (cut.To < 1)
        {
            stops.Add(new GradientStop(Colors.Black, Math.Max(cut.To - cut.Fade, middle)));
            stops.Add(new GradientStop(Colors.Transparent, cut.To));
        }
        else
        {
            stops.Add(new GradientStop(Colors.Black, 1));
        }

        return stops;
    }
}
