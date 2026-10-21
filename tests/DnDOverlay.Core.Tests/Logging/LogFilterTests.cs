using DnDOverlay.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Tests.Logging;

/// <summary>The log panel's filter bar (Part 8): device as parent, screens beneath it, a view and not a setting.</summary>
public sealed class LogFilterTests
{
    private static readonly DeviceId Table = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"));
    private static readonly DeviceId Beamer = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"));
    private static readonly ScreenId One = new("DISPLAY1");
    private static readonly ScreenId Two = new("DISPLAY2");
    private static readonly ScreenId Hdmi = new("HDMI1");

    private static readonly Dictionary<DeviceId, IReadOnlyCollection<ScreenId>> All = new()
    {
        [Table] = [One, Two],
        [Beamer] = [Hdmi],
    };

    [Fact]
    public void Everything_shows_at_the_start()
    {
        var filter = new LogFilter();

        Assert.True(filter.Shows(Line(null, null), Known));
        Assert.True(filter.Shows(Line(Table, One), Known));
        Assert.True(filter.Shows(Line(Beamer, null), Known));
    }

    /// <summary>The device's bubble is the parent: one tap hides it with its screens, a second brings all back.</summary>
    [Fact]
    public void The_device_bubble_hides_the_device_with_its_screens()
    {
        var filter = new LogFilter();

        filter.ToggleDevice(Table, All[Table]);

        Assert.False(filter.Shows(Line(Table, null), Known));
        Assert.False(filter.Shows(Line(Table, Two), Known));
        Assert.True(filter.Shows(Line(Beamer, Hdmi), Known));
        Assert.Equal(Showing.None, filter.Of(Table, All[Table]));

        filter.ToggleDevice(Table, All[Table]);

        Assert.True(filter.Shows(Line(Table, Two), Known));
    }

    /// <summary>A screen switches only itself, and its device then stands half filled; the device's own lines stay.</summary>
    [Fact]
    public void A_screen_bubble_switches_only_itself()
    {
        var filter = new LogFilter();

        filter.ToggleScreen(Table, One, All[Table]);

        Assert.False(filter.Shows(Line(Table, One), Known));
        Assert.True(filter.Shows(Line(Table, Two), Known));
        Assert.True(filter.Shows(Line(Table, null), Known));
        Assert.Equal(Showing.Partly, filter.Of(Table, All[Table]));

        // From half filled, the device's bubble brings everything back.
        filter.ToggleDevice(Table, All[Table]);

        Assert.True(filter.Shows(Line(Table, One), Known));
    }

    /// <summary>A screen the control does not know falls back to its device - it is not thrown away (Part 8).</summary>
    [Fact]
    public void An_unknown_screen_falls_back_to_its_device()
    {
        var filter = new LogFilter();

        filter.ToggleScreen(Table, One, All[Table]);

        Assert.True(filter.Shows(Line(Table, new ScreenId("UNKNOWN")), Known));
    }

    /// <summary>
    /// A screen hidden and then gone from the device's list has no bubble left to bring it back - its
    /// lines follow the device instead of staying hidden out of sight.
    /// </summary>
    [Fact]
    public void A_hidden_screen_that_is_no_longer_known_follows_its_device()
    {
        var filter = new LogFilter();

        filter.ToggleScreen(Table, One, All[Table]);

        Assert.True(filter.Shows(Line(Table, One), device => device == Table ? [Two] : []));
    }

    /// <summary>"Only these" shows the one source and hides the rest; "show all" undoes it.</summary>
    [Fact]
    public void Only_these_and_show_all()
    {
        var filter = new LogFilter();

        filter.Only(Table, Two, All);

        Assert.False(filter.Shows(Line(null, null), Known));
        Assert.False(filter.Shows(Line(Beamer, null), Known));
        Assert.False(filter.Shows(Line(Table, One), Known));
        Assert.True(filter.Shows(Line(Table, Two), Known));

        filter.Only(null, null, All);

        Assert.True(filter.Shows(Line(null, null), Known));
        Assert.False(filter.Shows(Line(Table, Two), Known));

        filter.ShowAll();

        Assert.True(filter.Shows(Line(Beamer, Hdmi), Known));
    }

    private static IReadOnlyCollection<ScreenId> Known(DeviceId device) => All.GetValueOrDefault(device) ?? [];

    private static LogRecord Line(DeviceId? device, ScreenId? screen) =>
        new(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            LogLevel.Information,
            1000,
            "Test",
            [],
            Source: device is { } source ? new LogSource(source, "DEVICE") : null,
            Screen: screen);
}
