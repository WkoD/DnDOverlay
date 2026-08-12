using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The delta mechanism the two-sided configuration rests on. It has to be a delta because the
/// same value has two writers, and every one of these tests is about the difference between
/// "unchanged" and "cleared" (Part 4, Part 6).
/// </summary>
public sealed class ScreenSettingsTests
{
    private static readonly ScreenContext Context = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    /// <summary>
    /// The heart of it: null means UNCHANGED. A full set in one direction would reset the other
    /// side's change without anybody ordering it.
    /// </summary>
    [Fact]
    public void What_a_delta_does_not_mention_it_does_not_touch()
    {
        var before = Context with { ParkEdge = ParkEdge.Left, ScaleOnLoad = 0.25, DefaultRotationDeg = 180 };

        var after = new ScreenSettings(ScaleOnLoad: 0.75).ApplyTo(before);

        Assert.Equal(0.75, after.ScaleOnLoad);
        Assert.Equal(ParkEdge.Left, after.ParkEdge);
        Assert.Equal(180, after.DefaultRotationDeg);
    }

    /// <summary>
    /// Size and DPI are hardware facts and are deliberately not in the settings at all - a device
    /// that could set them would be able to lie about its own monitor.
    /// </summary>
    [Fact]
    public void A_delta_cannot_touch_size_or_dpi()
    {
        var after = ScreenSettings.Of(ScreenContext.Default(new PixelSize(800, 600), 192), null).ApplyTo(Context);

        Assert.Equal(new PixelSize(1920, 1080), after.Size);
        Assert.Equal(96, after.Dpi);
    }

    [Fact]
    public void A_full_set_survives_being_laid_back_over_the_defaults()
    {
        var set = Context with
        {
            MinVisiblePixels = 120,
            MaxScale = 4,
            Placement = PlacementMode.Cascade,
            DefaultRotationDeg = 90,
            ParkEdge = ParkEdge.Top,
        };

        Assert.Equal(set, ScreenSettings.Of(set, null).ApplyTo(Context));
    }

    [Fact]
    public void A_diff_carries_only_what_moved()
    {
        var before = ScreenSettings.Of(Context, "Touch table");
        var after = ScreenSettings.Of(Context with { ParkEdge = ParkEdge.Bottom }, "Touch table");

        var delta = ScreenSettings.Diff(before, after);

        Assert.Equal(ParkEdge.Bottom, delta.ParkEdge);
        Assert.Null(delta.CustomName);
        Assert.Null(delta.ScaleOnLoad);
        Assert.Null(delta.Placement);
    }

    [Fact]
    public void A_diff_over_two_equal_sets_says_nothing()
    {
        var settings = ScreenSettings.Of(Context, "Touch table");

        Assert.True(ScreenSettings.Diff(settings, settings).IsEmpty);
    }

    /// <summary>
    /// A renamed screen is a change like any other - the name flows both ways because it may be
    /// given at the device as well (Part 6).
    /// </summary>
    [Fact]
    public void A_changed_name_is_part_of_the_delta()
    {
        var delta = ScreenSettings.Diff(
            ScreenSettings.Of(Context, null),
            ScreenSettings.Of(Context, "Touch table"));

        Assert.Equal("Touch table", delta.CustomName);
        Assert.False(delta.IsEmpty);
    }
}
