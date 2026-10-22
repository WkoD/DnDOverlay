using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows.Tests;

/// <summary>
/// The survey of foreign windows, on the real desktop (Part 6). What a test can rely on there is the
/// shape, not the content: whatever is open, none of it is ours, every frame is normalised to its
/// screen, and the depths count down from the top without a gap.
/// </summary>
public sealed class ForeignWindowSurveyTests
{
    [Fact]
    public void The_survey_leaves_out_our_own_windows_and_normalises_the_rest()
    {
        var monitors = Screens.Enumerate("TEST-PC");

        // A desk with every window of this process counted as "ours" - and the test host is this
        // process, so a window it might have must not appear.
        var windows = ForeignWindowSurvey.Survey(monitors, Environment.ProcessId);

        Assert.All(windows, window =>
        {
            Assert.False(string.IsNullOrWhiteSpace(window.Program));
            Assert.Contains(monitors, monitor => monitor.Screen.ScreenId == window.ScreenId);
            Assert.True(double.IsFinite(window.Bounds.X) && double.IsFinite(window.Bounds.Width));
            Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0);
        });

        Assert.Equal(Enumerable.Range(0, windows.Count), windows.Select(window => window.ZOrder));
    }

    /// <summary>
    /// The wireframe is the visible frame, <c>SetWindowPos</c> sets the window rectangle with its
    /// invisible border (Part 6). "Fill this screen" must put the VISIBLE frame on the screen's edges,
    /// so the rectangle handed over reaches the border beyond them - otherwise every move shifted the
    /// window by the border and shrank it by twice it.
    /// </summary>
    [Fact]
    public void A_move_widens_the_rectangle_by_the_invisible_border()
    {
        var placed = ForeignWindowSurvey.WindowRect(new Rect(0, 0, 1, 1), Monitor("RIGHT", 1920, 0, 1920, 1080), (7, 0, 7, 7));

        Assert.Equal((1913, 0, 1934, 1087), placed);
    }

    /// <summary>Without a border to read, the frame goes over as it is.</summary>
    [Fact]
    public void Without_a_border_the_frame_goes_over_unchanged()
    {
        var placed = ForeignWindowSurvey.WindowRect(new Rect(0.25, 0.5, 0.5, 0.25), Monitor("LEFT", 0, 0, 2560, 1440), (0, 0, 0, 0));

        Assert.Equal((640, 720, 1280, 360), placed);
    }

    private static MonitorInfo Monitor(string name, int x, int y, int width, int height) =>
        new(
            new ScreenInfo(new ScreenId($@"\\?\DISPLAY#{name}"), $"PC//{name}", null, new PixelSize(width, height), 96, IsPrimary: false),
            x,
            y,
            width,
            height);

    /// <summary>A handle that does not exist is gone - not an error, and nothing is touched.</summary>
    [Fact]
    public void A_window_that_is_not_there_is_gone()
    {
        var monitors = Screens.Enumerate("TEST-PC");

        if (monitors.Count == 0)
        {
            return;
        }

        var monitor = monitors[0];

        var outcome = ForeignWindowSurvey.Lay(0x7FFF_FFF0, "MapTool", WindowAction.Maximize, null, monitor, out var after);

        Assert.Equal(WindowOutcome.Gone, outcome);
        Assert.Null(after);
    }
}
