using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// What the progress ring is fed from (Part 7). The promises are the ones a ring makes to whoever
/// is watching it, and each is a way for it to lie: filling before the picture is drawable, going
/// backwards, appearing for a picture nobody is waiting for, or never stopping.
/// </summary>
public sealed class AssetProgressTrackerTests
{
    private static readonly AssetId Picture = new(new string('a', 64));
    private static readonly AssetId Other = new(new string('b', 64));

    /// <summary>
    /// A device with nothing to do sends <b>nothing at all</b> - not an empty message. That is the
    /// normal case for a table where nothing is happening, and an empty reading a few times a
    /// second would be traffic whose only content is that there is no content (Part 4).
    /// </summary>
    [Fact]
    public void With_nothing_loading_there_is_nothing_to_send()
    {
        Assert.Null(new AssetProgressTracker().Reading());
    }

    [Fact]
    public void The_fraction_rises_as_bytes_arrive()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);

        var fractions = new List<double>();

        foreach (var received in new long[] { 100, 400, 900 })
        {
            tracker.Received(Picture, received, total: 1000);
            fractions.Add(Single(tracker).Fraction);
        }

        Assert.Equal([0.1, 0.4, 0.9], fractions);
        Assert.Equal(AssetLoadState.Loading, Single(tracker).State);
    }

    /// <summary>
    /// <b>Done means decoded</b>, not "the last byte arrived". On a large picture decoding costs
    /// real time, and a ring that filled with the last byte would stand full while the table still
    /// showed nothing (Part 11).
    /// </summary>
    [Fact]
    public void All_the_bytes_are_not_yet_done()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);
        tracker.Received(Picture, 1000, total: 1000);

        Assert.Equal(1, Single(tracker).Fraction);
        Assert.NotEqual(AssetLoadState.Done, Single(tracker).State);

        tracker.Verifying(Picture);
        Assert.Equal(AssetLoadState.Verifying, Single(tracker).State);

        tracker.Decoding(Picture);
        Assert.Equal(AssetLoadState.Decoding, Single(tracker).State);

        tracker.Done(Picture);
        Assert.Equal(AssetLoadState.Done, Single(tracker).State);
    }

    /// <summary>
    /// A picture already in the store is finished at once, and no request ever goes out. The ring
    /// must not appear for a picture nobody is waiting for (Part 5).
    /// </summary>
    [Fact]
    public void A_picture_already_in_the_store_is_reported_finished_straight_away()
    {
        var tracker = new AssetProgressTracker();

        tracker.AlreadyHere(Picture);

        var load = Single(tracker);
        Assert.Equal(AssetLoadState.Done, load.State);
        Assert.Equal(1, load.Fraction);
    }

    /// <summary>
    /// A retry continues the attempt. Starting over would put the ring back to zero, and that reads
    /// as "this is going wrong" when it is merely going slowly (Part 7).
    /// </summary>
    [Fact]
    public void A_retry_does_not_secretly_start_over()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);
        tracker.Received(Picture, 700, total: 1000);

        tracker.Started(Picture);

        Assert.Equal(0.7, Single(tracker).Fraction);

        // And a second attempt that begins from the first byte does not pull it back either.
        tracker.Received(Picture, 10, total: 1000);

        Assert.Equal(0.7, Single(tracker).Fraction);
    }

    /// <summary>
    /// A counterpart that does not say how big the picture is leaves the fraction where it was.
    /// A ring guessing is worse than a ring waiting.
    /// </summary>
    [Fact]
    public void Without_a_size_the_fraction_is_not_invented()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);
        tracker.Received(Picture, 500, total: 0);

        Assert.Equal(0, Single(tracker).Fraction);
        Assert.Equal(AssetLoadState.Loading, Single(tracker).State);
    }

    /// <summary>
    /// Finally unsuccessful is a state of its own, not a ring that stops filling - the item shows a
    /// placeholder with a reason instead (Part 7). How far it got stays in the reading.
    /// </summary>
    [Fact]
    public void A_failed_attempt_ends_on_failed_and_keeps_how_far_it_got()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);
        tracker.Received(Picture, 300, total: 1000);
        tracker.Failed(Picture);

        var load = Single(tracker);
        Assert.Equal(AssetLoadState.Failed, load.State);
        Assert.Equal(0.3, load.Fraction);
    }

    /// <summary>
    /// A final state is worth saying once. After it has gone out the reading is about what is still
    /// running - and once nothing is, the device falls silent again.
    /// </summary>
    [Fact]
    public void What_has_finished_drops_out_of_the_next_reading()
    {
        var tracker = new AssetProgressTracker();

        tracker.Started(Picture);
        tracker.Started(Other);
        tracker.Done(Picture);

        Assert.Equal(2, tracker.Reading()!.Loads.Count);

        tracker.Settle();

        Assert.Equal([Other], tracker.Reading()!.Loads.Select(load => load.Asset));

        tracker.Failed(Other);
        tracker.Settle();

        Assert.Null(tracker.Reading());
    }

    /// <summary>
    /// The reading goes into the progress queue, not the state queue - rank 3, its own drawer. It
    /// is the first message in this protocol that is not state, so this is the assertion that makes
    /// the whole ranking do any work (Part 4).
    /// </summary>
    [Fact]
    public void The_reading_travels_as_progress_and_not_as_state()
    {
        var tracker = new AssetProgressTracker();
        tracker.Started(Picture);

        Assert.Equal(SendClass.Progress, SendClasses.Of(tracker.Reading()!));
    }

    private static AssetLoad Single(AssetProgressTracker tracker) =>
        Assert.Single(tracker.Reading()!.Loads);
}
