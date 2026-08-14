using DnDOverlay.Transport;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The part of reconnecting that can be checked without a network: how the waiting grows, where it
/// stops, and that a connection which worked starts the count again.
/// <para>
/// Seeded, so the spread is exercised rather than avoided - the alternative would be to test a
/// version of the type that nobody runs.
/// </para>
/// </summary>
public sealed class ReconnectBackoffTests
{
    [Fact]
    public void The_waiting_doubles_and_stops_at_thirty_seconds()
    {
        var backoff = new ReconnectBackoff(seed: 1);
        var plain = new[] { 1, 2, 4, 8, 16, 30, 30, 30, 30, 30 };

        foreach (var expected in plain)
        {
            var waited = backoff.Next().TotalSeconds;

            // Between 80 % and 120 % of the plain value: what grows is checked, the spread is
            // bounded, and neither is pinned to one particular random sequence.
            Assert.InRange(waited, expected * 0.8, expected * 1.2);
        }
    }

    /// <summary>
    /// After a control restarts, every display in the house lost its connection in the same
    /// moment. Without the spread they would all knock again in the same instant - and then again
    /// together two seconds later (Part 4).
    /// </summary>
    [Fact]
    public void Two_displays_do_not_come_back_in_the_same_instant()
    {
        var first = new ReconnectBackoff(seed: 1);
        var second = new ReconnectBackoff(seed: 2);

        var apart = Enumerable
            .Range(0, 5)
            .Select(_ => Math.Abs((first.Next() - second.Next()).TotalMilliseconds))
            .ToList();

        Assert.All(apart, difference => Assert.True(difference > 0));
    }

    /// <summary>
    /// Found in the M1c hand run, and it was the loudest defect so far: a refused device came back
    /// about once a second and wrote the refusal into both logs every time, until nothing else in
    /// them was readable. Five minutes is the plan's distance (Part 4).
    /// </summary>
    [Fact]
    public void A_refusal_is_waited_out_in_minutes_not_in_seconds()
    {
        var backoff = new ReconnectBackoff(seed: 1);

        Assert.InRange(backoff.Refused().TotalMinutes, 4, 6);
    }

    /// <summary>
    /// The refusal steps out of the growing wait rather than joining it: it neither grows with
    /// repetition nor resets the count that a genuine network fault is building up. What it waits
    /// for is a person changing their mind, and that has nothing to do with either.
    /// </summary>
    [Fact]
    public void A_refusal_neither_grows_nor_resets_the_ordinary_waiting()
    {
        var backoff = new ReconnectBackoff(seed: 1);

        _ = backoff.Next();
        _ = backoff.Next();

        // Three refusals in a row are three times the same distance, not 5, 10, 20 minutes.
        Assert.All(
            Enumerable.Range(0, 3).Select(_ => backoff.Refused().TotalMinutes),
            waited => Assert.InRange(waited, 4, 6));

        // And the growing one carries on where it left off - the third step, not the first.
        Assert.InRange(backoff.Next().TotalSeconds, 3.2, 4.8);
    }

    /// <summary>
    /// Without this a display that reconnects once an evening would, by the end of the night, take
    /// half a minute to come back from a two-second hiccup.
    /// </summary>
    [Fact]
    public void A_connection_that_worked_starts_the_count_again()
    {
        var backoff = new ReconnectBackoff(seed: 1);

        for (var i = 0; i < 8; i++)
        {
            _ = backoff.Next();
        }

        Assert.InRange(backoff.Next().TotalSeconds, 24, 36);

        backoff.Succeeded();

        Assert.InRange(backoff.Next().TotalSeconds, 0.8, 1.2);
    }
}
