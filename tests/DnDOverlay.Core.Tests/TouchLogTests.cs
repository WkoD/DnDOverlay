using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// What one screen collects between two sends. The window only translates events into this; every
/// rule about what a report contains is here, where it can be read without a digitizer.
/// </summary>
public sealed class TouchLogTests
{
    private static readonly ScreenId Table = new(@"\\?\DISPLAY#IVM1234#5&1a2b");

    /// <summary>
    /// A table nobody is touching says nothing at all. It is the ordinary state, and it is the
    /// reason this traffic costs nothing on an evening where the DM does all the moving (Part 4).
    /// </summary>
    [Fact]
    public void An_untouched_table_says_nothing()
    {
        var log = new TouchLog(new ManualTime());

        Assert.Null(log.Take(Table));
    }

    /// <summary>
    /// A point is not a place, it is a place AND a moment. The ages are what the fading is driven
    /// by, so they are worked out against the moment of sending rather than written down as they
    /// happen (Part 4).
    /// </summary>
    [Fact]
    public void Every_point_is_aged_against_the_moment_it_is_taken()
    {
        var time = new ManualTime();
        var log = new TouchLog(time);

        log.Moved(1, 0.1, 0.2);
        time.Advance(TimeSpan.FromMilliseconds(30));
        log.Moved(1, 0.3, 0.4);
        time.Advance(TimeSpan.FromMilliseconds(20));

        var trail = Assert.Single(log.Take(Table)!.Touches);

        Assert.Equal([50, 20], trail.Points.Select(point => point.AgeMs));
    }

    /// <summary>
    /// Two people pointing at once are two trails, and they stay two: the identity is what keeps
    /// them from arriving as one line jumping between them (Part 4, Part 7).
    /// </summary>
    [Fact]
    public void Two_fingers_are_two_trails_in_one_message()
    {
        var log = new TouchLog(new ManualTime());

        log.Moved(1, 0.1, 0.1);
        log.Moved(2, 0.9, 0.9);
        log.Moved(1, 0.2, 0.2);

        var message = log.Take(Table)!;

        Assert.Equal(2, message.Touches.Count);
        Assert.Equal([0.1, 0.2], message.Touches.Single(trail => trail.Touch == 1).Points.Select(point => point.X));
        Assert.Equal([0.9], message.Touches.Single(trail => trail.Touch == 2).Points.Select(point => point.X));
    }

    /// <summary>
    /// A finger held on one spot goes on being reported at that spot. Windows raises nothing while
    /// it does not move, so a log that only passed on events would let the receiver's 300 ms decay
    /// wipe out a hand that is very much still there - and the commonest thing anybody does at a
    /// table is hold a finger on a place (Part 7).
    /// </summary>
    [Fact]
    public void A_resting_finger_goes_on_being_reported_where_it_rests()
    {
        var time = new ManualTime();
        var log = new TouchLog(time);

        log.Moved(1, 0.4, 0.6);

        _ = log.Take(Table);

        time.Advance(TimeSpan.FromMilliseconds(100));

        var resting = Assert.Single(Assert.Single(log.Take(Table)!.Touches).Points);

        Assert.Equal(0.4, resting.X);
        Assert.Equal(0.6, resting.Y);

        // Age zero, because that is the truth: the finger is there at this moment. It is the
        // movement that stopped, not the finger that left.
        Assert.Equal(0, resting.AgeMs);
    }

    /// <summary>
    /// The last points of a finger go out BEFORE the empty list that says it has gone. The empty
    /// list exists so nobody has to wait out the decay; sending it in place of the end of the
    /// movement would buy that at the price of the movement (Part 4).
    /// </summary>
    [Fact]
    public void A_lift_reports_the_last_points_first_and_the_empty_list_after()
    {
        var log = new TouchLog(new ManualTime());

        log.Moved(1, 0.1, 0.1);
        log.Lifted(1, 0.2, 0.2);

        Assert.Equal([0.1, 0.2], Assert.Single(log.Take(Table)!.Touches).Points.Select(point => point.X));
        Assert.Empty(log.Take(Table)!.Touches);
    }

    /// <summary>Said once. After that there is simply nothing to say again.</summary>
    [Fact]
    public void The_empty_list_is_said_once()
    {
        var log = new TouchLog(new ManualTime());

        log.Moved(1, 0.1, 0.1);
        log.Lifted(1, 0.1, 0.1);

        _ = log.Take(Table);

        Assert.Empty(log.Take(Table)!.Touches);
        Assert.Null(log.Take(Table));
    }

    /// <summary>
    /// One finger lifting while another is still down is not "the last finger": the empty list
    /// would be a lie, and the receiver would drop a hand that is still on the table.
    /// </summary>
    [Fact]
    public void One_finger_of_two_lifting_is_not_the_empty_list()
    {
        var log = new TouchLog(new ManualTime());

        log.Moved(1, 0.1, 0.1);
        log.Moved(2, 0.9, 0.9);
        log.Lifted(1, 0.1, 0.1);

        _ = log.Take(Table);

        var still = log.Take(Table)!;

        Assert.Equal(2, Assert.Single(still.Touches).Touch);
    }

    /// <summary>
    /// A finger is forgotten when its trail is taken, and that is the ONLY time - so whoever drains
    /// this has to keep draining whether or not there is anywhere to send.
    /// <para>
    /// The reporter therefore runs with the process rather than with a connection. Built the other
    /// way round first, and the cost is easy to miss: a table with nobody listening would keep one
    /// entry per touch for as long as no control was there, on a device the last milestone spent
    /// ten hours proving flat.
    /// </para>
    /// </summary>
    [Fact]
    public void A_lifted_finger_is_only_forgotten_when_its_trail_is_taken()
    {
        var log = new TouchLog(new ManualTime());

        for (var touch = 0; touch < 50; touch++)
        {
            log.Moved(touch, 0.1, 0.1);
            log.Lifted(touch, 0.1, 0.1);
        }

        // Everything at once, because nobody asked in between - and after it, nothing is left.
        Assert.Equal(50, log.Take(Table)!.Touches.Count);
        Assert.Empty(log.Take(Table)!.Touches);
        Assert.Null(log.Take(Table));
    }

    /// <summary>
    /// A finger the system has let go of without saying so stops being reported - and the empty
    /// list still goes out, so nobody has to sit out the decay for a touch we already know is over.
    /// <para>
    /// <b>Measured before it was built</b> (first run of the gesture block): something lay on the
    /// screen, its lift never arrived, and the resting rule reported the spot ten times a second
    /// with one point each for over ten minutes. Resting and stuck cannot be told apart from the
    /// events - Windows raises none for either - so the window asks whether the touch still exists
    /// and says so here.
    /// </para>
    /// </summary>
    [Fact]
    public void A_finger_the_system_lost_stops_being_reported()
    {
        var time = new ManualTime();
        var log = new TouchLog(time);

        log.Moved(1, 0.4, 0.6);

        _ = log.Take(Table);

        time.Advance(TimeSpan.FromSeconds(1));

        // Still down as far as anybody here knows: reported at its place, once per round.
        Assert.Single(Assert.Single(log.Take(Table)!.Touches).Points);

        log.Vanished(1);

        // The last word about it, and then silence - not ten reports a second for ever.
        Assert.Empty(log.Take(Table)!.Touches);
        Assert.Null(log.Take(Table));
        Assert.Null(log.Take(Table));
    }

    /// <summary>
    /// The cap takes from the front, so a finger that has been drawing for a while during a stall
    /// keeps its head. The trail gets shorter, never the message bigger (Part 4).
    /// </summary>
    [Fact]
    public void A_long_movement_between_two_sends_keeps_its_newest_points()
    {
        var log = new TouchLog(new ManualTime());

        for (var i = 0; i < TouchTrail.MaxPoints + 20; i++)
        {
            log.Moved(1, i, 0.5);
        }

        var points = Assert.Single(log.Take(Table)!.Touches).Points;

        Assert.Equal(TouchTrail.MaxPoints, points.Count);
        Assert.Equal(20, points[0].X);
        Assert.Equal(TouchTrail.MaxPoints + 19, points[^1].X);
    }
}
