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
