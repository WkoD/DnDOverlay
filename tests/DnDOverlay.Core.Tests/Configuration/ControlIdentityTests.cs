using System.Text;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// Getting the identity back out of a control.json that could not be read.
/// <para>
/// It is worth its own file because of what hangs on it: a display discards the beacons of every
/// control it is not bound to, so a control that comes back with a NEW identity is a stranger to
/// its own devices - they never knock, it lists nothing, and both sides are silent while behaving
/// correctly. Keeping the identity is the difference between one grip at the control and a walk to
/// every display PC in the flat (Part 4, Part 6).
/// </para>
/// </summary>
public sealed class ControlIdentityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DnDOverlay.Tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The half that is not damaged at all: a schemaVersion newer than ours is refused although
    /// the file parses perfectly (Part 6). Reading one field out of it is not the interpretation
    /// the hard "no" forbids - an identity has no meaning to get wrong.
    /// </summary>
    [Fact]
    public void A_version_from_the_future_still_gives_up_its_identity()
    {
        var id = Guid.NewGuid();
        var path = Write($$"""
            {
              "schemaVersion": 99,
              "controlId": "{{id}}",
              "port": 47800,
              "knownDevices": [],
              "knownScreens": []
            }
            """);

        Assert.True(ControlIdentity.TryRecover(path, out var recovered));
        Assert.Equal(id, recovered);
    }

    /// <summary>
    /// And the half that IS damaged - half written, truncated, hand-mangled. A parser has nothing
    /// left to work with, but the bytes are still there.
    /// </summary>
    [Fact]
    public void A_truncated_file_still_gives_up_its_identity()
    {
        var id = Guid.NewGuid();
        var path = Write($$"""
            {
              "schemaVersion": 1,
              "controlId": "{{id}}",
              "port": 47800,
              "knownDevices": [ { "deviceId": "
            """);

        Assert.True(ControlIdentity.TryRecover(path, out var recovered));
        Assert.Equal(id, recovered);
    }

    /// <summary>
    /// The known devices carry a <c>deviceId</c>, never a <c>controlId</c>, so there is exactly one
    /// candidate in the document. Checked rather than assumed, because the whole recovery rests on
    /// it - taking a device's identifier for the control's would bind this control to a name no
    /// display has ever heard.
    /// </summary>
    [Fact]
    public void A_device_identifier_is_not_mistaken_for_the_control()
    {
        var control = Guid.NewGuid();
        var device = Guid.NewGuid();

        var path = Write($$"""
            { "schemaVersion": 1, "knownDevices": [ { "deviceId": "{{device}}", "name": "TISCH-PC" ,
              "controlId": "{{control}}"
            """);

        Assert.True(ControlIdentity.TryRecover(path, out var recovered));
        Assert.Equal(control, recovered);
        Assert.NotEqual(device, recovered);
    }

    /// <summary>
    /// A fresh identity is a legitimate outcome - and one that has to be REPORTED rather than
    /// assumed, because it is the case that costs the walk. So the answer is a plain no, never a
    /// guess.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all, and no identifier either")]
    [InlineData("{ \"schemaVersion\": 1, \"controlId\": \"00000000-0000-0000-0000-000000000000\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"controlId\": \"not-a-guid\" }")]
    public void What_cannot_be_recovered_is_answered_with_no(string content)
    {
        Assert.False(ControlIdentity.TryRecover(Write(content), out var recovered));
        Assert.Equal(Guid.Empty, recovered);
    }

    [Fact]
    public void A_file_that_is_not_there_is_answered_with_no()
    {
        Assert.False(ControlIdentity.TryRecover(Path.Combine(_directory, "gone.json"), out _));
        Assert.False(ControlIdentity.TryRecover(null, out _));
        Assert.False(ControlIdentity.TryRecover("   ", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Write(string content)
    {
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, $"control.json.broken {Guid.NewGuid():N}");

        File.WriteAllText(path, content, Encoding.UTF8);

        return path;
    }
}
