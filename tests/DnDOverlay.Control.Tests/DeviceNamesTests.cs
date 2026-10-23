using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>Device and screen names are unique across all devices (Part 3) - what a rename in the device band may take.</summary>
public sealed class DeviceNamesTests
{
    private static readonly DeviceId Table = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000d1"));
    private static readonly DeviceId Beamer = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000d2"));
    private static readonly ScreenRef TableOne = new(Table, new ScreenId("DISPLAY1"));
    private static readonly ScreenRef TableTwo = new(Table, new ScreenId("DISPLAY2"));
    private static readonly ScreenRef BeamerOne = new(Beamer, new ScreenId("DISPLAY1"));

    private static readonly IReadOnlyList<DeviceView> All =
    [
        new(Table, "TISCH-PC", Connected: true, [Screen(TableOne, "Tisch"), Screen(TableTwo, "Wand")]),
        new(Beamer, "BEAMER", Connected: false, [Screen(BeamerOne, "BEAMER//1")]),
    ];

    /// <summary>A screen may not carry another screen's label - on another device or its own - nor any device's name, whatever the case.</summary>
    [Theory]
    [InlineData("Wand")]
    [InlineData("beamer//1")]
    [InlineData("BEAMER")]
    [InlineData("tisch-pc")]
    public void A_screen_name_another_carries_is_taken(string typed) =>
        Assert.True(DeviceNames.ScreenNameTaken(All, TableOne, typed));

    /// <summary>Its own label is not taken from it, and empty gives the default back.</summary>
    [Theory]
    [InlineData("Tisch")]
    [InlineData("")]
    [InlineData("Spieltisch")]
    public void A_screens_own_label_a_free_name_and_empty_are_not_taken(string typed) =>
        Assert.False(DeviceNames.ScreenNameTaken(All, TableOne, typed));

    /// <summary>A device may not carry another device's name or any screen's label - its own screens' included.</summary>
    [Theory]
    [InlineData("beamer")]
    [InlineData("Wand")]
    [InlineData("BEAMER//1")]
    public void A_device_name_another_carries_is_taken(string typed) =>
        Assert.True(DeviceNames.DeviceNameTaken(All, Table, typed));

    [Theory]
    [InlineData("TISCH-PC")]
    [InlineData("SPIELTISCH")]
    public void A_devices_own_name_and_a_free_one_are_not_taken(string typed) =>
        Assert.False(DeviceNames.DeviceNameTaken(All, Table, typed));

    private static ScreenView Screen(ScreenRef screen, string label) =>
        new(screen, new ScreenInfo(screen.Screen, label, null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled);
}
