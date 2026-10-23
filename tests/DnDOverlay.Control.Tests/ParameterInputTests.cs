using System.Globalization;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>A number typed in the device band (Part 6, Part 7): the DM's format, the parameter's range, its places.</summary>
public sealed class ParameterInputTests
{
    private static readonly CultureInfo German = new("de-DE");

    private static readonly DisplayParameter Size = new("scaleOnLoad", "Arrival size", ParameterScope.Screen, ParameterKind.Number, "%", Min: 5, Max: 100, Decimals: 1);

    [Fact]
    public void A_number_in_range_is_taken_in_the_DMs_format()
    {
        Assert.Equal(12.5, ParameterInput.Value(" 12,5 ", Size, German));
        Assert.Equal(12.5, ParameterInput.Value("12.5", Size, CultureInfo.InvariantCulture));
    }

    /// <summary>The bounds belong to the range.</summary>
    [Fact]
    public void The_bounds_are_in_range()
    {
        Assert.Equal(5, ParameterInput.Value("5", Size, German));
        Assert.Equal(100, ParameterInput.Value("100", Size, German));
    }

    /// <summary>Outside the range or no number: refused, and the box shows the value in force again.</summary>
    [Theory]
    [InlineData("4,9")]
    [InlineData("100,1")]
    [InlineData("")]
    [InlineData("groß")]
    public void Out_of_range_or_no_number_is_refused(string typed) =>
        Assert.Null(ParameterInput.Value(typed, Size, German));

    /// <summary>Rounded to the places the box shows, so the device is told what the DM reads.</summary>
    [Fact]
    public void A_value_is_rounded_to_the_places_shown() =>
        Assert.Equal(33.3, ParameterInput.Value("33,333", Size, German));
}
