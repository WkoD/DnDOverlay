using System.Text.Json;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// Blackout was struck on 19.10.2026 - Inactive does what was wanted of it. Until then the control's
/// scaffold offered every state, so a <c>control.json</c> in use can still say <c>"Blackout"</c>, and
/// an older control can still send it. Either way it has to read, and read as Inactive: a file that
/// does not read is set aside as damaged, paired devices and screen wishes with it.
/// </summary>
public sealed class RetiredBlackoutTests
{
    private static readonly KnownScreen Screen = new(
        Guid.NewGuid(),
        @"\\?\DISPLAY#TEST",
        "TISCH-PC//DISPLAY1",
        ScreenState.Enabled,
        new PixelSize(1920, 1080),
        96,
        new ScreenSettings());

    [Fact]
    public void A_control_json_that_still_says_Blackout_reads_as_inactive()
    {
        var json = JsonSerializer.Serialize(
            new ControlConfiguration { KnownScreens = [Screen] },
            ConfigurationJsonContext.Default.ControlConfiguration);

        Assert.Contains("\"state\": \"Enabled\"", json, StringComparison.Ordinal);

        var old = json.Replace("\"state\": \"Enabled\"", "\"state\": \"Blackout\"", StringComparison.Ordinal);
        var read = JsonSerializer.Deserialize(old, ConfigurationJsonContext.Default.ControlConfiguration);

        Assert.Equal(ScreenState.Inactive, Assert.Single(read!.KnownScreens).State);
    }

    [Fact]
    public void An_older_control_sending_Blackout_reaches_a_newer_display_as_inactive()
    {
        var message = new ConfigUpdateMessage(new ConfigUpdate(
            [new ScreenConfigUpdate(new ScreenId("S"), Command: new ScreenCommand(ScreenState.Enabled))]));

        var wire = System.Text.Encoding.UTF8.GetString(ProtocolJson.Serialise(message));

        Assert.Contains("\"Enabled\"", wire, StringComparison.Ordinal);

        var old = wire.Replace("\"Enabled\"", "\"Blackout\"", StringComparison.Ordinal);
        var parsed = Assert.IsType<ConfigUpdateMessage>(ProtocolJson.Parse(System.Text.Encoding.UTF8.GetBytes(old)));

        Assert.Equal(ScreenState.Inactive, Assert.Single(parsed.Update.Screens).Command!.State);
    }

    /// <summary>Its old number reads the same way, and every remaining state survives a round trip by name.</summary>
    [Fact]
    public void The_retired_number_reads_as_inactive_and_the_rest_by_name()
    {
        Assert.Equal(ScreenState.Inactive, JsonSerializer.Deserialize("3", ConfigurationJsonContext.Default.ScreenState));

        foreach (var state in Enum.GetValues<ScreenState>())
        {
            var json = JsonSerializer.Serialize(state, ConfigurationJsonContext.Default.ScreenState);

            Assert.Equal($"\"{state}\"", json);
            Assert.Equal(state, JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.ScreenState));
        }
    }

    [Fact]
    public void A_state_nobody_ever_had_is_still_refused() =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("\"Sideways\"", ConfigurationJsonContext.Default.ScreenState));
}
