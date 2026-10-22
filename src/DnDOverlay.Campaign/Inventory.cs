using System.Text.Json;
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

    /// <summary>
    /// File names in <c>assets\</c> of pictures taken out of the stock whose file could not be deleted at
    /// the time - open in a viewer, being sent. Without this note the next folder check would see a
    /// hash-named picture without an entry and take it back in; with it, the file is deleted then, and
    /// named as foreign only while it is still held (M5b5; DM decision of 22.10.2026). An addition a
    /// schema-1 reader skips, so the version stays.
    /// </summary>
    public List<string> Removed { get; set; } = [];
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

/// <summary>
/// Reads the <c>schemaVersion</c> of a campaign file BEFORE it is read as what it is (Part 3: "only
/// one thing is a hard no: a schemaVersion newer than ours"). Asked after a typed read, the rule
/// depended on the newer file still parsing with the old types: a scene with a new item kind became
/// "unreadable" and could be overwritten, an inventory with a changed field was set aside and replaced
/// - after which the hard no could never fire again.
/// </summary>
internal static class SchemaVersions
{
    /// <summary>The version a file says it was written by, or <see langword="null"/> when it says none.</summary>
    /// <exception cref="JsonException">Not JSON at all - unreadable, as before.</exception>
    internal static int? Of(ReadOnlyMemory<byte> file)
    {
        // A byte order mark is no damage (the configuration learned that the hard way, Part 6).
        if (file.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            file = file[3..];
        }

        using var document = JsonDocument.Parse(file);

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind is JsonValueKind.Number)
            {
                // A number too large for an int is newer than anything this build knows.
                return property.Value.TryGetInt32(out var version) ? version : int.MaxValue;
            }
        }

        return null;
    }
}
