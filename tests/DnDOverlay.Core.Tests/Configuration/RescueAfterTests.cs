namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// The rescue mark's grace as a device parameter (Part 6): a minute unless set, and never so short
/// that a Wi-Fi hiccup puts the mark up.
/// </summary>
public sealed class RescueAfterTests
{
    [Fact]
    public void Unset_it_is_a_minute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new DeviceSettings().RescueAfter);
    }

    [Fact]
    public void Set_it_is_what_was_set()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), new DeviceSettings(RescueAfterSeconds: 90).RescueAfter);
    }

    [Fact]
    public void It_is_never_shorter_than_ten_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new DeviceSettings(RescueAfterSeconds: 0).RescueAfter);
    }

    /// <summary>The fourth field reaches both merges and the emptiness check, like the three before it.</summary>
    [Fact]
    public void It_survives_a_merge_and_makes_a_delta_non_empty()
    {
        var delta = new DeviceSettings(RescueAfterSeconds: 120);

        Assert.False(delta.IsEmpty);
        Assert.Equal(120, DeviceSettings.Merge(new DeviceSettings(RescueAfterSeconds: 60), delta).RescueAfterSeconds);
        Assert.Equal(120, DeviceSettings.Merge(delta, new DeviceSettings(KeepAwake: true)).RescueAfterSeconds);
    }
}
