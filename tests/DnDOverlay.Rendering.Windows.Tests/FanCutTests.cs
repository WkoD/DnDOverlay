using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Rendering.Windows;

namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The fade on a cut fan card. <b>The rule worth a test is the one that stops the card
/// disappearing:</b> the two fades may never meet and cancel it out, and a long card in a full fan
/// is exactly where they would - the window there is thinner than two fade widths, which is the
/// normal case rather than an exotic one.
/// <para>
/// It is tested here rather than at either surface because both now ask it: the table trims a card
/// with a mask on its element, the thumbnail with one pushed onto its drawing context, and the
/// stops are the one thing they must not decide differently (rule 9).
/// </para>
/// </summary>
public sealed class FanCutTests
{
    private static IReadOnlyList<GradientStop> Stops(Parking.Cut cut) =>
        [.. FanCut.Stops(cut).OrderBy(stop => stop.Offset)];

    /// <summary>Cut at both ends: transparent outside the window, opaque inside it.</summary>
    [Fact]
    public void A_window_cut_at_both_ends_fades_in_and_out()
    {
        var stops = Stops(new Parking.Cut(From: 0.2, To: 0.8, Fade: 0.05));

        Assert.Equal(4, stops.Count);

        Assert.Equal(0.2, stops[0].Offset, 6);
        Assert.Equal(Colors.Transparent, stops[0].Color);

        Assert.Equal(0.25, stops[1].Offset, 6);
        Assert.Equal(Colors.Black, stops[1].Color);

        Assert.Equal(0.75, stops[2].Offset, 6);
        Assert.Equal(Colors.Black, stops[2].Color);

        Assert.Equal(0.8, stops[3].Offset, 6);
        Assert.Equal(Colors.Transparent, stops[3].Color);
    }

    /// <summary>
    /// <b>A window thinner than two fades keeps a card to look at.</b> Without the clamp the fade
    /// coming in from the head would end past the one coming in from the tail, the gradient would
    /// run transparent-black-black-transparent in the wrong order, and the card would be a smear.
    /// </summary>
    [Fact]
    public void Two_fades_meet_in_the_middle_rather_than_crossing()
    {
        var cut = new Parking.Cut(From: 0.45, To: 0.55, Fade: 0.2);
        var stops = Stops(cut);
        var middle = (cut.From + cut.To) / 2;

        Assert.Equal(4, stops.Count);
        Assert.Equal(middle, stops[1].Offset, 6);
        Assert.Equal(middle, stops[2].Offset, 6);

        // And the whole thing still reads head to tail, which is what a renderer needs.
        Assert.Equal(stops.Select(stop => stop.Offset).Order(), stops.Select(stop => stop.Offset));

        // Not blank: the card is at its thinnest here and there is still something opaque to see.
        Assert.Contains(stops, stop => stop.Color == Colors.Black);
    }

    /// <summary>
    /// Cut at one end only - the head end of the fan, where a card runs off the start of the bar.
    /// The other end is the picture's own edge and gets no fade, because there is nothing more of
    /// it to promise.
    /// </summary>
    [Fact]
    public void An_end_that_is_not_cut_gets_no_fade()
    {
        var stops = Stops(new Parking.Cut(From: 0, To: 0.6, Fade: 0.05));

        Assert.Equal(3, stops.Count);

        Assert.Equal(0, stops[0].Offset, 6);
        Assert.Equal(Colors.Black, stops[0].Color);
        Assert.Equal(Colors.Transparent, stops[^1].Color);
    }
}
