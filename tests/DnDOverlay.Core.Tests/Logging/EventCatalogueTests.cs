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
