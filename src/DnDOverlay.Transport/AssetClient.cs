using System.Globalization;
using System.Net.Http.Headers;
using DnDOverlay.Core;

namespace DnDOverlay.Transport;

/// <summary>
/// Fetches the bytes of an asset over HTTP.
/// <para>
/// It deals in BYTES, never in decoded bitmaps, and that is a project boundary rather than a
/// preference: Transport builds on net10.0, and a <c>BitmapImage</c> in here would quietly hang a
/// WPF dependency on it. Two costed promises hang on that not happening - the price of the WinUI
/// fallback from Spike A, and the slim display MSI that only weighs ~5 MB because the heavy
/// decoder lives in the control alone (Part 2, Part 5). Decoding happens in the display, with
/// WIC, in its own project.
/// </para>
/// <para>
/// The address is composed from the live connection every time, never stored. A remembered base
/// URL is a trap: when the Surface moves between WLAN and dock the WebSocket finds the new
/// address by itself while the URL still points at the old one - connected, and loading nothing
/// (Part 5). The disk-backed, size-capped cache is M2.
/// </para>
/// </summary>
public sealed class AssetClient
{
    private readonly HttpClient _http;

    public AssetClient(HttpClient http) => _http = http;

    /// <summary>
    /// Downloads one asset. Throws when it is not there - the caller decides what that costs.
    /// </summary>
    /// <param name="token">
    /// The device's token, and it is REQUIRED: since M2 the stock is behind it, or anyone on the
    /// network who could guess a hash would read the open campaign (Part 4).
    /// <para>
    /// It travels with the call rather than sitting on the client, for the same reason the address
    /// does: both belong to the live connection. A token parked on a shared <c>HttpClient</c>
    /// would also go out to every other host that client ever talks to.
    /// </para>
    /// <para>
    /// In the header, never in the query - a query parameter lands in access logs, proxy caches
    /// and browser history, and this one is the whole of the device's identity.
    /// </para>
    /// </param>
    public Task<byte[]> GetAsync(
        Uri hubBaseAddress,
        string assetPath,
        AssetId id,
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAsync(hubBaseAddress, $"{assetPath.TrimEnd('/')}/{id.Value}", token, cancellationToken);

    /// <summary>
    /// The same download, reported as it comes - what the progress ring is fed from (Part 7).
    /// <para>
    /// A separate name rather than an optional parameter on <see cref="GetAsync"/>: the token has
    /// to stay the last parameter (CA1068), and an overload that differs only by an optional
    /// argument is the kind a caller picks by accident. It also reads correctly at the call site,
    /// where the two are genuinely different acts - one buffers, the other streams.
    /// </para>
    /// </summary>
    public Task<byte[]> GetReportingAsync(
        Uri hubBaseAddress,
        string assetPath,
        AssetId id,
        string token,
        Advanced advanced,
        CancellationToken cancellationToken = default) =>
        FetchAsync(hubBaseAddress, $"{assetPath.TrimEnd('/')}/{id.Value}", token, cancellationToken, advanced);

    /// <summary>
    /// Downloads the thumbnail, which is what lets a picture stand at its place within a second
    /// while the full one is still coming (Part 5, Part 10). The width is a wish - what comes back
    /// is the step the stock holds.
    /// </summary>
    public Task<byte[]> GetThumbnailAsync(
        Uri hubBaseAddress,
        string assetPath,
        AssetId id,
        int width,
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAsync(
            hubBaseAddress,
            string.Create(
                CultureInfo.InvariantCulture, $"{assetPath.TrimEnd('/')}/{id.Value}/thumb?w={width}"),
            token,
            cancellationToken);

    /// <summary>
    /// How far a download has got: bytes so far, and the total the counterpart declared - which is
    /// <c>0</c> when it declared none. Kept as a callback rather than an event so the caller decides
    /// what it costs; the progress ring is one reader, and it is throttled at its own end (Part 7).
    /// </summary>
    public delegate void Advanced(long received, long total);

    private async Task<byte[]> FetchAsync(
        Uri hubBaseAddress,
        string path,
        string token,
        CancellationToken cancellationToken,
        Advanced? advanced = null)
    {
        ArgumentNullException.ThrowIfNull(hubBaseAddress);
        ArgumentException.ThrowIfNullOrEmpty(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(hubBaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Read the headers first when somebody is watching: without it the whole body is already
        // buffered by the time we return, and every reading would be "0 %" followed by "100 %" -
        // a ring that jumps rather than fills (Part 7).
        var completion = advanced is null
            ? HttpCompletionOption.ResponseContentRead
            : HttpCompletionOption.ResponseHeadersRead;

        using var response = await _http
            .SendAsync(request, completion, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (advanced is null)
        {
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var total = response.Content.Headers.ContentLength ?? 0;

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var collected = new MemoryStream();

        var buffer = new byte[81920];
        int read;

        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            collected.Write(buffer, 0, read);
            advanced(collected.Length, total);
        }

        return collected.ToArray();
    }
}
