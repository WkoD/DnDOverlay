using System.Text.Json;
using System.Text.Json.Serialization;

namespace DnDOverlay.Core.Protocol;

/// <summary>
/// What a control shouts into the network so a display can find it without anybody typing an
/// address.
/// <para>
/// <b>Exactly four things, and no more.</b> It is unauthenticated and readable by everyone on the
/// network - device lists, versions of paired machines, screen names and anything else belong
/// behind the pairing (Part 4). The identifier is in here because it has to be: it is what binds
/// a display to ITS control, and what lets it ignore a second control in the same network.
/// </para>
/// <para>
/// It is deliberately not a <see cref="ProtocolMessage"/>. Nothing here travels over the
/// WebSocket, and putting it in that union would offer it as something one could send over a
/// connection - which is exactly what it is for not having yet.
/// </para>
/// </summary>
public sealed record Beacon(Guid ControlId, string Name, int Port, int ProtocolVersion);

/// <summary>
/// Its own source-generated context, for the same reason as the protocol's: no type resolution at
/// run time. This one reads datagrams from anybody on the network, so it is the last place that
/// should be lenient (Part 4).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Beacon))]
public sealed partial class DiscoveryJsonContext : JsonSerializerContext;

/// <summary>Turning a beacon into a datagram and back, so both ends do it the same way.</summary>
public static class DiscoveryJson
{
    /// <summary>
    /// A ceiling for an incoming datagram. A beacon is around a hundred bytes; anything beyond
    /// this is not one, and reading it would be reading whatever somebody felt like sending.
    /// </summary>
    public const int MaxBytes = 1024;

    public static byte[] Serialise(Beacon beacon) =>
        JsonSerializer.SerializeToUtf8Bytes(beacon, DiscoveryJsonContext.Default.Beacon);

    /// <summary>
    /// Reads a datagram, and answers with <see langword="null"/> for anything that is not one of
    /// ours - a stray broadcast from a different program, a truncated packet, deliberate noise.
    /// <b>Never throws</b>: this is fed by the open network, and an exception per stray packet
    /// would make the listener a denial-of-service target against itself.
    /// </summary>
    public static Beacon? Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length is 0 or > MaxBytes)
        {
            return null;
        }

        try
        {
            var beacon = JsonSerializer.Deserialize(utf8Json, DiscoveryJsonContext.Default.Beacon);

            return beacon is null || beacon.Port is <= 0 or > 65535 ? null : beacon;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
