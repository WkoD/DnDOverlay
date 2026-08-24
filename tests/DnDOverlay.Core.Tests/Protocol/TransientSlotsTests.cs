using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;

namespace DnDOverlay.Core.Tests.Protocol;

/// <summary>
/// Rank 4 on its own, away from any socket: a slot per kind, replaced rather than stacked, and a
/// wait that is charged to what waited (Part 4).
/// <para>
/// It stands once and serves three places - the hub's socket, the display's socket and a control's
/// event stream - so the rule is tested once as well, here, rather than three times through
/// whatever happens to be in front of it.
/// </para>
/// </summary>
public sealed class TransientSlotsTests
{
    /// <summary>
    /// Five readings of one kind are one message. This is the rule that <b>replaces the floor</b>
    /// that stood until M3c: one small queue dropping its oldest, which was the placeholder for
    /// this and said so.
    /// </summary>
    [Fact]
    public void One_kind_keeps_one_slot()
    {
        var slots = Slots();

        for (var i = 1; i <= 5; i++)
        {
            Assert.True(slots.Offer(new Reading("frames", i)));
        }

        // One message, and it carries all five marks: the slot held one at a time and none of the
        // five was thrown away on the way - this stand-in adds itself to what it displaces.
        Assert.Equal(1 + 2 + 3 + 4 + 5, Taken(slots).Single().Mark);
    }

    /// <summary>
    /// Two kinds are two slots. Without that, a hand on the table would silence the diagnostics of
    /// M5 simply by being busier than they are.
    /// </summary>
    [Fact]
    public void Two_kinds_are_two_slots()
    {
        var slots = Slots();

        Assert.True(slots.Offer(new Reading("frames", 1)));
        Assert.True(slots.Offer(new Reading("windows", 2)));

        Assert.Equal([1, 2], Taken(slots).Select(reading => reading.Mark));
    }

    /// <summary>
    /// A slot that is replaced keeps its place in the order rather than going to the back:
    /// otherwise a screen with a hand on it would hold a quieter one behind it for as long as the
    /// hand stayed down.
    /// </summary>
    [Fact]
    public void A_replaced_slot_keeps_its_turn()
    {
        var slots = Slots();

        Assert.True(slots.Offer(new Reading("frames", 1)));
        Assert.True(slots.Offer(new Reading("windows", 2)));
        Assert.True(slots.Offer(new Reading("frames", 3)));

        Assert.Equal(["frames", "windows"], Taken(slots).Select(reading => reading.Kind));
    }

    /// <summary>
    /// The waiting item is handed to the arriving one along with how much older it is, and what
    /// comes of the two is the message's business. Here it is addition, which is enough to show
    /// that nothing was silently thrown away.
    /// </summary>
    [Fact]
    public void What_replacement_means_is_left_to_the_item()
    {
        var time = new ManualTime();
        var slots = new TransientSlots<Reading>(maxSlots: 8, time);

        Assert.True(slots.Offer(new Reading("frames", 1)));

        time.Advance(TimeSpan.FromMilliseconds(40));

        Assert.True(slots.Offer(new Reading("frames", 2)));

        var taken = Taken(slots).Single();

        Assert.Equal(3, taken.Mark);
        Assert.Equal(40, taken.GapMs);
    }

    /// <summary>What the wait cost is charged on the way out, where it is finally known.</summary>
    [Fact]
    public void The_wait_is_charged_when_the_item_is_taken()
    {
        var time = new ManualTime();
        var slots = new TransientSlots<Reading>(maxSlots: 8, time);

        Assert.True(slots.Offer(new Reading("frames", 1)));

        time.Advance(TimeSpan.FromMilliseconds(70));

        Assert.Equal(70, Taken(slots).Single().WaitedMs);
    }

    /// <summary>
    /// An item the wait has made worthless is passed over without a word and the next one is
    /// tried - dropping is ordinary operation in rank 4, not an incident.
    /// </summary>
    [Fact]
    public void Something_the_wait_made_worthless_is_skipped_for_the_next()
    {
        var time = new ManualTime();
        var slots = new TransientSlots<Reading>(maxSlots: 8, time);

        Assert.True(slots.Offer(new Reading("frames", 1) { Perishable = true }));
        Assert.True(slots.Offer(new Reading("windows", 2)));

        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal([2], Taken(slots).Select(reading => reading.Mark));
    }

    /// <summary>
    /// More kinds than there are slots is a fault in this build rather than a busy moment, and it
    /// is answered the way rank 4 answers everything: by dropping, and by not taking the ones
    /// already waiting down with it.
    /// </summary>
    [Fact]
    public void A_kind_too_many_is_dropped_and_the_rest_stand()
    {
        var slots = new TransientSlots<Reading>(maxSlots: 2, TimeProvider.System);

        Assert.True(slots.Offer(new Reading("frames", 1)));
        Assert.True(slots.Offer(new Reading("windows", 2)));
        Assert.False(slots.Offer(new Reading("touch", 3)));

        Assert.Equal([1, 2], Taken(slots).Select(reading => reading.Mark));
    }

    /// <summary>
    /// Something that says nothing about slots gets one per type - the same answer Part 4 gives
    /// for the three that do, and the right default for whatever is added next.
    /// </summary>
    [Fact]
    public void Something_without_a_slot_of_its_own_gets_one_per_type()
    {
        var slots = new TransientSlots<string>(maxSlots: 8, TimeProvider.System);

        Assert.True(slots.Offer("first"));
        Assert.True(slots.Offer("second"));

        Assert.True(slots.TryTake(out var only));
        Assert.Equal("second", only);
        Assert.False(slots.TryTake(out _));
    }

    private static TransientSlots<Reading> Slots() => new(maxSlots: 8, TimeProvider.System);

    private static List<Reading> Taken(TransientSlots<Reading> slots)
    {
        var taken = new List<Reading>();

        while (slots.TryTake(out var reading))
        {
            taken.Add(reading!);
        }

        return taken;
    }

    /// <summary>
    /// A stand-in for a transient message: it says which slot it is in, adds itself to whatever it
    /// displaces, and notes what it was told about the gap and the wait so a test can read them.
    /// </summary>
    private sealed record Reading(string Kind, int Mark) : IReplacing<Reading>
    {
        public int GapMs { get; init; }

        public int WaitedMs { get; init; }

        /// <summary>Whether a wait of any length makes this one worthless.</summary>
        public bool Perishable { get; init; }

        public string Slot => Kind;

        public Reading Over(Reading waiting, int gapMs) =>
            this with { Mark = Mark + waiting.Mark, GapMs = gapMs };

        public Reading? Sent(int waitedMs) =>
            Perishable && waitedMs > 0 ? null : this with { WaitedMs = waitedMs };
    }
}
