namespace DnDOverlay.Control.Tests;

/// <summary>The head of the campaign panel: undo and redo say what the next press does, in the room there is (Part 7).</summary>
public sealed class PanelHeadTests
{
    /// <summary>
    /// Narrow, the label is cut to its verb (Part 7) - first in English, last in German, whose steps
    /// end on the participle. The first German word would be the picture's name, a number or "Szene".
    /// </summary>
    [Theory]
    [InlineData("Add \"Grimmbart\"", false, "Add")]
    [InlineData("Clear all", false, "Clear")]
    [InlineData("\"Grimmbart\" hinzugefügt", true, "hinzugefügt")]
    [InlineData("3 Bilder entfernt", true, "entfernt")]
    [InlineData("Hintergrund von TISCH-PC//DISPLAY1 entfernt", true, "entfernt")]
    [InlineData("\"Ratsversammlung\" geladen", true, "geladen")]
    public void Narrow_the_label_keeps_the_verb_wherever_the_language_puts_it(string label, bool verbLast, string verb) =>
        Assert.Equal(verb, PanelHead.Fitted(label, "Undo", 120, verbLast));

    /// <summary>A free text brings its line breaks into the label; the button stays one line high.</summary>
    [Fact]
    public void The_label_is_one_line_whatever_the_text_brings()
    {
        Assert.Equal("Add text \"Der Kerker von Townsvil\"", PanelHead.Fitted("Add text \"Der Kerker\nvon\r\nTownsvil\"", "Undo", 400, verbLast: false));
        Assert.Equal("hinzugefügt", PanelHead.Fitted("Text \"Der Kerker\nvon Townsvil\" hinzugefügt", "Rückgängig", 120, verbLast: true));
    }

    [Fact]
    public void Nothing_to_undo_reads_as_the_grip_itself() =>
        Assert.Equal("Undo", PanelHead.Fitted(null, "Undo", 120, verbLast: false));

    /// <summary>The language says where its verb stands - German at the end, English at the start.</summary>
    [Fact]
    public void Each_language_says_where_its_verb_stands()
    {
        var before = Thread.CurrentThread.CurrentUICulture;

        try
        {
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("de");
            Assert.Equal("true", Strings.PanelHeadVerbLast);

            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en");
            Assert.Equal("false", Strings.PanelHeadVerbLast);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = before;
        }
    }
}
