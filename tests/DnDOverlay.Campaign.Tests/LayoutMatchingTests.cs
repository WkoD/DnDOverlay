using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>Which screen a layout's scene goes to (Part 3): by address, then by name, and a screen once.</summary>
public sealed class LayoutMatchingTests
{
    private static readonly DeviceId Table = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"));
    private static readonly DeviceId Reinstalled = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));

    private static readonly ScreenRef One = new(Table, new ScreenId(@"\\?\DISPLAY#ONE"));
    private static readonly ScreenRef Two = new(Table, new ScreenId(@"\\?\DISPLAY#TWO"));

    [Fact]
    public void Screens_that_are_there_are_found_by_address()
    {
        var found = LayoutMatching.Match(
            [Part(One, "TISCH-PC//1"), Part(Two, "TISCH-PC//2")],
            [new LayoutScreen(Two, "TISCH-PC//2"), new LayoutScreen(One, "TISCH-PC//1")]);

        Assert.Equal([One, Two], found);
    }

    /// <summary>The DM renamed the screen since: the address still finds it.</summary>
    [Fact]
    public void A_renamed_screen_is_found_by_its_address()
    {
        var found = LayoutMatching.Match([Part(One, "TISCH-PC//1")], [new LayoutScreen(One, "Tisch")]);

        Assert.Equal([One], found);
    }

    /// <summary>The device was set up anew and has a new address: the name finds the screen, whatever its case.</summary>
    [Fact]
    public void A_reinstalled_device_is_found_by_the_name()
    {
        var fresh = new ScreenRef(Reinstalled, new ScreenId(@"\\?\DISPLAY#ONE"));

        var found = LayoutMatching.Match([Part(One, "Tisch")], [new LayoutScreen(fresh, "TISCH")]);

        Assert.Equal([fresh], found);
    }

    [Fact]
    public void A_screen_that_is_not_there_is_not_found()
    {
        var found = LayoutMatching.Match([Part(One, "Beamer"), Part(Two, "Tisch")], [new LayoutScreen(Two, "Tisch")]);

        Assert.Equal([null, Two], found);
    }

    /// <summary>
    /// The beamer's old name now belongs to the screen the other part found by address: that screen
    /// is not handed out twice - the second part goes to the mapping table.
    /// </summary>
    [Fact]
    public void A_screen_found_by_address_is_not_given_out_again_by_name()
    {
        var gone = new ScreenRef(Reinstalled, new ScreenId(@"\\?\DISPLAY#GONE"));

        var found = LayoutMatching.Match(
            [Part(gone, "Tisch"), Part(One, "Beamer")],
            [new LayoutScreen(One, "Tisch")]);

        Assert.Equal([null, One], found);
    }

    private static LayoutPart Part(ScreenRef screen, string name) =>
        new(screen, name, new SavedScene(new PixelSize(1920, 1080), DateTimeOffset.UnixEpoch, SceneState.Empty));
}
