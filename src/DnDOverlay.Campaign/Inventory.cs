using System.Text.Json.Serialization;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// The stock's index, written to <c>inventory.json</c> in the campaign folder. It is not a save
/// FORMAT: scenes and layouts come into being when the DM saves, the stock simply is there
/// (Part 1, idea 3; Part 3).
/// <para>
/// The entry is the index over the files, so a removed entry takes its file with it - there are no
/// corpses, and because the list is per campaign the reference check is cheap (Part 3).
/// </para>
/// </summary>
public sealed class InventoryDocument
{
    /// <summary>
    /// One of exactly two numbers in the whole program - one for the configuration cluster, one
    /// for the campaign (rule 6). Everything in the campaign folder moves together.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The version this document was written by.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// When the campaign was created. A written field rather than the folder's timestamp: the
    /// folder is the exchange format, and copying it resets file times - on every entry at once
    /// (Part 3).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The entries, keyed by the source hash that identifies them.</summary>
    public List<InventoryEntry> Entries { get; set; } = [];
}

/// <summary>One image in the stock.</summary>
public sealed class InventoryEntry
{
    /// <summary>SHA-256 of the SOURCE bytes - what came in, not what goes out (Part 5).</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>The name shown in the stock. One image, one name, across the whole campaign.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When it was taken in. Written, never read off the file - see
    /// <see cref="InventoryDocument.CreatedAt"/> for why.
    /// </summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>The delivery format's extension, which is also how its file is found.</summary>
    public string Extension { get; set; } = "png";

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public bool IsAnimated { get; set; }

    /// <summary>Byte count of the DELIVERED file.</summary>
    public long Bytes { get; set; }

    /// <summary>
    /// SHA-256 of the delivered bytes. The second hash, and the reason there are two: the file
    /// name carries the source identity, so without this the display could not verify a download
    /// at all (Part 5).
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>The measurements and hashes in the shape the rest of the program uses.</summary>
    [JsonIgnore]
    public AssetMeta Meta =>
        new(PixelWidth, PixelHeight, Extension, Bytes, IsAnimated, ContentHash);
}
