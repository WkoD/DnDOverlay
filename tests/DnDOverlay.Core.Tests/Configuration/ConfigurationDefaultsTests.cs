using System.Text.Json;
using DnDOverlay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// The one property the whole schema strategy rests on: a key that is <b>absent</b> reads as its
/// declared default rather than as <c>default(T)</c>.
/// <para>
/// Everything additive depends on it (rule 7). A build that adds a setting meets files written
/// before it existed on every machine that updates - and if the deserializer filled those in with
/// zero, "additive with a default" would be a sentence with nothing behind it.
/// </para>
/// <para>
/// <b>Found the hard way, and it was not theory.</b> With <c>init</c> accessors the source
/// generator emits <c>ObjectCreator = null</c> and marks every property
/// <c>IsMemberInitializer</c>: the object is built through an object initializer that assigns
/// EVERY member, so an absent key silently became zero, null or the first enum value. Measured on
/// control.json, where a missing <c>controlId</c> came back as <c>Guid.Empty</c> - which would
/// have unbound every display in the house. Plain setters put the parameterless constructor back
/// in play, and with it the initializers.
/// </para>
/// <para>
/// So this test guards a decision that looks like a style choice in the source and is not: turning
/// those setters back into <c>init</c> would reintroduce the fault without a compiler warning.
/// </para>
/// </summary>
public sealed class ConfigurationDefaultsTests
{
    [Fact]
    public void An_absent_key_reads_as_its_declared_default()
    {
        var value = JsonSerializer.Deserialize(
            """{"port":40404}""",
            ConfigurationJsonContext.Default.ControlConfiguration);

        Assert.NotNull(value);

        // What the file said.
        Assert.Equal(40404, value.Port);

        // What it did not say, and what the type promises.
        Assert.NotNull(value.KnownDevices);
        Assert.NotNull(value.KnownScreens);
        Assert.NotEqual(Guid.Empty, value.ControlId);
        Assert.Equal(ConfigurationSchema.Version, value.SchemaVersion);
        Assert.Equal(LogLevel.Information, value.LogLevel);
    }

    /// <summary>
    /// The same for the file that carries a device's identity, where the cost would be highest:
    /// a display PC whose <c>deviceId</c> read as <c>Guid.Empty</c> would collide with every other
    /// one in the house.
    /// </summary>
    [Fact]
    public void A_display_keeps_its_identity_when_a_key_is_missing()
    {
        var value = JsonSerializer.Deserialize(
            """{"host":"dm-surface"}""",
            ConfigurationJsonContext.Default.DisplayConfiguration);

        Assert.NotNull(value);
        Assert.Equal("dm-surface", value.Host);
        Assert.NotEqual(Guid.Empty, value.DeviceId);
        Assert.NotNull(value.Screens);
        Assert.NotNull(value.Device);
        Assert.Equal(LogLevel.Warning, value.Device.ForwardAtLeast);
    }
}
