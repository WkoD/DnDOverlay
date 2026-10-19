using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The ceiling on how many diagnostics readings one device may send (Part 4): ten a second, where
/// the stream itself sends one. A protection limit, like the touch ceiling beside it.
/// </summary>
public sealed class DiagnosticsRelayTests
{
    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000002"));

    /// <summary>The counter-check, and the more important of the two: a reading a second never comes near it.</summary>
    [Fact]
    public async Task A_reading_a_second_never_comes_near_the_ceiling()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        for (var i = 0; i < 30; i++)
        {
            relay.Take(Reading(i));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(29, await NewestAsync(watching));
    }

    [Fact]
    public async Task What_is_over_the_rate_is_refused()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        for (var i = 0; i < 50; i++)
        {
            relay.Take(Reading(i));
        }

        // The tenth got through and the eleventh did not.
        Assert.Equal(DiagnosticsRelay.Allowed - 1, await NewestAsync(watching));
    }

    [Fact]
    public async Task A_fresh_second_starts_over()
    {
        var time = new ManualTime();
        var relay = Relay(time, out var events);

        var watching = events.Open(() => Nothing);

        for (var i = 0; i < 50; i++)
        {
            relay.Take(Reading(i));
        }

        time.Advance(TimeSpan.FromSeconds(2));
        relay.Take(Reading(999));

        Assert.Equal(999, await NewestAsync(watching));
    }

    private static readonly SessionEvent Nothing = new SessionEvent.DevicesChanged([]);

    private static DiagnosticsRelay Relay(TimeProvider time, out SessionEvents events)
    {
        events = new SessionEvents(time);

        return new DiagnosticsRelay(events, time, NullLogger.Instance, Device, "TISCH-PC");
    }

    /// <summary>A reading whose frame count says which one it is.</summary>
    private static DiagnosticsMessage Reading(int mark) =>
        new(new DiagnosticsReading(mark, null, null, null, 0, 0, 0, 0, 0, 0, 0));

    /// <summary>The newest reading that reached the stream - the slot keeps one, so that is the last that got through.</summary>
    private static async Task<int> NewestAsync(SessionEvents.Subscription watching)
    {
        watching.Dispose();

        SessionEvent.Diagnostics? last = null;

        await foreach (var @event in watching.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            if (@event is SessionEvent.Diagnostics reading)
            {
                Assert.Equal(Device, reading.Device);
                last = reading;
            }
        }

        Assert.NotNull(last);

        return last.Reading.Frames;
    }
}
