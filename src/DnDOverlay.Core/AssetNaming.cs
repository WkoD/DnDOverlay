using System.Net;

namespace DnDOverlay.Core;

/// <summary>
/// What an entrance was able to offer about a picture's name. Everything an entrance knows and
/// nothing about HOW it knows it - a file drop fills <see cref="FileName"/>, a browser paste fills
/// <see cref="Html"/>, a screenshot fills nothing at all.
/// <para>
/// <b>This shape is why the naming lives in <c>Core</c>.</b> Four of the five stages are plain
/// string work over what the clipboard offered; only the ASKING is Windows. The control turns an
/// <c>IDataObject</c> into this record and does nothing else, and the derivation is then provable
/// on both platforms (Part 2) instead of only where a clipboard exists.
/// </para>
/// </summary>
public sealed record NameSource
{
    /// <summary>The token's own name out of a <c>.rptok</c> - "Testfigur", not an MD5 (Part 5).</summary>
    public string? TokenName { get; init; }

    /// <summary>The file name from a file dialog or an Explorer drop, extension and all.</summary>
    public string? FileName { get; init; }

    /// <summary>The address a URL import fetched from.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The clipboard's <c>CF_HTML</c> in full, header included. Handed over raw on purpose: both
    /// halves that matter - the <c>SourceURL:</c> header and the <c>alt</c> of the fragment's
    /// <c>&lt;img&gt;</c> - are text, so splitting it in the control would move a decision out of
    /// the tested half for nothing.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>Plain text lying beside the picture. Counts only when it is a valid URL (Part 3).</summary>
    public string? Text { get; init; }
}

/// <summary>
/// The name a new stock entry starts out with, in the five stages of Part 3.
/// <para>
/// <b>Nothing is invented.</b> The last stage is a counted name rather than a guess at the content -
/// anything made up would mislead more than a number does. And the result is a PROPOSAL: the stock
/// holds it against what is already there and numbers a collision (<c>AssetStore.FreeName</c>),
/// because an import of two hundred files cannot stop to ask.
/// </para>
/// </summary>
public static class AssetNaming
{
    /// <summary>
    /// Part 3's budget for a name a person is meant to read. It is not a file name here - in M2
    /// every picture lies under its hash - but the same number governs later, when a campaign name
    /// has to fit inside a path, and a proposal that has to be cut down twice is worse than one cut
    /// once.
    /// </summary>
    public const int MaxLength = 100;

    /// <summary>The placeholder a counted name pattern carries.</summary>
    public const string CounterPlaceholder = "{n}";

    /// <summary>
    /// The five stages, first answer wins.
    /// <para>
    /// The order is the plan's and it is an order of EVIDENCE, not of convenience: a token name and
    /// a file name were chosen by a person, a URL segment was at least written by one, and only
    /// then comes a number. A real screenshot is the one case that falls through all four, and for
    /// it there is genuinely nothing to find.
    /// </para>
    /// </summary>
    /// <param name="offer">What the entrance had.</param>
    /// <param name="counterPattern">
    /// The counted name's pattern, a control setting (Part 7). Handed in per call rather than
    /// remembered, which is what makes a changed pattern act on the very next paste.
    /// </param>
    /// <param name="counter">
    /// The number to put in the pattern. It is handed in because <c>Core</c> does not know the
    /// stock; that the stock still numbers a collision afterwards is the second net, not this one.
    /// </param>
    public static string Derive(NameSource offer, string counterPattern, int counter)
    {
        ArgumentNullException.ThrowIfNull(offer);

        return Clean(offer.TokenName)
            ?? Clean(WithoutExtension(offer.FileName))
            ?? FromUrl(offer.SourceUrl)
            ?? FromClipboard(offer)
            ?? Counted(counterPattern, counter);
    }

    /// <summary>
    /// Stage 4, and it is the reason a browser paste is not nameless: the fragment usually carries
    /// an <c>&lt;img&gt;</c> with <c>alt</c> or <c>title</c>, and beside it stands the address it
    /// came from.
    /// </summary>
    private static string? FromClipboard(NameSource offer)
    {
        if (offer.Html is { } html)
        {
            // A person wrote the alt text; the address was assembled by a server. So the human
            // answer goes first, and only when there is none does the path segment count.
            if (FromImageTag(html) is { } written)
            {
                return written;
            }

            if (FromUrl(SourceUrlHeader(html)) is { } addressed)
            {
                return addressed;
            }
        }

        return FromUrl(offer.Text);
    }

    /// <summary>
    /// The last path segment, decoded and without its extension. Used twice - for the URL import
    /// and for what lies in the clipboard - because it is the same question both times.
    /// <para>
    /// Only an absolute http(s) address counts. Anything else that happens to lie in the clipboard
    /// is not an address the picture came from, and a name derived from it would be a guess.
    /// </para>
    /// </summary>
    private static string? FromUrl(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var segment = path[(path.LastIndexOf('/') + 1)..];

        // Decoded, because %C3%9C is a letter to everyone except the address bar. A malformed
        // escape is left standing rather than refused - measured, not assumed: UnescapeDataString
        // does not throw on one, and a guard for a case that cannot happen would claim a check
        // nobody could ever trip.
        return Clean(WithoutExtension(Uri.UnescapeDataString(segment)));
    }

    /// <summary>
    /// The <c>SourceURL:</c> line of the <c>CF_HTML</c> envelope. It sits in the header block above
    /// the markup, one per line, and is the only part of that envelope worth reading here.
    /// </summary>
    private static string? SourceUrlHeader(string html)
    {
        const string Key = "SourceURL:";

        foreach (var line in html.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith(Key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[Key.Length..].Trim();
            }

            // The header block ends where the markup begins; searching the whole document would
            // find a SourceURL mentioned in the page's own text.
            if (trimmed.StartsWith('<'))
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// The <c>alt</c> or <c>title</c> of the first image tag that carries one.
    /// <para>
    /// <b>Scanned rather than matched with an expression</b>, and that is a decision: this text
    /// comes from the outside and may be a megabyte of markup, where a backtracking pattern is a
    /// denial of service with extra steps. A scan over the string costs what the string costs.
    /// </para>
    /// </summary>
    private static string? FromImageTag(string html)
    {
        var index = 0;

        while ((index = html.IndexOf("<img", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = html.IndexOf('>', index);
            var tag = end < 0 ? html[index..] : html[index..end];

            if (Clean(WebUtility.HtmlDecode(Attribute(tag, "alt") ?? Attribute(tag, "title"))) is { } name)
            {
                return name;
            }

            if (end < 0)
            {
                break;
            }

            index = end + 1;
        }

        return null;
    }

    /// <summary>One attribute's value out of a single tag, quoted or bare.</summary>
    private static string? Attribute(string tag, string name)
    {
        var at = 0;

        while ((at = tag.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var after = at + name.Length;

            // Preceded by whitespace and followed by '=', or else "data-alt" and "title" inside a
            // word would both count as a hit.
            if (at == 0 || !char.IsWhiteSpace(tag[at - 1]))
            {
                at = after;
                continue;
            }

            while (after < tag.Length && char.IsWhiteSpace(tag[after]))
            {
                after++;
            }

            if (after >= tag.Length || tag[after] != '=')
            {
                at = after;
                continue;
            }

            after++;

            while (after < tag.Length && char.IsWhiteSpace(tag[after]))
            {
                after++;
            }

            if (after >= tag.Length)
            {
                return null;
            }

            if (tag[after] is '"' or '\'')
            {
                var quote = tag[after];
                var close = tag.IndexOf(quote, after + 1);

                return close < 0 ? tag[(after + 1)..] : tag[(after + 1)..close];
            }

            var space = tag.IndexOfAny([' ', '\t', '\r', '\n'], after);

            return space < 0 ? tag[after..] : tag[after..space];
        }

        return null;
    }

    /// <summary>
    /// Stage 5. The pattern is the DM's, so a pattern without the placeholder is taken at its word -
    /// every picture then proposes the same name, and the stock numbers them. Inventing a number
    /// where none was asked for would be the program overruling a setting.
    /// </summary>
    private static string Counted(string pattern, int counter)
    {
        var counted = string.IsNullOrWhiteSpace(pattern)
            ? counter.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : pattern.Replace(
                CounterPlaceholder,
                counter.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        return Clean(counted) ?? counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Everything before the last dot, the way a file name works. A name that IS an extension
    /// ("<c>.gitignore</c>") keeps it - there would be nothing left otherwise.
    /// </summary>
    private static string? WithoutExtension(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var bare = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var dot = bare.LastIndexOf('.');

        return dot > 0 ? bare[..dot] : bare;
    }

    /// <summary>
    /// What is left of a candidate once it can be shown to a person: no control characters, no line
    /// breaks, no runs of blanks, and not longer than a name is allowed to be.
    /// <para>
    /// It runs over EVERY stage and not only the outside ones. A file name carries a newline about
    /// as often as never, and "about as often as never" is the frequency at which a missing check
    /// is found by the person it happens to.
    /// </para>
    /// </summary>
    private static string? Clean(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var text = new System.Text.StringBuilder(candidate.Length);
        var blank = false;

        foreach (var character in candidate)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                blank = text.Length > 0;
                continue;
            }

            if (blank)
            {
                text.Append(' ');
                blank = false;
            }

            text.Append(character);
        }

        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > MaxLength ? text.ToString(0, MaxLength).TrimEnd() : text.ToString();
    }
}
