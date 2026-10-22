using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Display.Tests;

/// <summary>The display's start arguments (Part 9): what is said wins for this session, what is not stays unset.</summary>
public sealed class DisplayOptionsTests
{
    [Fact]
    public void Nothing_said_leaves_everything_to_discovery_and_the_file()
    {
        var options = DisplayOptions.Parse([]);

        Assert.Null(options.Host);
        Assert.Equal(Protocol.DefaultPort, options.Port);
        Assert.Null(options.DeviceName);
        Assert.False(options.Windowed);
        Assert.Null(options.DataRoot);
    }

    [Fact]
    public void Every_switch_is_read()
    {
        var options = DisplayOptions.Parse(["--host", "tisch", "--port", "48000", "--name", "Beamer", "--windowed", "--data", @"D:\dnd"]);

        Assert.Equal(new DisplayOptions("tisch", 48000, "Beamer", Windowed: true, @"D:\dnd"), options);
    }

    /// <summary>A switch missing its value, or a port that is no number, is passed over rather than taken as something else.</summary>
    [Fact]
    public void A_switch_without_its_value_is_passed_over()
    {
        var options = DisplayOptions.Parse(["--port", "viele", "--name"]);

        Assert.Equal(Protocol.DefaultPort, options.Port);
        Assert.Null(options.DeviceName);
    }
}
