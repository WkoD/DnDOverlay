using System.Reflection;
using System.Text.Json.Serialization;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// The shared description of the display parameters (Part 6): complete against the types that hold
/// them, each one setting only itself, and "follows the default" meaning what the files mean by it.
/// </summary>
public sealed class DisplayParametersTests
{
    private static readonly ScreenContext Table = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    /// <summary>
    /// A new parameter appears in both surfaces of necessity (Part 6) - here, by failing this test
    /// until it is described. The custom name is not a parameter; it has its own row.
    /// </summary>
    [Fact]
    public void Every_setting_is_described_once_and_in_its_scope()
    {
        static IEnumerable<string> Settable(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .Select(property => property.Name);

        Assert.Equal(
            Settable(typeof(ScreenSettings)).Where(name => name != nameof(ScreenSettings.CustomName)).Order(),
            DisplayParameters.For(ParameterScope.Screen).Select(parameter => parameter.Key).Order());
        Assert.Equal(
            Settable(typeof(DeviceSettings)).Order(),
            DisplayParameters.For(ParameterScope.Device).Select(parameter => parameter.Key).Order());
        Assert.Equal(DisplayParameters.All.Count, DisplayParameters.All.Select(parameter => parameter.Key).Distinct().Count());
    }

    /// <summary>Every screen parameter reads at its default on a fresh screen, and setting its own value changes nothing.</summary>
    [Fact]
    public void A_fresh_screen_follows_every_default()
    {
        foreach (var parameter in DisplayParameters.For(ParameterScope.Screen))
        {
            Assert.True(DisplayParameters.FollowsDefault(parameter, Table), parameter.Key);
            Assert.Equal(Table, DisplayParameters.Set(parameter, Table, DisplayParameters.Value(parameter, Table)).ApplyTo(Table));
        }
    }

    /// <summary>A changed value is the only thing its delta says; the screen no longer follows the default there, and a reset brings it back.</summary>
    [Fact]
    public void Setting_one_parameter_touches_only_that_one_and_reset_returns()
    {
        var size = DisplayParameters.All.Single(parameter => parameter.Key == nameof(ScreenSettings.ScaleOnLoad));
        var delta = DisplayParameters.Set(size, Table, 0.3);
        var changed = delta.ApplyTo(Table);

        Assert.Equal(new ScreenSettings(ScaleOnLoad: 0.3), delta);
        Assert.False(DisplayParameters.FollowsDefault(size, changed));
        Assert.Equal(new ScreenSettings(ScaleOnLoad: 0.3), ScreenSettings.Opinion(changed, null));

        var back = DisplayParameters.Reset(size, changed).ApplyTo(changed);

        Assert.True(DisplayParameters.FollowsDefault(size, back));
        Assert.True(ScreenSettings.Opinion(back, null).IsEmpty);
    }

    /// <summary>The smallest picture is kept as a fraction of the height and shown in DIP, as Part 6 names it: 80 DIP.</summary>
    [Fact]
    public void The_smallest_picture_is_shown_in_dip()
    {
        var smallest = DisplayParameters.All.Single(parameter => parameter.Key == nameof(ScreenSettings.MinScale));

        Assert.Equal(80d, DisplayParameters.Value(smallest, Table));

        var changed = DisplayParameters.Set(smallest, Table, 120d).ApplyTo(Table);

        Assert.Equal(120d / 1080, changed.MinScale, 6);
        Assert.Equal(120d, DisplayParameters.Value(smallest, changed));
    }

    /// <summary>"Automatic" for the diagnostic bar cannot travel in a delta - choosing it says nothing, and it is the one not offered for reset.</summary>
    [Fact]
    public void The_automatic_diagnostic_bar_is_no_change()
    {
        var bar = DisplayParameters.All.Single(parameter => parameter.Key == nameof(ScreenSettings.DiagnosticEdge));

        Assert.Equal(DisplayParameters.Automatic, DisplayParameters.Value(bar, Table));
        Assert.True(DisplayParameters.Set(bar, Table, DisplayParameters.Automatic).IsEmpty);
        Assert.False(DisplayParameters.CanReset(bar));
    }

    /// <summary>A device that reported nothing follows every default; a set value does not, and a reset sends the default.</summary>
    [Fact]
    public void Device_parameters_read_against_the_defaults()
    {
        var awake = DisplayParameters.All.Single(parameter => parameter.Key == nameof(DeviceSettings.KeepAwake));
        var grace = DisplayParameters.All.Single(parameter => parameter.Key == nameof(DeviceSettings.RescueAfterSeconds));

        Assert.All(DisplayParameters.For(ParameterScope.Device), parameter => Assert.True(DisplayParameters.FollowsDefault(parameter, new DeviceSettings())));
        Assert.False(DisplayParameters.FollowsDefault(awake, new DeviceSettings(KeepAwake: false)));
        Assert.Equal(new DeviceSettings(RescueAfterSeconds: 120), DisplayParameters.Set(grace, 120d));
        Assert.Equal(new DeviceSettings(KeepAwake: true), DisplayParameters.Reset(awake));
    }
}
