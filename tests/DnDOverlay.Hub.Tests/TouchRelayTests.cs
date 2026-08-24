using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The ceiling on how many touch reports one device may send, and what happens above it.
/// <para>
/// It is a protection limit rather than access control: what it keeps alive is the process, and it
/// is deliberately an order of magnitude above what a table with four hands on it produces
/// (Part 4).
/// </para>
/// <para>
/// What reaches the subscriber is read as the LAST point that got through, because the transient
/// slot combines what it replaces: one message comes out however many went in, and the newest
/// point in it is the newest one the ceiling let past.
/// </para>
/// </summary>
public sealed class TouchRelayTests
{
    private static readonly ScreenRef Table = new(
        new DeviceId(Guid.Parse("dddddddd-0000-0000-0000-000000000001")),
        new ScreenId("TISCH//DISPLAY1"));

    /// <summary>
    /// An evening's worth of fingers, all the way through. The counter-check to the one below, and
    /// the more important of the two: a limit that bites in ordinary use is a fault.
    /// </summary>
    [Fact]
    public async Task An_ordinary_table_never_comes_near_the_ceiling()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        // Two screens' worth of rate, ten reports a second, over five seconds.
        for (var i = 0; i < 100; i++)
        {
            relay.Take(Table, Fingers(i));
            relay.Take(Table, Fingers(i));

            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(99, await NewestAsync(watching));
    }

    /// <summary>
    /// Above the rate the reports are refused - and refused rather than swallowed, which is the
    /// difference between a limit and a silence (Part 4).
    /// </summary>
    [Fact]
    public async Task What_is_over_the_rate_is_refused()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        // Everything inside one second, so nothing rolls the window over.
        for (var i = 0; i < 500; i++)
        {
            relay.Take(Table, Fingers(i));
        }

        // The 160th got through and the 161st did not: what is over the rate is refused, and the
        // limit does not choose among what a device sends.
        Assert.Equal(159, await NewestAsync(watching));
    }

    /// <summary>The ceiling is a rate, not a budget for the whole connection.</summary>
    [Fact]
    public async Task A_fresh_second_starts_over()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        for (var i = 0; i < 500; i++)
        {
            relay.Take(Table, Fingers(i));
        }

        time.Advance(TimeSpan.FromSeconds(2));
        relay.Take(Table, Fingers(999));

        Assert.Equal(999, await NewestAsync(watching));
    }

    private static readonly SessionEvent Nothing = new SessionEvent.DevicesChanged([]);

    private static TouchRelay Relay(TimeProvider time, out SessionEvents events)
    {
        events = new SessionEvents(time);

        return new TouchRelay(events, time, NullLogger.Instance, "TISCH-PC");
    }

    /// <summary>One finger, at a place that says which report this is.</summary>
    private static TouchPointsMessage Fingers(int mark) =>
        new(Table.Screen, [new TouchTrail(1, [new TouchPoint(mark, 0.5, 0)])]);

    /// <summary>
    /// The newest point that reached the stream. The subscription is ended first so the stream
    /// finishes after draining what is queued - otherwise reading it would wait for a publisher
    /// that has already said everything it is going to.
    /// </summary>
    private static async Task<double> NewestAsync(SessionEvents.Subscription watching)
    {
        watching.Dispose();

        SessionEvent.TouchPoints? last = null;

        await foreach (var @event in watching.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            if (@event is SessionEvent.TouchPoints touch)
            {
                last = touch;
            }
        }

        Assert.NotNull(last);

        return Assert.Single(last.Touches).Points[^1].X;
    }
}
