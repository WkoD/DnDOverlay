using System.Globalization;
using System.Xml.Linq;
using DnDOverlay.Core.Tests.Architecture;

namespace DnDOverlay.Core.Tests.Logging;

/// <summary>
/// The identifier register (Part 11). A message lives in three places - the
/// <c>[LoggerMessage]</c> declaration, the neutral resource catalogue and the table in
/// <c>docs/protocol.md</c> - and until now nothing checked that the three agree.
/// <para>
/// Without this, a forgotten translation shows up when somebody switches language, a renamed
/// placeholder in the very line one wanted to read, and a stale "next free" hands the same number
/// out twice. The last one has already happened once, in the send-queue step.
/// </para>
/// </summary>
public sealed class EventCatalogueTests
{
    /// <summary>
    /// The number is the contract. A retired one is never reused either: were 1002 to take on a
    /// new meaning, an older counterpart would render a PLAUSIBLE BUT WRONG line from its old
    /// entry - worse than an unknown identifier, which at least looks unknown (Part 8).
    /// </summary>
    [Fact]
    public void Every_identifier_is_declared_exactly_once()
    {
        var twice = EventCatalogue.Declared
            .GroupBy(declared => declared.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(d => $"{d.Name} in {d.File}"))}")
            .ToList();

        Assert.Empty(twice);
    }

    /// <summary>
    /// Punctuation and symbols in a message are ASCII. <b>Letters are not</b> - a translation says
    /// "Bildschirm" and "größer", and a rule that forbade that would forbid the translations this
    /// catalogue exists for.
    /// <para>
    /// Measured at the table, not thought up: <c>2001</c> and <c>3006</c> carried a proper
    /// multiplication sign, and the M2c hand-run read <c>64?64</c>. The log FILE was never wrong -
    /// it is UTF-8 without a BOM and holds U+00D7 exactly as written - but a log line is read in
    /// more places than the file. The debug channel goes out through <c>OutputDebugStringA</c> and
    /// is ANSI by construction, and a console inherits whatever code page the machine has; neither
    /// is ours to fix.
    /// </para>
    /// <para>
    /// So the line is drawn where the loss is avoidable rather than at "non-ASCII": a multiplication
    /// sign, an em dash and a middle dot each have an ASCII spelling that costs nothing - <c>x</c>,
    /// <c>-</c>, <c>|</c> - while an umlaut has none, and dropping it would damage the sentence
    /// instead of the typography. A letter that arrives mangled is still the word; a symbol that
    /// arrives mangled is a question mark in the middle of a measurement.
    /// </para>
    /// <para>
    /// Every <c>LogMessages*.resx</c> is scanned, not only the neutral one, so a translation added
    /// later is held to the same rule on the day it appears. Values that flow through the
    /// placeholders are NOT covered: an asset called "Königin der Hazim'Tor" is data and belongs in
    /// the line as it is. The exception is <see cref="PixelSize"/> - not data from outside but a
    /// rendering WE choose, and it lands inside half the lines about screens and pictures.
    /// </para>
    /// </summary>
    [Fact]
    public void Symbols_in_messages_are_ASCII_while_letters_may_be_anything()
    {
        static string? Offending(string text)
        {
            foreach (var character in text)
            {
                if (character <= '\x7f' || char.IsLetter(character))
                {
                    continue;
                }

                // A combining accent belongs to the letter in front of it and travels with it.
                if (CharUnicodeInfo.GetUnicodeCategory(character)
                    is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                {
                    continue;
                }

                return $"U+{(int)character:X4}";
            }

            return null;
        }

        var wrong = EventCatalogue.Declared
            .Where(declared => Offending(declared.Message) is not null)
            .Select(declared => $"{declared.Key} declares {Offending(declared.Message)}: {declared.Message}")
            .ToList();

        foreach (var catalogue in new DirectoryInfo(
            Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "src"))
            .EnumerateFiles("LogMessages*.resx", SearchOption.AllDirectories))
        {
            wrong.AddRange(XDocument
                .Load(catalogue.FullName)
                .Root!
                .Elements("data")
                .Select(entry => (Name: (string)entry.Attribute("name")!, Text: entry.Element("value")!.Value))
                .Where(entry => Offending(entry.Text) is not null)
                .Select(entry => $"{catalogue.Name}: {entry.Name} holds {Offending(entry.Text)}: {entry.Text}"));
        }

        Assert.Empty(wrong);

        // The rule itself, both ways round - otherwise "no message offends" would also be true of a
        // rule that permits everything, and the catalogue is English today so nothing would catch it
        // until the first translation (C2).
        Assert.Null(Offending("Bildschirm zu klein: naeher als 96 Pixel, groesser als 10x."));
        Assert.Null(Offending("Bildschirm zu klein - näher als 96 Pixel, größer als 10x."));
        Assert.Equal("U+00D7", Offending("Asset decoded at 64×64."));
        Assert.Equal("U+2014", Offending("Took it in — and kept it."));

        // The values we render ourselves, on the way into those lines.
        Assert.Equal("1920x1080", new PixelSize(1920, 1080).ToString());
    }

    /// <summary>
    /// Every declared message has a catalogue entry, and both carry the SAME named placeholders.
    /// A renamed placeholder would otherwise survive until somebody reads the line it broke.
    /// </summary>
    [Fact]
    public void Every_message_has_a_catalogue_entry_with_the_same_placeholders()
    {
        var wrong = new List<string>();

        foreach (var declared in EventCatalogue.Declared)
        {
            if (!EventCatalogue.Catalogue.TryGetValue(declared.Key, out var entry))
            {
                wrong.Add($"{declared.Key} is missing from LogMessages.resx");
                continue;
            }

            var inCode = EventCatalogue.PlaceholdersOf(declared.Message).Order().ToList();
            var inCatalogue = EventCatalogue.PlaceholdersOf(entry).Order().ToList();

            if (!inCode.SequenceEqual(inCatalogue, StringComparer.Ordinal))
            {
                wrong.Add(
                    $"{declared.Key}: code has [{string.Join(", ", inCode)}], "
                    + $"catalogue has [{string.Join(", ", inCatalogue)}]");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// And the neutral entry says the SAME THING, word for word. The placeholders matching is not
    /// enough: it was, and the two halves of <c>4004</c> drifted apart underneath it.
    /// <para>
    /// <b>What it cost.</b> The declaration told the DM to "call for orphaned devices" - a grip that
    /// belongs to M5a and does not exist - while the catalogue said to reset the pairing at each
    /// device. Both are read by the same person for the same fault, and which one they got depended
    /// on whether the line was rendered in the process or out of the catalogue. Nothing was wrong
    /// with either sentence on its own, which is why nothing noticed for two milestones.
    /// </para>
    /// <para>
    /// The neutral entry only. A TRANSLATION differs on purpose - that is what it is for - and it is
    /// held to the identifier and the placeholders, which the tests above and below cover.
    /// </para>
    /// </summary>
    [Fact]
    public void The_neutral_catalogue_entry_says_what_the_declaration_says()
    {
        var wrong = EventCatalogue.Declared
            .Where(declared => EventCatalogue.Catalogue.TryGetValue(declared.Key, out var entry)
                && !string.Equals(entry, declared.Message, StringComparison.Ordinal))
            .Select(declared =>
                $"{declared.Key}{Environment.NewLine}  code:      {declared.Message}"
                + $"{Environment.NewLine}  catalogue: {EventCatalogue.Catalogue[declared.Key]}")
            .ToList();

        Assert.Empty(wrong);
    }

    /// <summary>
    /// And the other way round, or the catalogue silently keeps entries for messages that no
    /// longer exist - the kind of leftover that makes a retired number look free.
    /// </summary>
    [Fact]
    public void The_catalogue_holds_nothing_that_is_not_declared()
    {
        var known = EventCatalogue.Declared
            .Select(declared => declared.Key)
            .Concat(EventCatalogue.HandBuilt.Select(id => Handmade(id)))
            .ToHashSet(StringComparer.Ordinal);

        var leftovers = EventCatalogue.Catalogue.Keys.Where(key => !known.Contains(key)).ToList();

        Assert.Empty(leftovers);
    }

    /// <summary>
    /// The documentation is the third place, and the one a bug report is read against: the file
    /// log is written in the language of whoever wrote it, so the identifier is the key one looks
    /// up without reading a word of it (Part 8).
    /// </summary>
    [Fact]
    public void The_documentation_lists_every_message_with_its_level()
    {
        var documented = EventCatalogue.Documented.ToDictionary(entry => entry.Id);
        var wrong = new List<string>();

        foreach (var declared in EventCatalogue.Declared)
        {
            if (!documented.TryGetValue(declared.Id, out var entry))
            {
                wrong.Add($"{declared.Id} {declared.Name} is missing from docs/protocol.md");
                continue;
            }

            if (!string.Equals(entry.Name, declared.Name, StringComparison.Ordinal)
                || !string.Equals(entry.Level, declared.Level, StringComparison.Ordinal))
            {
                wrong.Add(
                    $"{declared.Id}: code says {declared.Name}/{declared.Level}, "
                    + $"documentation says {entry.Name}/{entry.Level}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_documentation_lists_nothing_that_does_not_exist()
    {
        var known = EventCatalogue.Declared
            .Select(declared => declared.Id)
            .Concat(EventCatalogue.HandBuilt)
            .ToHashSet();

        var invented = EventCatalogue.Documented.Where(entry => !known.Contains(entry.Id)).ToList();

        Assert.Empty(invented);
    }

    /// <summary>
    /// "Next free" is a promise about the next number somebody will hand out, and it is the one
    /// piece of the catalogue that cannot be derived by reading a table row - so it drifts. It
    /// already did once.
    /// </summary>
    [Fact]
    public void Next_free_is_really_next_free()
    {
        var used = EventCatalogue.Declared
            .Select(declared => declared.Id)
            .Concat(EventCatalogue.HandBuilt)
            .ToList();

        var wrong = new List<string>();

        foreach (var claim in EventCatalogue.NextFreeClaims)
        {
            var range = claim / 1000 * 1000;
            var inRange = used.Where(id => id >= range && id < range + 1000).ToList();
            var expected = inRange.Count == 0 ? range + 1 : inRange.Max() + 1;

            if (claim != expected)
            {
                wrong.Add($"documentation claims {claim} is next free in {range}s, but it is {expected}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>The resource key of a hand-built message, looked up in the catalogue by number.</summary>
    private static string Handmade(int id) =>
        EventCatalogue.Catalogue.Keys.Single(key =>
            key.StartsWith($"E{id}_", StringComparison.Ordinal));
}
