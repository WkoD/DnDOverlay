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

    /// <summary>Downloads one asset. Throws when it is not there - the caller decides what that costs.</summary>
    public async Task<byte[]> GetAsync(
        Uri hubBaseAddress,
        string assetPath,
        AssetId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hubBaseAddress);

        var address = new Uri(hubBaseAddress, $"{assetPath.TrimEnd('/')}/{id.Value}");

        return await _http.GetByteArrayAsync(address, cancellationToken).ConfigureAwait(false);
    }
}
