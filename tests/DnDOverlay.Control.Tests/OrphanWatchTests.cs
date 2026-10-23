using DnDOverlay.Core;
using Ending = DnDOverlay.Control.OrphanWatch.Ending;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// Whether the call for orphaned devices was answered (Part 4) - the window says so when nobody did
/// (Prüfschritt 43c).
/// </summary>
public sealed class OrphanWatchTests
{
    private static readonly DeviceId Table = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f1"));
    private static readonly DeviceId Beamer = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f2"));
    private static readonly DeviceId Stranger = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f3"));

    /// <summary>The veto of 43c: called, nothing knocked - the window says nobody answered.</summary>
    [Fact]
    public void A_call_without_a_request_ends_with_nobody_answered()
    {
        var watch = new OrphanWatch();
        var call = watch.Started([]);

        watch.Saw([]);

        Assert.Equal(Ending.NobodyAnswered, watch.Ended(call));
    }

    /// <summary>A request that appears during the call is the answer.</summary>
    [Fact]
    public void A_request_appearing_during_the_call_is_an_answer()
    {
        var watch = new OrphanWatch();
        var call = watch.Started([]);

        watch.Saw([Table]);

        Assert.Equal(Ending.Answered, watch.Ended(call));
    }

    /// <summary>Decided before the call ended, the request is gone from the list - it still answered.</summary>
    [Fact]
    public void An_answer_already_decided_still_counts()
    {
        var watch = new OrphanWatch();
        var call = watch.Started([]);

        watch.Saw([Table]);
        watch.Saw([]);

        Assert.Equal(Ending.Answered, watch.Ended(call));
    }

    /// <summary>A stranger waiting since before the call answered nothing.</summary>
    [Fact]
    public void A_request_waiting_before_the_call_is_no_answer()
    {
        var watch = new OrphanWatch();
        var call = watch.Started([Stranger]);

        watch.Saw([Stranger]);

        Assert.Equal(Ending.NobodyAnswered, watch.Ended(call));
    }

    /// <summary>One that went away and came back during the call knocked again - that is an answer.</summary>
    [Fact]
    public void A_request_that_left_and_came_back_during_the_call_is_an_answer()
    {
        var watch = new OrphanWatch();
        var call = watch.Started([Beamer]);

        watch.Saw([]);
        watch.Saw([Beamer]);

        Assert.Equal(Ending.Answered, watch.Ended(call));
    }

    /// <summary>
    /// Pressing again starts the two minutes anew (Part 4): the first press's ending says nothing, and
    /// the new call is judged on its own - an answer to the first is no answer to the second.
    /// </summary>
    [Fact]
    public void A_second_press_overtakes_the_first()
    {
        var watch = new OrphanWatch();
        var first = watch.Started([]);

        watch.Saw([Table]);

        var second = watch.Started([Table]);

        Assert.Equal(Ending.None, watch.Ended(first));
        Assert.Equal(Ending.NobodyAnswered, watch.Ended(second));
    }

    /// <summary>A call is reported once; nothing is said without one - a window opened again knows of none.</summary>
    [Fact]
    public void Without_a_running_call_nothing_is_said()
    {
        var watch = new OrphanWatch();

        watch.Saw([Table]);

        Assert.Equal(Ending.None, watch.Ended(0));

        var call = watch.Started([]);

        Assert.Equal(Ending.NobodyAnswered, watch.Ended(call));
        Assert.Equal(Ending.None, watch.Ended(call));
    }
}
