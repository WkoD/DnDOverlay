using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// How often a running gesture is reported. It is a decision and not a mechanism, which is why it
/// is here and not a comparison against a clock inside an event handler (Part 4).
/// </summary>
public sealed class TransformThrottleTests
{
    private static readonly ItemId One = new(Guid.Parse("11111111-0000-0000-0000-000000000001"));
    private static readonly ItemId Two = new(Guid.Parse("11111111-0000-0000-0000-000000000002"));

    [Fact]
    public void The_first_report_of_a_gesture_goes_out()
    {
        Assert.True(new TransformThrottle().Allows(One, nowMs: 0, binding: false));
    }

    [Fact]
    public void A_report_inside_the_interval_is_held_back_and_one_after_it_is_not()
    {
        var throttle = new TransformThrottle(TimeSpan.FromMilliseconds(50));

        Assert.True(throttle.Allows(One, 1000, binding: false));
        Assert.False(throttle.Allows(One, 1020, binding: false));
        Assert.False(throttle.Allows(One, 1049, binding: false));
        Assert.True(throttle.Allows(One, 1050, binding: false));
    }

    /// <summary>
    /// <b>Per item, and this is the test that says why.</b> Two players each pushing their own
    /// picture must not halve each other's reporting - a global limit would do exactly that, and it
    /// would look like the table lagging under two hands (Part 4).
    /// </summary>
    [Fact]
    public void Two_pictures_moved_at_once_do_not_slow_each_other_down()
    {
        var throttle = new TransformThrottle(TimeSpan.FromMilliseconds(50));

        Assert.True(throttle.Allows(One, 1000, binding: false));
        Assert.True(throttle.Allows(Two, 1001, binding: false));
        Assert.False(throttle.Allows(One, 1010, binding: false));
        Assert.False(throttle.Allows(Two, 1011, binding: false));
    }

    /// <summary>
    /// The binding report is the one the scene is left standing on. Held back, a picture would snap
    /// back to wherever the last throttled report put it.
    /// </summary>
    [Fact]
    public void The_binding_report_is_never_held_back()
    {
        var throttle = new TransformThrottle(TimeSpan.FromMilliseconds(50));

        Assert.True(throttle.Allows(One, 1000, binding: false));
        Assert.True(throttle.Allows(One, 1001, binding: true));
    }

    /// <summary>And it starts the next gesture with a clean slate rather than inside the interval.</summary>
    [Fact]
    public void A_new_gesture_after_a_binding_report_reports_at_once()
    {
        var throttle = new TransformThrottle(TimeSpan.FromMilliseconds(50));

        Assert.True(throttle.Allows(One, 1000, binding: false));
        Assert.True(throttle.Allows(One, 1005, binding: true));
        Assert.True(throttle.Allows(One, 1006, binding: false));
    }

    /// <summary>
    /// A picture that vanished under the finger never sends its binding report, so somebody has to
    /// say so - otherwise the table keeps one entry per such item for as long as the process runs.
    /// </summary>
    [Fact]
    public void An_item_that_went_away_can_be_forgotten()
    {
        var throttle = new TransformThrottle(TimeSpan.FromMilliseconds(50));

        Assert.True(throttle.Allows(One, 1000, binding: false));
        throttle.Forget(One);

        Assert.True(throttle.Allows(One, 1001, binding: false));
    }
}
