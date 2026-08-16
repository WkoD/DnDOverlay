using DnDOverlay.Core;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The display's picture store. What it promises is that a picture is transferred once and then
/// stays until the ceiling forces it out - and that the one file a current item stands on is
/// never the one that goes (Part 5, Part 11).
/// </summary>
public sealed class AssetCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dndoverlay-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void What_went_in_comes_back_out()
    {
        var cache = new AssetCache(_directory);
        var picture = Bytes(64, seed: 1);

        cache.Store(Asset(1), picture);

        Assert.True(cache.TryGet(Asset(1), out var read));
        Assert.Equal(picture, read);
    }

    [Fact]
    public void A_picture_that_was_never_stored_is_simply_absent()
    {
        var cache = new AssetCache(_directory);

        Assert.False(cache.TryGet(Asset(9), out var read));
        Assert.Empty(read);
    }

    /// <summary>
    /// Original and thumbnail are two files and one picture. They are stored apart and counted
    /// together, which is what lets the next load path start from the thumbnail (Part 5).
    /// </summary>
    [Fact]
    public void The_thumbnail_lives_beside_the_original_and_counts_with_it()
    {
        var cache = new AssetCache(_directory);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.StoreThumbnail(Asset(1), Bytes(20, seed: 2));

        Assert.True(cache.TryGet(Asset(1), out var original));
        Assert.True(cache.TryGetThumbnail(Asset(1), out var thumbnail));
        Assert.Equal(100, original.Length);
        Assert.Equal(20, thumbnail.Length);
        Assert.Equal(120, cache.Bytes);
        Assert.Equal(1, cache.Count);
    }

    /// <summary>Below the ceiling nothing goes, however long it has been lying there.</summary>
    [Fact]
    public void Under_the_ceiling_nothing_is_evicted()
    {
        var cache = new AssetCache(_directory, maxBytes: 1000);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.Store(Asset(2), Bytes(100, seed: 2));

        Assert.Empty(cache.Trim(Nothing));
        Assert.Equal(2, cache.Count);
    }

    /// <summary>
    /// Over the ceiling the longest unused goes first - and "unused" means used, not stored.
    /// The oldest picture here is read again before the third arrives, so a store that ordered by
    /// arrival would drop the wrong one. That is the whole difference between a cache and a queue.
    /// </summary>
    [Fact]
    public void Over_the_ceiling_the_longest_unused_goes_first()
    {
        var cache = new AssetCache(_directory, maxBytes: 250);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.Store(Asset(2), Bytes(100, seed: 2));

        // Touching the first makes the SECOND the longest unused.
        Assert.True(cache.TryGet(Asset(1), out _));

        cache.Store(Asset(3), Bytes(100, seed: 3));

        Assert.Equal([Asset(2)], cache.Trim(Nothing));
        Assert.True(cache.TryGet(Asset(1), out _));
        Assert.False(cache.TryGet(Asset(2), out _));
        Assert.True(cache.TryGet(Asset(3), out _));
    }

    /// <summary>
    /// The promise that matters at the table: a file a current item needs is never evicted, even
    /// when it is the oldest thing in the store. Without it the picture the DM is looking at is
    /// exactly the one that disappears (Part 11, step 44a).
    /// </summary>
    [Fact]
    public void The_oldest_picture_stays_when_an_item_is_standing_on_it()
    {
        var cache = new AssetCache(_directory, maxBytes: 250);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.Store(Asset(2), Bytes(100, seed: 2));
        cache.Store(Asset(3), Bytes(100, seed: 3));

        var evicted = cache.Trim(new HashSet<AssetId> { Asset(1) });

        Assert.Equal([Asset(2)], evicted);
        Assert.True(cache.TryGet(Asset(1), out _));
    }

    /// <summary>
    /// If everything left is in use, the store stays above its ceiling. Showing the table what it
    /// is meant to show beats keeping a number - and the alternative would be evicting a picture
    /// somebody is looking at.
    /// </summary>
    [Fact]
    public void A_store_made_entirely_of_pictures_in_use_stays_over_its_ceiling()
    {
        var cache = new AssetCache(_directory, maxBytes: 150);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.Store(Asset(2), Bytes(100, seed: 2));

        Assert.Empty(cache.Trim(new HashSet<AssetId> { Asset(1), Asset(2) }));
        Assert.Equal(200, cache.Bytes);
    }

    /// <summary>Evicting a picture takes its thumbnail with it - they were one picture.</summary>
    [Fact]
    public void An_eviction_takes_the_thumbnail_too()
    {
        var cache = new AssetCache(_directory, maxBytes: 150);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.StoreThumbnail(Asset(1), Bytes(20, seed: 2));
        cache.Store(Asset(2), Bytes(100, seed: 3));

        Assert.Equal([Asset(1)], cache.Trim(Nothing));

        Assert.False(cache.TryGet(Asset(1), out _));
        Assert.False(cache.TryGetThumbnail(Asset(1), out _));
        Assert.Empty(Directory.GetFiles(_directory, Asset(1).Value + "*"));
    }

    /// <summary>
    /// An evicted picture is simply fetched again - that is what makes eviction affordable rather
    /// than final (Part 11).
    /// </summary>
    [Fact]
    public void An_evicted_picture_can_be_stored_again()
    {
        var cache = new AssetCache(_directory, maxBytes: 150);

        cache.Store(Asset(1), Bytes(100, seed: 1));
        cache.Store(Asset(2), Bytes(100, seed: 2));
        cache.Trim(Nothing);

        Assert.False(cache.TryGet(Asset(1), out _));

        cache.Store(Asset(1), Bytes(100, seed: 1));

        Assert.True(cache.TryGet(Asset(1), out var read));
        Assert.Equal(100, read.Length);
    }

    /// <summary>
    /// Nothing half-written ever stands under a valid name, and no scratch file is left lying -
    /// the store is written beside and renamed, like everything else here (Part 11).
    /// </summary>
    [Fact]
    public void A_write_leaves_no_scratch_file_behind()
    {
        var cache = new AssetCache(_directory);

        Parallel.For(0, 8, _ => cache.Store(Asset(1), Bytes(4096, seed: 1)));

        Assert.Empty(Directory.GetFiles(_directory, "*" + AtomicFile.TemporarySuffix));
        Assert.True(cache.TryGet(Asset(1), out var read));
        Assert.Equal(4096, read.Length);
    }

    /// <summary>
    /// The identifier arrives over the wire, inside a scene - so it is checked before it becomes a
    /// path, at this end too. Answering it only at the hub would make the check a property of who
    /// asked rather than of the value (Part 5).
    /// </summary>
    [Theory]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("../../etc/passwd")]
    [InlineData("short")]
    [InlineData("NOTLOWERCASEHEX0000000000000000000000000000000000000000000000000")]
    public void An_identifier_that_is_not_a_hash_never_becomes_a_path(string value)
    {
        var cache = new AssetCache(_directory);

        Assert.Throws<ArgumentException>(() => cache.Store(new AssetId(value), Bytes(10, seed: 1)));
        Assert.Throws<ArgumentException>(() => cache.TryGet(new AssetId(value), out _));
    }

    private static readonly IReadOnlySet<AssetId> Nothing = new HashSet<AssetId>();

    private static AssetId Asset(int n) => new(n.ToString(null as IFormatProvider).PadLeft(64, 'a'));

    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);

        return bytes;
    }
}
