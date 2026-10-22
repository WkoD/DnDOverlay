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

    /// <summary>
    /// With no campaign open and the campaigns folder out of reach, the head says so in place of the
    /// two ways on - both would lead to an empty switcher (Part 3). An open campaign hides it.
    /// </summary>
    [Fact]
    public async Task An_unreachable_campaigns_folder_is_said_in_the_head()
    {
        using var thread = new DispatcherThread();

        await thread.On(() =>
        {
            var head = new PanelHead();

            head.Campaign(label: null, unreachable: "Der Stick fehlt");

            Assert.Contains(Texts(head), text => text.StartsWith(Strings.PanelHeadUnreachable, StringComparison.Ordinal));
            Assert.Contains("Der Stick fehlt", Texts(head));

            head.Campaign(label: null);

            Assert.DoesNotContain("Der Stick fehlt", Texts(head));
            Assert.Contains(Strings.PanelHeadHint, Texts(head));

            return Task.CompletedTask;
        });
    }

    /// <summary>Every text the head shows: the labels of its buttons and its text blocks.</summary>
    private static List<string> Texts(System.Windows.DependencyObject element)
    {
        var texts = new List<string>();

        if (element is System.Windows.Controls.TextBlock { Visibility: System.Windows.Visibility.Visible } block)
        {
            texts.Add(block.Text);
        }

        if (element is System.Windows.Controls.ContentControl { Content: string content })
        {
            texts.Add(content);
        }

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(element).OfType<System.Windows.DependencyObject>())
        {
            texts.AddRange(Texts(child));
        }

        return texts;
    }

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
