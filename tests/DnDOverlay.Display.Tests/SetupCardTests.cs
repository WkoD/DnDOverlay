namespace DnDOverlay.Display.Tests;

/// <summary>The setup card stands in the bottom right corner of the work area, above the taskbar, and stays small (DM, 24.10.2026).</summary>
public sealed class SetupCardTests
{
    /// <summary>A 1920x1080 monitor at 96 DPI with a 48 px taskbar at the bottom: the card ends 16 px above the taskbar.</summary>
    [Fact]
    public void The_card_stands_above_the_taskbar_in_the_bottom_right_corner()
    {
        var (x, y, width, height) = SetupCard.Place((0, 0, 1920, 1032), dpi: 96);

        Assert.Equal((380, 210), (width, height));
        Assert.Equal(1920 - 16, x + width);
        Assert.Equal(1032 - 16, y + height);
    }

    /// <summary>A taskbar at the top or on the left moves the work area; the card follows it, not the monitor's bounds.</summary>
    [Fact]
    public void The_card_follows_the_work_area_on_a_second_monitor()
    {
        var (x, y, width, height) = SetupCard.Place((1920, 40, 1920, 1040), dpi: 96);

        Assert.Equal(1920 + 1920 - 16, x + width);
        Assert.Equal(40 + 1040 - 16, y + height);
    }

    /// <summary>At 200 % the card is twice as large in physical pixels, and so is its gap.</summary>
    [Fact]
    public void The_card_grows_with_the_scaling()
    {
        var (x, y, width, height) = SetupCard.Place((0, 0, 3840, 2080), dpi: 192);

        Assert.Equal((760, 420), (width, height));
        Assert.Equal(3840 - 32, x + width);
        Assert.Equal(2080 - 32, y + height);
    }

    /// <summary>On a small work area the card shrinks to its share, keeping its proportions - it never takes the monitor.</summary>
    [Fact]
    public void A_small_work_area_shrinks_the_card_to_its_share()
    {
        var (_, _, width, height) = SetupCard.Place((0, 0, 1280, 720), dpi: 192);

        Assert.True(width <= 1280 * SetupCard.MaxShare + 1);
        Assert.True(height <= 720 * SetupCard.MaxShare + 1);
        Assert.Equal(SetupCard.Width / SetupCard.Height, (double)width / height, precision: 1);
    }
}
