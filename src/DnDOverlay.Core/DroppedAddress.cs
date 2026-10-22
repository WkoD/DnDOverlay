using System.Net;

namespace DnDOverlay.Core;

/// <summary>
/// What a drop or a paste names by address rather than by bytes, and whether that address is a
/// file on this machine.
/// <para>
/// <b>Why a drop needs more than its text:</b> a picture dragged out of a Chromium surface - a
/// browser, and VS Code's Markdown preview, which is a webview - offers no file and often no text
/// at all. What it does offer is the address, in the URL format and in the <c>src</c> of the
/// <c>&lt;img&gt;</c> in its HTML fragment. Read only from the text, such a drop was refused with
/// "nothing in that drop could be taken in" (before a game session, 18.10.2026).
/// </para>
/// <para>
/// <b>And why a local address is taken as a file, not fetched:</b> the preview shows a picture from
/// the DM's own disk under an address of its own,
/// <c>https://file+.vscode-resource.vscode-cdn.net/c%3A/…</c>. Fetching it from outside VS Code goes
/// nowhere - the address only means something inside the webview. The path inside it does, and
/// reading the file it names is exactly what dragging that file out of the Explorer would have
/// done. Only an address that names an EXISTING file is taken this way; everything else stays an
/// address, with all of Part 5's guarding on the fetch.
/// </para>
/// </summary>
public static class DroppedAddress
{
    /// <summary>
    /// Every address the offer names, most deliberate first: the text, the URL format, the
    /// <c>text/uri-list</c>, then the pictures and links of the HTML fragment.
    /// </summary>
    public static IReadOnlyList<string> Candidates(
        string? text,
        string? urlFormat,
        string? uriList,
        string? html) =>
        Collect(text, urlFormat, uriList, html, links: true);

    /// <summary>
    /// The addresses that may be taken as a file on this machine: the same as <see cref="Candidates"/>
    /// without the links of the HTML fragment. A link on a copied page names what the PAGE points to,
    /// not the picture that was copied - and since local files are tried before the offer's own bitmap,
    /// any <c>&lt;a href="file:///…"&gt;</c> that names an existing file would otherwise win over it.
    /// The VS Code case this exists for carries its picture in the URL format and the <c>&lt;img src&gt;</c>.
    /// </summary>
    public static IReadOnlyList<string> FileCandidates(
        string? text,
        string? urlFormat,
        string? uriList,
        string? html) =>
        Collect(text, urlFormat, uriList, html, links: false);

    private static List<string> Collect(
        string? text,
        string? urlFormat,
        string? uriList,
        string? html,
        bool links)
    {
        var found = new List<string>();

        void Add(string? candidate)
        {
            var trimmed = candidate?.Trim().Trim('\0');

            if (!string.IsNullOrEmpty(trimmed) && !found.Contains(trimmed, StringComparer.Ordinal))
            {
                found.Add(trimmed);
            }
        }

        Add(text);

        // The URL format carries the address and, after a line break, sometimes a title.
        Add(FirstLine(urlFormat));

        foreach (var line in (uriList ?? string.Empty).Split('\n'))
        {
            // RFC 2483: lines starting with '#' are comments.
            if (!line.TrimStart().StartsWith('#'))
            {
                Add(line);
            }
        }

        if (html is not null)
        {
            foreach (var value in Values(html, "<img", "src").Concat(links ? Values(html, "<a", "href") : []))
            {
                Add(WebUtility.HtmlDecode(value));
            }
        }

        return found;
    }

    /// <summary>
    /// The local path an address names, or <see langword="null"/> when it names none: a
    /// <c>file:</c> URI, the address VS Code's webviews give a file on disk, or a bare absolute
    /// path. Whether the file exists is the caller's question - this is string work only.
    /// <para>
    /// <b>Never a network path.</b> The address comes from whatever page the picture was dragged or
    /// copied from, and a <c>\\host\share\x.png</c> or <c>file://host/share/x.png</c> there would make
    /// the caller's existence check open an SMB connection to a host the page chose - Windows sends the
    /// user's credentials along, and an unreachable host holds the control for the SMB timeout. A local
    /// path is taken only for the webview case, a file on the DM's own disk; anything on a share is
    /// dropped by the Explorer as a file, which needs none of this.
    /// </para>
    /// </summary>
    public static string? LocalPath(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var path = Resolve(candidate);

        return path is null || IsNetworkPath(path) ? null : path;
    }

    /// <summary>
    /// Two separators at the start - <c>\\host</c>, <c>//host</c>, <c>/\host</c> - are a UNC path to
    /// Windows, and so are the <c>\\?\UNC\</c> and <c>\\.\</c> device forms.
    /// </summary>
    private static bool IsNetworkPath(string path) =>
        path.Length >= 2 && (path[0] is '\\' or '/') && (path[1] is '\\' or '/');

    private static string? Resolve(string candidate)
    {
        // Asked before the URI parser, which reads "C:\..." differently on different platforms.
        if (Path.IsPathFullyQualified(candidate))
        {
            return candidate;
        }

        // VS Code's webview resources: the host names the mechanism, the path is the file, with the
        // drive's colon escaped ("/c%3A/Users/..."). Read as a string, not through Uri: the host
        // "file+.vscode-resource.vscode-cdn.net" carries a '+', which the URI parser refuses.
        if (WebviewPath(candidate) is { } escaped)
        {
            var path = Uri.UnescapeDataString(escaped).TrimStart('/');

            // "c:/Users/..." on Windows; an absolute "/home/..." elsewhere keeps its leading slash.
            return path.Length >= 2 && path[1] == ':' ? path.Replace('/', '\\') : "/" + path;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
    }

    private static readonly string[] WebviewHosts =
    [
        "https://file+.vscode-resource.",
        "vscode-file://vscode-app/",
        "vscode-resource:",
    ];

    /// <summary>The still escaped path after a webview resource prefix, or null for any other address.</summary>
    private static string? WebviewPath(string candidate)
    {
        var prefix = WebviewHosts.FirstOrDefault(host => candidate.StartsWith(host, StringComparison.OrdinalIgnoreCase));

        if (prefix is null)
        {
            return null;
        }

        // The path starts at the first slash after the host - for the vscode-cdn host that is after
        // its domain, for the other two the prefix already ends where the path begins.
        var rest = candidate[prefix.Length..];
        var slash = prefix.EndsWith('.') ? rest.IndexOf('/', StringComparison.Ordinal) : 0;

        if (slash < 0)
        {
            return null;
        }

        var path = rest[slash..];
        var query = path.IndexOfAny(['?', '#']);

        return query < 0 ? path : path[..query];
    }

    private static string? FirstLine(string? value) => value?.Split('\n')[0];

    private static IEnumerable<string> Values(string html, string opening, string attribute)
    {
        var index = 0;

        while ((index = html.IndexOf(opening, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var after = index + opening.Length;

            // "<a" must not match "<abbr", "<img" must not match "<imgx".
            if (after < html.Length && !char.IsWhiteSpace(html[after]) && html[after] != '>')
            {
                index = after;
                continue;
            }

            var end = html.IndexOf('>', index);
            var tag = end < 0 ? html[index..] : html[index..end];

            if (AssetNaming.Attribute(tag, attribute) is { Length: > 0 } value)
            {
                yield return value;
            }

            if (end < 0)
            {
                yield break;
            }

            index = end + 1;
        }
    }
}
