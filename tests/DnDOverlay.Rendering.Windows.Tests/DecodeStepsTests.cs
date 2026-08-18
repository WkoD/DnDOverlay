using DnDOverlay.Rendering.Windows;

namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// How many pixels are decoded. A decoded bitmap is width × height × 4 bytes of uncompressed
/// memory, so this arithmetic is what keeps twenty pictures on a table inside a budget the file
/// sizes say nothing about (Part 6, Part 11).
/// </summary>
public sealed class DecodeStepsTests
{
    /// <summary>
    /// The base step is the screen's longer edge, and it is about the picture's LONGER edge - so a
    /// portrait picture gets a smaller width than a landscape one on the same screen.
    /// </summary>
    [Theory]
    [InlineData(1920, 6000, 4000, 1920)]
    [InlineData(1920, 4000, 6000, 1280)]
    [InlineData(3840, 6000, 4000, 3840)]
    public void A_picture_larger_than_the_screen_is_decoded_at_the_screens_longer_edge(
        int screenEdge, int sourceWidth, int sourceHeight, int expected)
    {
        Assert.Equal(expected, DecodeSteps.Base(screenEdge, sourceWidth, sourceHeight));
    }

    /// <summary>
    /// Asking for more pixels than the source has adds no detail and spends the memory twice - it
    /// scales up. <c>0</c> means "as it comes".
    /// </summary>
    [Theory]
    [InlineData(1920, 800, 600)]
    [InlineData(1920, 1920, 1080)]
    public void A_picture_that_already_fits_is_decoded_as_it_is(int screenEdge, int width, int height)
    {
        Assert.Equal(0, DecodeSteps.Base(screenEdge, width, height));
    }

    [Theory]
    [InlineData(0, 4000, 3000)]
    [InlineData(1920, 0, 0)]
    public void Nothing_to_reckon_with_decodes_as_it_comes(int screenEdge, int width, int height)
    {
        Assert.Equal(0, DecodeSteps.Base(screenEdge, width, height));
    }

    /// <summary>One step per crossing, and the source is the cap.</summary>
    [Fact]
    public void Zooming_past_the_step_asks_for_twice_as_much()
    {
        Assert.Equal(3840, DecodeSteps.Next(decodedWidth: 1920, neededWidth: 2200, sourceWidth: 6000));
    }

    [Fact]
    public void The_source_is_the_ceiling()
    {
        Assert.Equal(5000, DecodeSteps.Next(decodedWidth: 3000, neededWidth: 9000, sourceWidth: 5000));
        Assert.Null(DecodeSteps.Next(decodedWidth: 5000, neededWidth: 9000, sourceWidth: 5000));
    }

    /// <summary>
    /// <b>Zooming out never decodes back.</b> The memory is already spent, giving it back costs a
    /// second decode, and the picture would go soft on a movement that asked for nothing.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(1920)]
    public void Zooming_out_decodes_nothing(int needed)
    {
        Assert.Null(DecodeSteps.Next(decodedWidth: 1920, neededWidth: needed, sourceWidth: 6000));
    }

    /// <summary>
    /// A picture decoded as it came has no step to climb: it is already at its source.
    /// </summary>
    [Fact]
    public void A_picture_decoded_whole_is_never_sharpened()
    {
        Assert.Null(DecodeSteps.Next(decodedWidth: 0, neededWidth: 4000, sourceWidth: 800));
    }

    /// <summary>
    /// Climbing repeatedly ends at the source and stays there - the property that keeps a pinch
    /// that carries on zooming from asking for ever.
    /// </summary>
    [Fact]
    public void Climbing_step_by_step_arrives_at_the_source_and_stops()
    {
        var width = DecodeSteps.Base(1920, 12000, 8000);
        var climbs = 0;

        while (DecodeSteps.Next(width, neededWidth: 99_000, sourceWidth: 12000) is { } next)
        {
            width = next;
            climbs++;

            Assert.True(climbs < 10, "the steps did not converge on the source");
        }

        Assert.Equal(12000, width);
    }
}
