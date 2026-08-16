using System.Net;
using System.Text;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The five guards of the URL import (Part 5). Four of them are measured against a server, and
/// every server a test can start lives on loopback - which the real policy refuses first of all.
/// <para>
/// <b>So the split is stated rather than glossed over</b> (guide <c>G11</c>): these tests hand in a
/// relaxed policy and thereby prove the OTHER four guards. The address check itself is proved where
/// it is provable - as a function, in <see cref="AddressPolicyTests"/> - and once here end to end,
/// with the real policy in place, against a server that then never sees a request.
/// </para>
/// </summary>
public sealed class PictureFetchTests
{
    /// <summary>Loopback is allowed through, so the other guards have something to answer them.</summary>
    private static readonly Func<IPAddress, string?> Relaxed = _ => null;

    private static readonly byte[] Picture = Encoding.ASCII.GetBytes("\u0089PNG\r\n\u001a\n and some pixels");

    /// <summary>The ordinary case, and the counter-check to every refusal below.</summary>
    [Fact]
    public async Task A_picture_at_an_address_is_fetched()
    {
        await using var host = new TestWebHost(_ => new Reply(Body: Picture));
        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/ork.png"), TestContext.Current.CancellationToken);

        var fetched = Assert.IsType<FetchResult.Fetched>(result);
        Assert.Equal(Picture, fetched.Bytes);
    }

    /// <summary>
    /// <b>The one that matters, and it is measured at the server:</b> with the real policy in place,
    /// an address on loopback is refused and the server never sees a request. Not "the bytes were
    /// discarded" - the connection was never made.
    /// </summary>
    [Fact]
    public async Task With_the_real_policy_a_loopback_address_never_reaches_the_server()
    {
        await using var host = new TestWebHost(_ => new Reply(Body: Picture));
        using var fetch = new PictureFetch();

        var result = await fetch.FetchAsync(host.At("/ork.png"), TestContext.Current.CancellationToken);

        var refused = Assert.IsType<FetchResult.Refused>(result);

        Assert.Equal(FetchRejection.Address, refused.Reason);
        Assert.Equal(0, host.Requests);
    }

    /// <summary>
    /// The same thing one hop later, which is the case the plan spells out: a public-looking address
    /// that redirects into the house. The redirect is followed by hand, so the second hop is checked
    /// exactly like the first - and the server sees the first request and never the second.
    /// </summary>
    [Fact]
    public async Task A_redirect_into_the_house_is_refused_at_the_hop_it_happens()
    {
        await using var inside = new TestWebHost(_ => new Reply(Body: Picture));
        await using var outside = new TestWebHost(
            _ => new Reply(302, Location: $"http://127.0.0.1:{inside.Port}/ork.png"));

        // Relaxed only for the FIRST host, which stands in for a public one; the address the
        // redirect points at is judged by the real policy.
        var addresses = 0;

        using var fetch = new PictureFetch(policy: address =>
            Interlocked.Increment(ref addresses) == 1 ? null : AddressPolicy.Refuses(address));

        var result = await fetch.FetchAsync(outside.At("/bild"), TestContext.Current.CancellationToken);

        var refused = Assert.IsType<FetchResult.Refused>(result);

        Assert.Equal(FetchRejection.Address, refused.Reason);
        Assert.Equal(1, outside.Requests);
        Assert.Equal(0, inside.Requests);
    }

    /// <summary>A chain that ends in a picture is followed, up to the allowed number of hops.</summary>
    [Fact]
    public async Task A_short_chain_of_redirects_is_followed()
    {
        await using var host = new TestWebHost(path => path switch
        {
            "/a" => new Reply(301, Location: "/b"),
            "/b" => new Reply(302, Location: "/c"),
            _ => new Reply(Body: Picture),
        });

        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/a"), TestContext.Current.CancellationToken);

        var fetched = Assert.IsType<FetchResult.Fetched>(result);

        Assert.Equal(Picture, fetched.Bytes);
        Assert.EndsWith("/c", fetched.Address.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>And a chain that does not end is cut off rather than followed forever.</summary>
    [Fact]
    public async Task A_redirect_loop_ends_at_the_limit()
    {
        await using var host = new TestWebHost(_ => new Reply(302, Location: "/round"));
        using var fetch = new PictureFetch(new FetchLimits(MaxRedirects: 3), Relaxed);

        var result = await fetch.FetchAsync(host.At("/round"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.TooManyRedirects, Assert.IsType<FetchResult.Refused>(result).Reason);
        Assert.True(host.Requests <= 5, $"the loop was walked {host.Requests} times");
    }

    /// <summary>
    /// A redirect that leaves http entirely. Checked on every hop, because a scheme check on the
    /// first address alone is a check on what the DM pasted rather than on what is fetched.
    /// </summary>
    [Fact]
    public async Task A_redirect_out_of_http_is_refused()
    {
        await using var host = new TestWebHost(_ => new Reply(302, Location: "file:///C:/Windows/win.ini"));
        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/bild"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.Scheme, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>What the DM pasted has to be a web address in the first place.</summary>
    [Theory]
    [InlineData("file:///C:/Bilder/ork.png")]
    [InlineData("ftp://example.invalid/ork.png")]
    [InlineData("nicht einmal eine adresse")]
    public async Task Only_http_and_https_are_fetched(string address)
    {
        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(address, TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.Scheme, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>
    /// A link that leads to a page about the picture rather than to the picture - the everyday
    /// mistake, answered in one sentence instead of as "unreadable file" after a download.
    /// </summary>
    [Fact]
    public async Task A_page_where_a_picture_was_promised_is_refused_by_its_type()
    {
        await using var host = new TestWebHost(
            _ => new Reply(Body: Encoding.ASCII.GetBytes("<html>"), ContentType: "text/html"));

        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/seite"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.ContentType, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>
    /// The counter-check to that guard, and it is why the guard is deliberately loose: plenty of
    /// servers hand a picture over as octet-stream or announce nothing at all. What a file really
    /// is gets settled by its header bytes in <c>Imaging</c>, never here.
    /// </summary>
    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task A_picture_without_a_proper_type_still_comes_through(string? type)
    {
        await using var host = new TestWebHost(_ => new Reply(Body: Picture, ContentType: type));
        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/ork"), TestContext.Current.CancellationToken);

        Assert.IsType<FetchResult.Fetched>(result);
    }

    /// <summary>An announced size over the ceiling is refused on the announcement - cheaply.</summary>
    [Fact]
    public async Task An_announced_size_over_the_ceiling_is_refused()
    {
        await using var host = new TestWebHost(_ => new Reply(Body: new byte[4096]));
        using var fetch = new PictureFetch(new FetchLimits(MaxBytes: 1024), Relaxed);

        var result = await fetch.FetchAsync(host.At("/gross.png"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.TooLarge, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>
    /// <b>And the case the announcement cannot cover.</b> Without <c>Content-Length</c> the body ends
    /// with the connection, so there is nothing to check beforehand - the ceiling has to be kept
    /// while the bytes are read. A server that simply omits the header would otherwise walk past a
    /// guard that looks like it is there.
    /// </summary>
    [Fact]
    public async Task An_unannounced_body_is_stopped_while_it_is_read()
    {
        await using var host = new TestWebHost(_ => new Reply(Body: new byte[4096], DeclareLength: false));
        using var fetch = new PictureFetch(new FetchLimits(MaxBytes: 1024), Relaxed);

        var result = await fetch.FetchAsync(host.At("/gross.png"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.TooLarge, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>
    /// The budget is for the whole fetch. A server that answers slowly enough to be a hang is
    /// turned away with a sentence rather than left to hold the DM's control.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_server_that_never_answers_runs_out_of_its_budget()
    {
        await using var host = new TestWebHost(
            _ => new Reply(Body: Picture, Delay: TimeSpan.FromSeconds(20)));

        using var fetch = new PictureFetch(new FetchLimits(Timeout: TimeSpan.FromMilliseconds(300)), Relaxed);

        var result = await fetch.FetchAsync(host.At("/langsam.png"), TestContext.Current.CancellationToken);

        Assert.Equal(FetchRejection.Timeout, Assert.IsType<FetchResult.Refused>(result).Reason);
    }

    /// <summary>A server that answers with an error says so, and the DM is told which error.</summary>
    [Fact]
    public async Task A_server_error_is_reported_as_what_it_was()
    {
        await using var host = new TestWebHost(_ => new Reply(404));
        using var fetch = new PictureFetch(policy: Relaxed);

        var result = await fetch.FetchAsync(host.At("/weg.png"), TestContext.Current.CancellationToken);

        var refused = Assert.IsType<FetchResult.Refused>(result);

        Assert.Equal(FetchRejection.Unreachable, refused.Reason);
        Assert.Contains("404", refused.Detail, StringComparison.Ordinal);
    }
}
