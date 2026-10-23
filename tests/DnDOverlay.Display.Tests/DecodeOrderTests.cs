using DnDOverlay.Core;
using DnDOverlay.Transport;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// A picture stands blurred within a second and goes sharp when its original lands - kept by the order
/// things are decoded in (Part 5).
/// </summary>
public sealed class DecodeOrderTests
{
    private static readonly AssetId Map = new(new string('a', 64));
    private static readonly AssetId Dragon = new(new string('b', 64));
    private static readonly AssetId Goblin = new(new string('c', 64));

    [Fact]
    public void Nothing_waiting_is_empty()
    {
        Assert.True(new DecodeOrder().IsEmpty);
    }

    /// <summary>A thumbnail landing during a long run of originals is decoded before the originals still waiting.</summary>
    [Fact]
    public void A_thumbnail_jumps_the_queue()
    {
        var order = new DecodeOrder();

        order.Add(Original(Map));
        order.Add(Original(Dragon));
        order.Add(Thumbnail(Goblin));

        Assert.Equal([Thumbnail(Goblin), Original(Map), Original(Dragon)], Drain(order));
    }

    /// <summary>The DM thought about the order when sending them: originals keep it, whatever their size.</summary>
    [Fact]
    public void Originals_keep_the_order_they_came_in()
    {
        var order = new DecodeOrder();

        order.Add(Original(Goblin, size: 10));
        order.Add(Original(Map, size: 30_000));
        order.Add(Original(Dragon, size: 100));

        Assert.Equal([Goblin, Map, Dragon], Drain(order).Select(arrived => arrived.Asset));
    }

    /// <summary>
    /// Out of the store, thumbnail and original land together: the blur nobody has to wait for is
    /// dropped, and the pictures come up sharp one after another.
    /// </summary>
    [Fact]
    public void A_thumbnail_whose_original_is_in_hand_is_dropped()
    {
        var order = new DecodeOrder();

        order.Add(Thumbnail(Map));
        order.Add(Original(Map));

        Assert.Null(order.Take());
        Assert.Equal(Original(Map), order.Take());
        Assert.True(order.IsEmpty);
    }

    /// <summary>A thumbnail whose original was already decoded earlier is not in hand any more - it goes through, and the decode itself passes it over.</summary>
    [Fact]
    public void A_thumbnail_after_its_original_is_not_dropped_here()
    {
        var order = new DecodeOrder();

        order.Add(Original(Map));
        Assert.Equal(Original(Map), order.Take());

        order.Add(Thumbnail(Map));

        Assert.Equal(Thumbnail(Map), order.Take());
    }

    /// <summary>Thumbnails among themselves keep their arrival order too.</summary>
    [Fact]
    public void Thumbnails_keep_their_order_among_themselves()
    {
        var order = new DecodeOrder();

        order.Add(Thumbnail(Dragon));
        order.Add(Thumbnail(Map));

        Assert.Equal([Dragon, Map], Drain(order).Select(arrived => arrived.Asset));
    }

    private static List<AssetArrived> Drain(DecodeOrder order)
    {
        var taken = new List<AssetArrived>();

        while (!order.IsEmpty)
        {
            if (order.Take() is { } next)
            {
                taken.Add(next);
            }
        }

        return taken;
    }

    private static readonly Dictionary<(AssetId, bool, int), AssetArrived> Made = [];

    /// <summary>One instance per asset, kind and size, so the records compare by reference-equal byte arrays.</summary>
    private static AssetArrived Arrival(AssetId asset, bool thumbnail, int size)
    {
        lock (Made)
        {
            if (!Made.TryGetValue((asset, thumbnail, size), out var arrived))
            {
                arrived = new AssetArrived(asset, new byte[size], thumbnail);
                Made[(asset, thumbnail, size)] = arrived;
            }

            return arrived;
        }
    }

    private static AssetArrived Original(AssetId asset, int size = 1) => Arrival(asset, thumbnail: false, size);

    private static AssetArrived Thumbnail(AssetId asset) => Arrival(asset, thumbnail: true, size: 1);
}
