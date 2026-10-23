using System.Globalization;

namespace DnDOverlay.Control.Tests;

/// <summary>What the typed fields of the window Settings accept (Part 7) - a refused value never reaches control.json.</summary>
public sealed class SettingsInputTests
{
    private static readonly CultureInfo German = new("de-DE");

    [Theory]
    [InlineData("1024", 1024)]
    [InlineData(" 50000 ", 50000)]
    [InlineData("65535", 65535)]
    public void A_port_in_range_is_taken(string typed, int port) =>
        Assert.Equal(port, SettingsInput.Port(typed));

    /// <summary>Below 1024, above 65535, signed, with separators or not a number: refused.</summary>
    [Theory]
    [InlineData("1023")]
    [InlineData("65536")]
    [InlineData("-5000")]
    [InlineData("+5000")]
    [InlineData("50.000")]
    [InlineData("")]
    [InlineData("Port")]
    public void A_port_out_of_range_or_not_a_number_is_refused(string typed) =>
        Assert.Null(SettingsInput.Port(typed));

    /// <summary>A pattern without {n} would give every screenshot the same name: refused; one with it is kept trimmed.</summary>
    [Fact]
    public void A_name_pattern_needs_the_number()
    {
        Assert.Equal("Bild {n}", SettingsInput.Pattern("  Bild {n} "));
        Assert.Null(SettingsInput.Pattern("Bild"));
        Assert.Null(SettingsInput.Pattern("Bild {N}"));
    }

    /// <summary>The free-space threshold is typed in GB in the DM's number format - a German comma is a decimal comma.</summary>
    [Fact]
    public void Free_space_is_read_in_the_DMs_number_format()
    {
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), SettingsInput.FreeSpaceBytes("1,5", German));
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), SettingsInput.FreeSpaceBytes("1.5", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("")]
    [InlineData("viel")]
    public void Free_space_of_nothing_or_less_is_refused(string typed) =>
        Assert.Null(SettingsInput.FreeSpaceBytes(typed, German));

    [Fact]
    public void The_picture_count_is_a_whole_number_above_nothing()
    {
        Assert.Equal(2000, SettingsInput.PictureCount(" 2000 ", German));
        Assert.Null(SettingsInput.PictureCount("0", German));
        Assert.Null(SettingsInput.PictureCount("2.000", German));
        Assert.Null(SettingsInput.PictureCount("12,5", German));
    }

    /// <summary>The same folder, in another case or with a trailing backslash, is no move.</summary>
    [Fact]
    public void The_same_place_in_another_spelling_is_no_move()
    {
        Assert.True(SettingsInput.SamePlace(@"D:\Kampagnen", @"d:\kampagnen\"));
        Assert.False(SettingsInput.SamePlace(@"D:\Kampagnen", @"D:\Kampagnen2"));
    }

    /// <summary>A folder inside the other, either way round, is refused; a neighbour whose name only begins alike is not.</summary>
    [Fact]
    public void A_place_inside_the_other_is_refused_either_way_round()
    {
        Assert.True(SettingsInput.Nested(@"D:\Kampagnen", @"D:\Kampagnen\Neu"));
        Assert.True(SettingsInput.Nested(@"D:\Kampagnen\Alt", @"D:\KAMPAGNEN"));
        Assert.True(SettingsInput.Nested(@"D:\", @"D:\Kampagnen"));
        Assert.False(SettingsInput.Nested(@"D:\Kampagnen", @"D:\Kampagnen2"));
        Assert.False(SettingsInput.Nested(@"D:\Kampagnen", @"E:\Kampagnen"));
    }
}
