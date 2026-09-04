using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// The pictures the stage draws, kept once per asset.
/// <para>
/// <b>Never the original.</b> Four screens of twenty pictures would be eighty decoded full-size
/// images for tiles the size of a thumbnail, and the stock holds hundreds more (Part 7). What is
/// drawn is the 256 px preview that every asset has anyway - decided at the start of M4, against
/// the plan's second preview step: "sharp" is a promise to the TABLE, where the players look; the
/// DM checks what lies where.
/// </para>
/// <para>
/// <b>Loaded with <see cref="BitmapCacheOption.OnLoad"/></b>, so the file is closed again straight
/// away. A stream left open would lock a file in the campaign folder, and the folder is the DM's -
/// he may move it, and the tidy-up view deletes from it (Part 3).
/// </para>
/// </summary>
internal sealed class Pictures(AssetStore store)
{
    private readonly Dictionary<AssetId, BitmapSource?> _loaded = [];

    /// <summary>
    /// The preview of one asset, or <see langword="null"/> when there is none to be had - a
    /// thumbnail that has not been written yet, or a file that has gone missing.
    /// <para>
    /// A failure is remembered as a failure. Otherwise every redraw would try the same missing file
    /// again, which at sixty frames a second is not a retry but a load.
    /// </para>
    /// </summary>
    internal BitmapSource? For(AssetId asset)
    {
        if (_loaded.TryGetValue(asset, out var known))
        {
            return known;
        }

        var picture = Load(store.ThumbnailPath(asset));

        _loaded[asset] = picture;

        return picture;
    }

    /// <summary>Forgets one asset, so the next draw reads it again.</summary>
    internal void Forget(AssetId asset) => _loaded.Remove(asset);

    private static BitmapImage? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var picture = new BitmapImage();

            picture.BeginInit();
            picture.UriSource = new Uri(path);
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.EndInit();
            picture.Freeze();

            return picture;
        }
        catch (NotSupportedException)
        {
            // Half-written or not a picture at all. The tile draws the item as a plain rectangle,
            // which is the honest answer: the arrangement is right and the picture is not there.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
