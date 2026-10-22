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
        Assert.Null(options.After);
    }

    [Fact]
    public void The_process_to_wait_out_is_read()
    {
        var options = DisplayOptions.Parse(["--host", "tisch", "--after", "4711"]);

        Assert.Equal(4711, options.After);
        Assert.Equal("tisch", options.Host);
    }

    /// <summary>
    /// A restart keeps every argument and adds the wait, so the new process does not answer next to
    /// the old one's socket (Part 7).
    /// </summary>
    [Fact]
    public void A_restart_carries_the_arguments_and_waits_for_this_process()
    {
        var args = DisplayOptions.ForRestart(["--host", "tisch", "--windowed"], predecessor: 4711, deviceName: "Beamer");

        Assert.Equal(["--host", "tisch", "--windowed", "--after", "4711"], args);
        Assert.Equal(4711, DisplayOptions.Parse(args).After);
    }

    /// <summary>A second restart replaces the wait of the first rather than piling them up.</summary>
    [Fact]
    public void A_second_restart_replaces_the_first_wait()
    {
        var args = DisplayOptions.ForRestart(["--after", "4711", "--name", "Beamer"], predecessor: 815, deviceName: "Beamer");

        Assert.Equal(["--name", "Beamer", "--after", "815"], args);
    }

    /// <summary>
    /// DM decision of 22.10.2026: a device started with <c>--name</c> and renamed from the control keeps
    /// its new name across a restart from the control - the restart carries the name it has now, not the
    /// start argument the rename replaced. Without a <c>--name</c> none is added: display.json has the
    /// rename.
    /// </summary>
    [Fact]
    public void A_restart_carries_the_current_name_rather_than_the_old_start_argument()
    {
        var args = DisplayOptions.ForRestart(["--name", "Beamer", "--windowed"], predecessor: 815, deviceName: "Tisch links");

        Assert.Equal(["--name", "Tisch links", "--windowed", "--after", "815"], args);
        Assert.Equal("Tisch links", DisplayOptions.Parse(args).DeviceName);
        Assert.Null(DisplayOptions.Parse(DisplayOptions.ForRestart(["--windowed"], predecessor: 815, deviceName: "Tisch links")).DeviceName);
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
