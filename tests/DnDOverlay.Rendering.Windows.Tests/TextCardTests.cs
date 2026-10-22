namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The drawing of a free text (M5b9): its shape follows its words, and the same text is the same
/// drawing - which is what lets the table, the thumbnail and the measurement agree.
/// </summary>
public sealed class TextCardTests
{
    [Fact]
    public void A_longer_line_is_a_wider_shape_and_a_second_line_a_taller_one()
    {
        var short1 = TextCard.AspectRatio("Kerker");
        var long1 = TextCard.AspectRatio("Kerker von Townsvil");
        var two = TextCard.AspectRatio("Kerker\nvon Townsvil");

        Assert.True(short1 > 1, $"short {short1}");
        Assert.True(long1 > short1, $"long {long1} against short {short1}");
        Assert.True(two < long1, $"two lines {two} against one {long1}");
    }

    [Fact]
    public void The_same_text_is_the_same_frozen_drawing()
    {
        var first = TextCard.Draw("Kerker von Townsvil");

        Assert.Same(first, TextCard.Draw("Kerker von Townsvil"));
        Assert.True(first.IsFrozen);
    }
}
