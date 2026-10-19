namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// What gives way when the display runs out of memory: the fan from three quarters of the cap, the
/// table from the cap itself, the longest unused first, never what a hand is on (Part 6; the table
/// half decided by the DM on 19.10.2026).
/// </summary>
public sealed class MemoryPressureTests
{
    private const long Mb = 1024 * 1024;
    private const long Cap = 1000 * Mb;
    private const long Thumb = 1 * Mb;

    [Fact]
    public void The_cap_is_the_ceiling_or_a_quarter_of_the_machine_whichever_is_smaller()
    {
        Assert.Equal(MemoryPressure.Ceiling, MemoryPressure.Cap(16L * 1024 * Mb));
        Assert.Equal(1024 * Mb, MemoryPressure.Cap(4L * 1024 * Mb));

        // A machine that cannot say how much it has keeps the measured ceiling.
        Assert.Equal(MemoryPressure.Ceiling, MemoryPressure.Cap(0));
    }

    [Fact]
    public void Below_three_quarters_nothing_gives_way() =>
        Assert.Empty(MemoryPressure.StepDown(750 * Mb, Cap, [Fan(1, 100), Table(2, 100)], Thumb));

    /// <summary>The fan goes first, the card parked longest ago first, and only as far as needed.</summary>
    [Fact]
    public void From_three_quarters_the_fan_gives_way_the_oldest_card_first()
    {
        var steps = MemoryPressure.StepDown(
            800 * Mb, Cap, [Fan(1, age: 30), Fan(2, age: 10), Fan(3, age: 20), Table(4, age: 0)], Thumb);

        // 50 MB over the line; the oldest card (age 10) saves 39 MB, the next (age 20) the rest.
        Assert.Equal([Asset(2), Asset(3)], steps);
    }

    /// <summary>Under the cap the table is left alone, however much the fan could not give.</summary>
    [Fact]
    public void Between_three_quarters_and_the_cap_the_table_is_left_alone() =>
        Assert.Empty(MemoryPressure.StepDown(900 * Mb, Cap, [Table(1, age: 0), Table(2, age: 1)], Thumb));

    /// <summary>
    /// At the cap the table gives way too, the picture untouched longest first - after the whole fan,
    /// whatever their ages.
    /// </summary>
    [Fact]
    public void At_the_cap_the_table_gives_way_after_the_fan_the_longest_untouched_first()
    {
        var steps = MemoryPressure.StepDown(
            1100 * Mb,
            Cap,
            [Table(1, age: 5), Table(2, age: 1, bytes: 60), Fan(3, age: 9, bytes: 40), Table(4, age: 3)],
            Thumb);

        // The card saves 39 MB, which is not enough to get under the cap; then the table in age order.
        Assert.Equal([Asset(3), Asset(2), Asset(4)], steps);
    }

    /// <summary>A picture under a hand never goes soft, in the fan or on the table.</summary>
    [Fact]
    public void What_a_hand_is_on_never_gives_way()
    {
        var steps = MemoryPressure.StepDown(
            2000 * Mb, Cap, [Fan(1, age: 0, held: true), Table(2, age: 0, held: true), Table(3, age: 9)], Thumb);

        Assert.Equal([Asset(3)], steps);
    }

    private static DecodedPicture Fan(int n, long age, long bytes = 40, bool held = false) =>
        new(Asset(n), bytes * Mb, Parked: true, age, held);

    private static DecodedPicture Table(int n, long age, long bytes = 40, bool held = false) =>
        new(Asset(n), bytes * Mb, Parked: false, age, held);

    private static AssetId Asset(int n) => new(n.ToString(null as IFormatProvider).PadLeft(64, 'a'));
}
