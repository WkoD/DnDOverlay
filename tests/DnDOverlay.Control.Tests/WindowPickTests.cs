using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>Which foreign window a hand takes hold of on a tile (Part 7): the topmost under the place decides.</summary>
public sealed class WindowPickTests
{
    private const double Reach = 0.02;

    private static readonly ScreenId Screen = new("DISPLAY1");

    /// <summary>MapTool in front, the browser behind it and reaching further up.</summary>
    private static readonly ForeignWindow MapTool = new(1, Screen, "MapTool.exe", new Rect(0.2, 0.2, 0.6, 0.6), ZOrder: 0, Minimized: false);
    private static readonly ForeignWindow Browser = new(2, Screen, "firefox.exe", new Rect(0.1, 0.1, 0.8, 0.8), ZOrder: 1, Minimized: false);

    [Fact]
    public void The_title_strip_of_the_front_window_takes_it() =>
        Assert.Equal((MapTool, WindowGrip.Strip), WindowPick.At([Browser, MapTool], new Point(0.5, 0.21), Reach));

    [Fact]
    public void A_corner_sizes() =>
        Assert.Equal((MapTool, WindowGrip.Corner), WindowPick.At([MapTool], new Point(0.795, 0.795), Reach));

    /// <summary>The browser's strip lies under MapTool's inside here: the window in front hides it.</summary>
    [Fact]
    public void A_window_in_front_hides_the_grips_behind_it()
    {
        var hidden = Browser with { Bounds = new Rect(0.1, 0.5, 0.8, 0.4) };

        Assert.Null(WindowPick.At([MapTool, hidden], new Point(0.5, 0.505), Reach));
    }

    /// <summary>Beside the front window, the browser's own strip is reachable.</summary>
    [Fact]
    public void Beside_the_front_window_the_one_behind_is_taken() =>
        Assert.Equal((Browser, WindowGrip.Strip), WindowPick.At([MapTool, Browser], new Point(0.5, 0.11), Reach));

    /// <summary>The inside of a wireframe stays the selection frame's - nothing is taken there.</summary>
    [Fact]
    public void The_inside_of_a_window_takes_nothing() =>
        Assert.Null(WindowPick.At([MapTool], new Point(0.5, 0.5), Reach));

    /// <summary>A minimised window is not on the screen: it neither takes nor hides.</summary>
    [Fact]
    public void A_minimised_window_neither_takes_nor_hides() =>
        Assert.Equal((Browser, WindowGrip.Strip), WindowPick.At([MapTool with { Minimized = true, Bounds = new Rect(0.0, 0.0, 1, 1) }, Browser], new Point(0.5, 0.11), Reach));
}
