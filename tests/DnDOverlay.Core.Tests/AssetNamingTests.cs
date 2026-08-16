using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// The five stages of Part 3, and the properties that hold across all of them.
/// <para>
/// They run on Linux too, and that is half the reason the derivation sits in <c>Core</c>: only the
/// ASKING of the clipboard is Windows, the answering is text.
/// </para>
/// </summary>
public sealed class AssetNamingTests
{
    private const string Pattern = "Zwischenablage {n}";

    /// <summary>
    /// Stage 2 beats everything below it. Stage 1 - a container's own name - is not in this class
    /// at all: it becomes visible only when the container is opened, and that happens inside the
    /// ingest so no entrance can skip it. It is proved where it happens, in
    /// <c>Campaign.Tests.AssetStoreTests</c>.
    /// </summary>
    [Fact]
    public void A_file_name_beats_every_source_below_it()
    {
        var offer = new NameSource
        {
            FileName = "Wache.png",
            SourceUrl = "https://example.invalid/bilder/ork.png",
            Html = "<img src=\"x\" alt=\"Ein Ork\">",
            Text = "https://example.invalid/bilder/Tavernenwirt.webp",
        };

        Assert.Equal("Wache", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>Stage 2. <c>Krieger.png</c> becomes <c>Krieger</c> - the extension says nothing a person wants.</summary>
    [Fact]
    public void A_file_name_loses_its_extension()
    {
        var offer = new NameSource { FileName = "Krieger.png" };

        Assert.Equal("Krieger", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// A dot inside a name is not an extension marker, and a name that IS an extension keeps it -
    /// there would be nothing left otherwise.
    /// </summary>
    [Theory]
    [InlineData("Ork v1.2 fertig.png", "Ork v1.2 fertig")]
    [InlineData("Ohne Endung", "Ohne Endung")]
    [InlineData(".gitignore", ".gitignore")]
    public void Only_the_last_dot_ends_a_name(string fileName, string expected)
    {
        var offer = new NameSource { FileName = fileName };

        Assert.Equal(expected, AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// Stage 3, and all three demands of the plan at once: last segment, decoded, without extension.
    /// A query string is not part of the path and must not end up in the name.
    /// </summary>
    [Theory]
    [InlineData("https://example.invalid/bilder/2024/Waldl%C3%A4ufer.png", "Waldläufer")]
    [InlineData("https://example.invalid/bilder/ork.png?size=large&v=2", "ork")]
    [InlineData("http://example.invalid/a/b/Stadtkarte.jpeg", "Stadtkarte")]
    public void A_url_gives_its_last_path_segment_decoded_and_without_extension(string address, string expected)
    {
        var offer = new NameSource { SourceUrl = address };

        Assert.Equal(expected, AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// Stage 4, first half - the check step's own wording: <c>CF_HTML</c> with <c>alt</c> gives that
    /// text. The address stands right beside it and loses, because a person wrote the one and a
    /// server assembled the other.
    /// </summary>
    [Fact]
    public void The_alt_text_beats_the_address_it_came_from()
    {
        var offer = new NameSource { Html = Fragment("<img src=\"/i/9f3.png\" alt=\"Grimmbart der Ork\">") };

        Assert.Equal("Grimmbart der Ork", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>The title stands in when there is no alt - both are text a person wrote.</summary>
    [Fact]
    public void The_title_stands_in_when_there_is_no_alt()
    {
        var offer = new NameSource { Html = Fragment("<img src=\"/i/9f3.png\" title=\"Der Ratsherr\">") };

        Assert.Equal("Der Ratsherr", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// Stage 4, second half: no alt, but a <c>SourceURL:</c> header - the last path segment,
    /// decoded and without extension, exactly as for an import.
    /// </summary>
    [Fact]
    public void Without_an_alt_the_source_url_header_names_it()
    {
        var offer = new NameSource { Html = Fragment("<img src=\"/i/9f3.png\">") };

        Assert.Equal("Waldläufer", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// A first image without a usable name must not end the search. A pasted fragment regularly
    /// begins with a spacer or a tracking pixel, and the picture the DM meant comes after it.
    /// </summary>
    [Fact]
    public void An_image_without_a_name_does_not_stop_the_search()
    {
        var offer = new NameSource
        {
            Html = Fragment("<img src=\"/spacer.gif\" alt=\"\"><span>x</span><img src=\"/i/9f3.png\" alt=\"Der Ork\">"),
        };

        Assert.Equal("Der Ork", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// The counter-check to the scanner: an attribute is only an attribute when it stands on its
    /// own. Read loosely, <c>data-alt</c> would name every picture on half the pages there are.
    /// </summary>
    [Fact]
    public void An_attribute_that_merely_ends_in_alt_is_not_the_alt()
    {
        var offer = new NameSource { Html = Fragment("<img src=\"/i/9f3.png\" data-alt=\"Bildbeschreibung\">") };

        // Falls through to the header, which is the next stage - not to the tracking attribute.
        Assert.Equal("Waldläufer", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>A plain URL lying beside the picture counts as well (Part 3).</summary>
    [Fact]
    public void A_url_lying_beside_the_picture_counts_too()
    {
        var offer = new NameSource { Text = "  https://example.invalid/bilder/Tavernenwirt.webp  " };

        Assert.Equal("Tavernenwirt", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// And only an address counts. Ordinary text beside a screenshot is not where the picture came
    /// from, and a name made out of it would be a guess dressed up as a finding.
    /// </summary>
    [Theory]
    [InlineData("schau mal hier")]
    [InlineData("file:///C:/Bilder/geheim.png")]
    [InlineData("ftp://example.invalid/ork.png")]
    public void Only_an_http_address_counts_as_one(string text)
    {
        var offer = new NameSource { Text = text };

        Assert.Equal("Zwischenablage 7", AssetNaming.Derive(offer, Pattern, 7));
    }

    /// <summary>
    /// Stage 5, and the one case that genuinely falls through all four: a real screenshot. Nothing
    /// is invented, because anything invented would mislead more than a number.
    /// </summary>
    [Fact]
    public void A_screenshot_falls_through_to_the_counted_name()
    {
        Assert.Equal("Zwischenablage 3", AssetNaming.Derive(new NameSource(), Pattern, 3));
    }

    /// <summary>
    /// The check step says it in exactly these words: a changed pattern acts on the very NEXT
    /// paste. It does, because the pattern is handed in per call and nothing remembers it.
    /// </summary>
    [Fact]
    public void A_changed_pattern_acts_on_the_very_next_paste()
    {
        Assert.Equal("Zwischenablage 4", AssetNaming.Derive(new NameSource(), Pattern, 4));
        Assert.Equal("NSC 5", AssetNaming.Derive(new NameSource(), "NSC {n}", 5));
    }

    /// <summary>
    /// A pattern without the placeholder is taken at its word: every picture proposes the same name
    /// and the stock numbers them. The program does not overrule a setting by slipping a number in.
    /// </summary>
    [Fact]
    public void A_pattern_without_the_placeholder_is_taken_at_its_word()
    {
        Assert.Equal("Bild", AssetNaming.Derive(new NameSource(), "Bild", 9));
    }

    /// <summary>
    /// Cleaning runs over every stage, not only the outside ones. A file name carries a line break
    /// about as often as never - which is the frequency at which a missing check is found by the
    /// person it happens to, rather than by a test.
    /// </summary>
    [Theory]
    [InlineData("Ork\nmit Axt.png", "Ork mit Axt")]
    [InlineData("  Ork   mit   Axt  .png", "Ork mit Axt")]
    [InlineData("Ork\u0007\u0008.png", "Ork")]
    public void A_name_never_carries_a_break_or_a_control_character(string fileName, string expected)
    {
        var offer = new NameSource { FileName = fileName };

        Assert.Equal(expected, AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>A candidate that is nothing but whitespace is no candidate; the next stage answers.</summary>
    [Fact]
    public void A_blank_candidate_hands_on_to_the_next_stage()
    {
        var offer = new NameSource { FileName = "   ", SourceUrl = "https://example.invalid/a/Krieger.png" };

        Assert.Equal("Krieger", AssetNaming.Derive(offer, Pattern, 1));
    }

    /// <summary>
    /// Cut to what a person reads (Part 3's hundred characters), and cut cleanly - a name that ends
    /// mid-blank looks like the program lost the rest.
    /// </summary>
    [Fact]
    public void A_very_long_name_is_cut_to_what_a_person_reads()
    {
        var offer = new NameSource { FileName = new string('a', 300) + ".png" };

        var name = AssetNaming.Derive(offer, Pattern, 1);

        Assert.Equal(AssetNaming.MaxLength, name.Length);
        Assert.Equal(name.TrimEnd(), name);
    }

    /// <summary>
    /// An address without a last segment has no name in it, and the next stage answers instead of
    /// something failing - the entrance is a paste, and what lies in a clipboard is not ours.
    /// </summary>
    [Theory]
    [InlineData("https://example.invalid/")]
    [InlineData("https://example.invalid")]
    public void A_url_that_yields_nothing_falls_through_instead_of_failing(string address)
    {
        var offer = new NameSource { SourceUrl = address };

        Assert.Equal("Zwischenablage 2", AssetNaming.Derive(offer, Pattern, 2));
    }

    /// <summary>
    /// A malformed escape stays as it stands. <b>Measured rather than assumed:</b> the first
    /// version of this test expected the address to be dropped, because unescaping was believed to
    /// fail on <c>%zz</c> - it does not, it hands the sequence back untouched. The guard written
    /// for that case was dead code claiming a check that could never fire, and it went out with
    /// this finding.
    /// </summary>
    [Fact]
    public void A_malformed_escape_is_left_standing_rather_than_refused()
    {
        var offer = new NameSource { SourceUrl = "https://example.invalid/%zz.png" };

        Assert.Equal("%zz", AssetNaming.Derive(offer, Pattern, 2));
    }

    /// <summary>
    /// The header block ends where the markup begins. Searching the whole document would find a
    /// <c>SourceURL:</c> printed in the page's own text - a page about this very format, say.
    /// </summary>
    [Fact]
    public void A_source_url_inside_the_markup_is_not_the_header()
    {
        const string Html = """
            Version:0.9
            StartHTML:00000097
            <html><body><p>SourceURL:https://example.invalid/bilder/Falle.png</p>
            <img src="/i/9f3.png"></body></html>
            """;

        Assert.Equal("Zwischenablage 1", AssetNaming.Derive(new NameSource { Html = Html }, Pattern, 1));
    }

    /// <summary>A <c>CF_HTML</c> envelope the way Windows hands it over, with the fragment inside.</summary>
    private static string Fragment(string markup) =>
        $"""
        Version:0.9
        StartHTML:00000097
        EndHTML:00000200
        SourceURL:https://example.invalid/bilder/Waldl%C3%A4ufer.png
        <html><body><!--StartFragment-->{markup}<!--EndFragment--></body></html>
        """;
}
