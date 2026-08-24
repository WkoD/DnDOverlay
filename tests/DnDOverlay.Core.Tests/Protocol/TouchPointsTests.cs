using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Core.Tests.Protocol;

/// <summary>
/// The four rules of Part 4 that live on the message itself: what happens when a trail replaces
/// one that has not gone out yet, and what a wait in the queue costs it.
/// <para>
/// They are here rather than only in the queue's tests because they are statements about the
/// CONTENT - the queue decides that something is replaced, the message decides what replacing
/// means. And it is their only place: the two visible steps 37c and 37c1a travel to M4 with the
/// thumbnail, so without these the rules would be built in M3 and checked nowhere.
/// </para>
/// </summary>
public sealed class TouchPointsTests
{
    private static readonly ScreenId Table = new(@"\\?\DISPLAY#IVM1234#5&1a2b");
    private static readonly ScreenId Wall = new(@"\\?\DISPLAY#IVM1234#5&9z8y");

    /// <summary>
    /// <b>Discarding the delay is allowed, discarding the movement is not.</b> A plain overwrite
    /// under load would drop exactly the points that make the line, and what arrived would be a
    /// string of beads with no direction (Part 4).
    /// </summary>
    [Fact]
    public void A_replacing_trail_takes_over_the_points_of_the_one_it_displaces()
    {
        var waiting = Trails(new TouchTrail(1, [Point(0.1, 20), Point(0.2, 0)]));
        var arriving = Trails(new TouchTrail(1, [Point(0.3, 10), Point(0.4, 0)]));

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 100);

        Assert.Equal([0.1, 0.2, 0.3, 0.4], Assert.Single(merged.Touches).Points.Select(point => point.X));
    }

    /// <summary>
    /// The seam between two merged sends is the one place the fading could jump, so the older
    /// points are moved onto the newer message's clock rather than kept on their own.
    /// </summary>
    [Fact]
    public void The_points_taken_over_are_aged_by_the_gap_between_the_two_sends()
    {
        var waiting = Trails(new TouchTrail(1, [Point(0.1, 20), Point(0.2, 0)]));
        var arriving = Trails(new TouchTrail(1, [Point(0.3, 0)]));

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 100);

        // 20 and 0 on the old clock are 120 and 100 on this one; the new point keeps its own 0.
        Assert.Equal([120, 100, 0], Assert.Single(merged.Touches).Points.Select(point => point.AgeMs));
    }

    /// <summary>
    /// Two fingers stay two trails through a merge. This is the promise the "no zigzag" of the
    /// thumbnail hangs on in M4: without an identity per touch, two people pointing at once would
    /// arrive as one line jumping between them (Part 4, Part 7).
    /// </summary>
    [Fact]
    public void Two_fingers_stay_apart_when_their_messages_are_merged()
    {
        var waiting = Trails(new TouchTrail(1, [Point(0.1, 0)]), new TouchTrail(2, [Point(0.8, 0)]));
        var arriving = Trails(new TouchTrail(1, [Point(0.2, 0)]), new TouchTrail(2, [Point(0.9, 0)]));

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 100);

        Assert.Equal(2, merged.Touches.Count);
        Assert.Equal([0.1, 0.2], merged.Touches.Single(trail => trail.Touch == 1).Points.Select(point => point.X));
        Assert.Equal([0.8, 0.9], merged.Touches.Single(trail => trail.Touch == 2).Points.Select(point => point.X));
    }

    /// <summary>
    /// A finger the waiting message knew and the arriving one does not has lifted between the two
    /// sends, and where it went on its way up is as much part of the gesture as the rest.
    /// </summary>
    [Fact]
    public void A_finger_that_lifted_between_two_sends_keeps_its_trail()
    {
        var waiting = Trails(new TouchTrail(1, [Point(0.1, 0)]), new TouchTrail(2, [Point(0.8, 0)]));
        var arriving = Trails(new TouchTrail(1, [Point(0.2, 0)]));

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 50);

        Assert.Equal([0.8], merged.Touches.Single(trail => trail.Touch == 2).Points.Select(point => point.X));
    }

    /// <summary>
    /// The cap takes from the FRONT: the trail gets shorter, never the message bigger, and what is
    /// lost is its tail rather than the head that says where the finger is now (Part 4).
    /// </summary>
    [Fact]
    public void A_merge_past_the_cap_drops_the_oldest_points_and_keeps_the_newest()
    {
        var waiting = Trails(new TouchTrail(1, [.. Enumerable.Range(0, 30).Select(i => Point(i, 0))]));
        var arriving = Trails(new TouchTrail(1, [.. Enumerable.Range(30, 10).Select(i => Point(i, 0))]));

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 10);
        var points = Assert.Single(merged.Touches).Points;

        Assert.Equal(TouchTrail.MaxPoints, points.Count);
        Assert.Equal(8, points[0].X);
        Assert.Equal(39, points[^1].X);
    }

    /// <summary>
    /// Two screens are two independent sets of fingers. Should they ever meet in one slot - a
    /// build with a key of its own, a screen that changed under the message - the newer one simply
    /// wins, because joining them would invent a trail between two rooms.
    /// </summary>
    [Fact]
    public void Another_screens_fingers_are_never_joined_to_these()
    {
        var waiting = Trails(new TouchTrail(1, [Point(0.1, 0)]));
        var arriving = new TouchPointsMessage(Wall, [new TouchTrail(1, [Point(0.9, 0)])]);

        var merged = (TouchPointsMessage)arriving.Over(waiting, gapMs: 10);

        Assert.Equal(Wall, merged.Screen);
        Assert.Equal([0.9], Assert.Single(merged.Touches).Points.Select(point => point.X));
    }

    /// <summary>
    /// Ages are relative to the moment of sending, so a wait in the queue is charged to every one
    /// of them. Otherwise a trail that sat 50 ms behind a snapshot would fade 50 ms late at the
    /// other end, and the head would be drawn where the finger no longer is.
    /// </summary>
    [Fact]
    public void The_wait_in_the_queue_is_charged_to_every_point()
    {
        var message = Trails(new TouchTrail(1, [Point(0.1, 40), Point(0.2, 0)]));

        var sent = (TouchPointsMessage)message.Sent(waitedMs: 30)!;

        Assert.Equal([70, 30], Assert.Single(sent.Touches).Points.Select(point => point.AgeMs));
    }

    /// <summary>
    /// Measured at the YOUNGEST point: a trail whose head is fresh goes out whole, however old its
    /// tail naturally is. Past two sending intervals even the head is showing a finger that is no
    /// longer there, and then nothing goes out at all (Part 4).
    /// </summary>
    [Theory]
    [InlineData(150, 40, true)]
    [InlineData(150, 60, false)]
    public void A_trail_goes_out_whole_or_not_at_all_by_the_age_of_its_head(int tailMs, int waitedMs, bool expected)
    {
        var message = Trails(new TouchTrail(1, [Point(0.1, tailMs), Point(0.2, 150)]));

        Assert.Equal(expected, message.Sent(waitedMs) is not null);
    }

    /// <summary>
    /// The one message a wait cannot spoil. "The last finger has lifted" stays true however long
    /// it took to get out, and dropping it as stale would leave the ghost it exists to remove.
    /// </summary>
    [Fact]
    public void The_empty_list_survives_any_wait()
    {
        var message = new TouchPointsMessage(Table, []);

        Assert.Same(message, message.Sent(waitedMs: 5_000));
    }

    /// <summary>One slot per SCREEN, so a device with two of them keeps them apart in the queue.</summary>
    [Fact]
    public void Each_screen_has_a_slot_of_its_own()
    {
        Assert.NotEqual(
            new TouchPointsMessage(Table, []).Slot,
            new TouchPointsMessage(Wall, []).Slot);
    }

    private static TouchPointsMessage Trails(params TouchTrail[] touches) => new(Table, touches);

    private static TouchPoint Point(double x, int ageMs) => new(x, 0.5, ageMs);
}
