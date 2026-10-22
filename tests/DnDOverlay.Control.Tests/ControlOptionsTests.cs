namespace DnDOverlay.Control.Tests;

/// <summary>What the command line can say: <c>--data</c>, and nothing that becomes a stored value (Part 7, Part 9).</summary>
public sealed class ControlOptionsTests
{
    [Fact]
    public void Without_arguments_the_installed_location_is_used() =>
        Assert.Null(ControlOptions.Parse([]).DataRoot);

    [Fact]
    public void Data_takes_the_next_argument_as_the_root() =>
        Assert.Equal(@"D:\dev\data", ControlOptions.Parse(["--data", @"D:\dev\data"]).DataRoot);

    /// <summary>One compound configuration hands the same arguments to both processes (Part 2): the display's switch passes.</summary>
    [Fact]
    public void The_displays_windowed_switch_is_accepted_without_effect() =>
        Assert.Equal("root", ControlOptions.Parse(["--windowed", "--data", "root"]).DataRoot);

    /// <summary>A <c>--data</c> at the very end names nothing, and nothing is taken from it.</summary>
    [Fact]
    public void Data_without_a_value_is_ignored() =>
        Assert.Null(ControlOptions.Parse(["--data"]).DataRoot);

    [Fact]
    public void The_last_data_wins() =>
        Assert.Equal("second", ControlOptions.Parse(["--data", "first", "--data", "second"]).DataRoot);
}
