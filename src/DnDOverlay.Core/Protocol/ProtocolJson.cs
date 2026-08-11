using System.Text.Json;
using System.Text.Json.Serialization;

namespace DnDOverlay.Core.Protocol;

/// <summary>
/// The source-generated serialisation context for everything that goes over the wire.
/// <para>
/// This is not only an optimisation, it is the security property: there is NO type resolution at
/// run time. Both discriminated bases - <see cref="ProtocolMessage"/> and
/// <see cref="SceneItem"/> - are resolved over a fixed list of permitted types declared with
/// attributes, never over a transmitted type name (Part 4).
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProtocolMessage))]
[JsonSerializable(typeof(SceneState))]
[JsonSerializable(typeof(ScenePatch))]
public sealed partial class ProtocolJsonContext : JsonSerializerContext;

/// <summary>Serialising and parsing, so both ends do it the same way and neither invents options.</summary>
public static class ProtocolJson
{
    /// <summary>Turns a message into the bytes that go onto the socket.</summary>
    public static byte[] Serialise(ProtocolMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.ProtocolMessage);

    /// <summary>
    /// Reads a message back.
    /// <para>
    /// An unknown <c>t</c> makes this throw rather than return null, and the caller turns that
    /// into "ignore and log" (rule 7). Doing it here would mean Core deciding what a protocol
    /// violation costs, and that decision belongs where the connection is.
    /// </para>
    /// </summary>
    public static ProtocolMessage? Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, ProtocolJsonContext.Default.ProtocolMessage);
}
