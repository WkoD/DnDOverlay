using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Core.Tests.Protocol;

/// <summary>
/// The diagnostics reading on the wire: rank 4, a slot of one, and nothing in it a wait can age
/// (Part 4).
/// </summary>
public sealed class DiagnosticsMessageTests
{
    /// <summary>
    /// Telemetry is what should go first when things get tight - which is why it is not on the
    /// <c>Pong</c>, and why it is transient here.
    /// </summary>
    [Fact]
    public void A_reading_travels_as_transient()
    {
        Assert.Equal(SendClass.Transient, SendClasses.Of(Reading(1)));
    }

    /// <summary>The request switches something on and stays switched: state, never dropped.</summary>
    [Fact]
    public void The_request_travels_as_state()
    {
        Assert.Equal(SendClass.State, SendClasses.Of(new DiagnosticsRequestMessage(On: true)));
    }

    /// <summary>A frame time from a moment ago is not inaccurate, it is worthless: the newer one replaces it.</summary>
    [Fact]
    public void A_newer_reading_replaces_the_waiting_one()
    {
        var older = Reading(1);
        var newer = Reading(2);

        Assert.Equal(older.Slot, newer.Slot);
        Assert.Same(newer, newer.Over(older, gapMs: 1000));
    }

    /// <summary>It carries no ages, so waiting in the queue changes nothing in it and never makes it worthless on its own.</summary>
    [Fact]
    public void Waiting_changes_nothing_in_a_reading()
    {
        var reading = Reading(1);

        Assert.Same(reading, reading.Sent(waitedMs: 5000));
    }

    private static DiagnosticsMessage Reading(int frames) =>
        new(new DiagnosticsReading(frames, 16.7, 33.3, 50, 4.2, 1, 2, 30, 1L << 30, 400L << 20, 1L << 30));
}
