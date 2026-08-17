using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using DnDOverlay.Core;

namespace DnDOverlay.Transport;

/// <summary>
/// Turning a fetch refusal into the word the DM is answered in.
/// <para>
/// It sits here rather than at the entrance because this is where the vocabulary is defined: a
/// caller that had to know which <see cref="FetchRejection"/> means "the address" and which means
/// "nobody answered" would be a second place holding the same knowledge, and the M2c hand-run is
/// what a second place costs - the entrance had it, wrote <c>Unreadable</c> for all of them, and
/// nothing noticed for a whole milestone.
/// </para>
/// </summary>
public static class FetchRejections
{
    /// <summary>
    /// <see cref="FetchRejection.Scheme"/> lands on the address rather than on "unreadable": an
    /// <c>ftp://</c> address and a loopback address are refused by the same check for the same
    /// reason, that we do not fetch from there.
    /// </summary>
    public static IntakeRejection AsIntake(this FetchRejection reason) => reason switch
    {
        FetchRejection.Scheme or FetchRejection.Address => IntakeRejection.Address,
        FetchRejection.TooLarge => IntakeRejection.TooLarge,

        // A page instead of a picture is exactly "what came back is not an image", which is what
        // the word means on the file side too.
        FetchRejection.ContentType => IntakeRejection.Unreadable,

        // Redirects that never end and a budget that ran out are the same answer from the table:
        // it did not arrive.
        _ => IntakeRejection.Unreachable,
    };
}

/// <summary>Why a fetch was turned away, in a shape the collected report can group by.</summary>
public enum FetchRejection
{
    /// <summary>Not <c>http</c> or <c>https</c> - and that includes a redirect that leaves them.</summary>
    Scheme,

    /// <summary>The address points inside the house (<see cref="AddressPolicy"/>).</summary>
    Address,

    /// <summary>The chain of redirects did not end within the allowed number of hops.</summary>
    TooManyRedirects,

    /// <summary>The whole fetch ran past its time budget.</summary>
    Timeout,

    /// <summary>More bytes than an asset may have.</summary>
    TooLarge,

    /// <summary>The server announced something that is not a picture - usually a page.</summary>
    ContentType,

    /// <summary>No answer, or an answer that was not a success.</summary>
    Unreachable,
}

/// <summary>What became of one fetch.</summary>
public abstract record FetchResult
{
    private FetchResult()
    {
    }

    /// <summary>The bytes, and the address they finally came from - the last hop, not the first.</summary>
    public sealed record Fetched(byte[] Bytes, Uri Address) : FetchResult;

    /// <summary>Turned away, with the reason said out loud (Part 5).</summary>
    public sealed record Refused(FetchRejection Reason, string Detail) : FetchResult;
}

/// <summary>How tightly a fetch is led. Handed in, like every other limit (rule 10).</summary>
/// <param name="MaxRedirects">
/// Five hops. Enough for the shorteners and CDN chains a picture link really travels through, far
/// short of a loop.
/// </param>
/// <param name="Timeout">The budget for the WHOLE fetch, redirects included, not per hop.</param>
/// <param name="MaxBytes">
/// The same ceiling an asset has (Part 5). One number, not a second one that could drift from it.
/// </param>
public sealed record FetchLimits(
    int MaxRedirects = 5,
    TimeSpan Timeout = default,
    long MaxBytes = 0)
{
    /// <summary>The values above, as the ordinary case.</summary>
    public static FetchLimits Default { get; } = new();

    /// <summary>The time budget, with its default filled in.</summary>
    public TimeSpan EffectiveTimeout => Timeout == default ? TimeSpan.FromSeconds(30) : Timeout;

    /// <summary>The byte ceiling, with its default filled in from the asset limits.</summary>
    public long EffectiveMaxBytes => MaxBytes == 0 ? AssetLimits.Default.MaxSourceBytes : MaxBytes;
}

/// <summary>
/// Fetching a picture from an address the DM pasted - the one place in the program that reaches out
/// to somewhere it was told about rather than somewhere it knows.
/// <para>
/// <b>The address check sits in the connection, not in front of it, and that is the whole design.</b>
/// Written the obvious way - resolve the name, check the address, then hand the URL to an HTTP
/// client - everything the rule demands happens and the fetch can still land inside the house: the
/// client resolves the name a SECOND time, and between the two answers a name is free to point
/// somewhere else. Here the resolving and the connecting are the same step, so what is checked is
/// what is dialled. "After every redirect" then falls out for nothing, because every hop is a
/// connection.
/// </para>
/// <para>
/// Four more guards ride along, and each of them is a way around the first if it is missing:
/// <b>no proxy</b> (a proxy would fetch on our behalf and never show us an address), <b>no
/// automatic redirects</b> (they would connect before we saw where to), <b>a scheme check on every
/// hop</b>, and <b>a ceiling on the bytes</b> as they are read rather than after.
/// </para>
/// </summary>
public sealed class PictureFetch : IDisposable
{
    private readonly FetchLimits _limits;
    private readonly Func<IPAddress, string?> _policy;
    private readonly HttpClient _client;

    /// <param name="limits">How tightly the fetch is led.</param>
    /// <param name="policy">
    /// Which addresses are refused. Handed in for the same reason <see cref="AssetLimits"/> is: the
    /// other four guards can only be measured against a server, every test server lives on
    /// loopback, and the real policy refuses loopback first of all. A harness that relaxes it
    /// proves the four - and the policy itself is proved as what it is, a function over an address,
    /// plus once end to end with the real one in place (guide <c>G11</c>).
    /// </param>
    public PictureFetch(FetchLimits? limits = null, Func<IPAddress, string?>? policy = null)
    {
        _limits = limits ?? FetchLimits.Default;
        _policy = policy ?? AddressPolicy.Refuses;

        var handler = new SocketsHttpHandler
        {
            // Every one of these is part of the check rather than tuning.
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = ConnectAsync,
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            // The budget is kept by us, over the whole chain - the client's own timeout would apply
            // per request and let a five-hop chain take five times as long.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DnDOverlay", "1.0"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
    }

    /// <summary>
    /// Fetches <paramref name="address"/>, following redirects by hand and checking each hop.
    /// </summary>
    public async Task<FetchResult> FetchAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri))
        {
            return new FetchResult.Refused(FetchRejection.Scheme, "That is not a web address.");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_limits.EffectiveTimeout);

        try
        {
            return await FollowAsync(uri, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchResult.Refused(
                FetchRejection.Timeout,
                $"No answer within {_limits.EffectiveTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException failed)
        {
            // The refusal is thrown from inside the connection callback, so it arrives wrapped.
            // Unwrapped here rather than let out as "connection failed", because "points into your
            // own network" is the one answer the DM actually needs.
            return Unwrap(failed) is { } refused
                ? new FetchResult.Refused(refused.Reason, refused.Detail)
                : new FetchResult.Refused(FetchRejection.Unreachable, failed.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private async Task<FetchResult> FollowAsync(Uri address, CancellationToken cancellationToken)
    {
        for (var hop = 0; ; hop++)
        {
            if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            {
                // Checked on EVERY hop, not only the first: a redirect to file:// or to a
                // magick-readable pseudo scheme is exactly how a fetch stops being a fetch.
                return new FetchResult.Refused(
                    FetchRejection.Scheme, $"Only http and https are fetched, not {address.Scheme}.");
            }

            if (hop > _limits.MaxRedirects)
            {
                return new FetchResult.Refused(
                    FetchRejection.TooManyRedirects,
                    $"The address kept redirecting past {_limits.MaxRedirects} hops.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (Redirection(response) is { } next)
            {
                address = new Uri(address, next);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FetchResult.Refused(
                    FetchRejection.Unreachable,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}."));
            }

            return await ReadAsync(response, address, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Uri? Redirection(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect
            ? response.Headers.Location
            : null;

    /// <summary>
    /// Reads the body with the ceiling applied AS IT COMES IN. A check on <c>Content-Length</c>
    /// alone would be an announcement rather than a measurement - the header may be missing, and it
    /// may lie; the same shape as trusting a file extension over the header bytes (Part 5).
    /// </summary>
    private async Task<FetchResult> ReadAsync(
        HttpResponseMessage response, Uri address, CancellationToken cancellationToken)
    {
        if (ContentTypeRefusal(response) is { } wrongType)
        {
            return wrongType;
        }

        var ceiling = _limits.EffectiveMaxBytes;

        if (response.Content.Headers.ContentLength is { } announced && announced > ceiling)
        {
            return TooLarge(announced);
        }

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var collected = new MemoryStream();

        var chunk = new byte[81920];

        while (true)
        {
            var read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            collected.Write(chunk, 0, read);

            if (collected.Length > ceiling)
            {
                return TooLarge(collected.Length);
            }
        }

        return new FetchResult.Fetched(collected.ToArray(), address);
    }

    /// <summary>
    /// The announced type, and it is a courtesy rather than a decision: what a file really is gets
    /// settled by the header bytes in <c>Imaging</c>, and nothing here overrules that. What it buys
    /// is the everyday case - a link that leads to a PAGE about the picture instead of the picture -
    /// answered in one sentence rather than as "unreadable file" after a download.
    /// <para>
    /// A missing type passes, and so does <c>application/octet-stream</c>: plenty of servers hand
    /// pictures over as either, and refusing them would turn a courtesy into a barrier.
    /// </para>
    /// </summary>
    private static FetchResult.Refused? ContentTypeRefusal(HttpResponseMessage response)
    {
        var type = response.Content.Headers.ContentType?.MediaType;

        if (type is null
            || type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FetchResult.Refused(
            FetchRejection.ContentType, $"The address answered with {type}, which is not a picture.");
    }

    private FetchResult.Refused TooLarge(long bytes) =>
        new(
            FetchRejection.TooLarge,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The picture is at least {bytes / (1024 * 1024)} MB, "
                + $"the limit is {_limits.EffectiveMaxBytes / (1024 * 1024)} MB."));

    private static FetchRefused? Unwrap(Exception? failure)
    {
        while (failure is not null)
        {
            if (failure is FetchRefused refused)
            {
                return refused;
            }

            failure = failure.InnerException;
        }

        return null;
    }

    /// <summary>
    /// Resolves and connects in ONE step. Nothing between the check and the dial - that is what the
    /// rule from Part 5 is actually asking for, and the reason it cannot be written above this
    /// method instead.
    /// </summary>
    private async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length == 0)
        {
            throw new FetchRefused(FetchRejection.Unreachable, $"{host} resolves to nothing.");
        }

        foreach (var address in addresses)
        {
            // ANY refused answer refuses the whole fetch, not just that one address. A name that
            // answers with a public address and a loopback one is not a host with a quirk, it is
            // the rebinding case itself - picking the allowed answer would walk straight into it.
            if (_policy(address) is { } why)
            {
                throw new FetchRefused(FetchRejection.Address, $"{host} resolves to {address}, and {why}.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            // Connected to the addresses that were just checked - never to the host name again.
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A refusal on its way out of the connection callback, where a return value cannot go.
    /// Internal on purpose: it exists to travel through <see cref="HttpClient"/>, and outside this
    /// class the answer is a <see cref="FetchResult.Refused"/>.
    /// </summary>
    private sealed class FetchRefused(FetchRejection reason, string detail) : Exception(detail)
    {
        internal FetchRejection Reason { get; } = reason;

        internal string Detail { get; } = detail;
    }
}
