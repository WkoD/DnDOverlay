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

    private async Task<byte[]> FetchAsync(
        Uri hubBaseAddress, string path, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubBaseAddress);
        ArgumentException.ThrowIfNullOrEmpty(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(hubBaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
