using System.Globalization;

namespace DnDOverlay.Core;

/// <summary>
/// A screen within its device. This is the Windows device instance path, and it is unique per
/// machine and no further: EDID identifier, bus instance and output UID can be identical on two
/// cloned display PCs with the same monitor on the same port - and cloning a disk is the usual
/// way a second display PC comes into being (Part 3, Part 4).
/// <para>
/// It is therefore never an address on its own. <see cref="ScreenRef"/> is, always.
/// </para>
/// <para>
/// The value is deliberately an opaque string rather than a structured path: what goes in is the
/// business of whoever enumerates the screens, which is the APPLICATION. Core only passes it on
/// and compares it, so a different system could feed it a connector name plus EDID without the
/// reducer, the protocol or control.json learning anything about it.
/// </para>
/// </summary>
public readonly record struct ScreenId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>A display device. Created once on first start and kept in display.json.</summary>
public readonly record struct DeviceId(Guid Value)
{
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>One image lying on one screen. Handed out by the hub alone (Part 1, rule 2).</summary>
public readonly record struct ItemId(Guid Value)
{
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// An image, addressed by the SHA-256 of its SOURCE bytes - what came in, not what goes out
/// (Part 5). Identity and integrity are two questions: the delivered bytes carry their own hash
/// in <see cref="AssetMeta.ContentHash"/>.
/// </summary>
public readonly record struct AssetId(string Value)
{
    /// <summary>A SHA-256 rendered as lower-case hex.</summary>
    public const int Length = 64;

    /// <summary>
    /// Whether the value can safely become part of a file name or a URL path. The check lives
    /// here so it exists once, but the place that MUST call it is the hub's asset endpoint: it
    /// takes the identifier from a paired device, and without this
    /// <c>GET /assets/..%5C..%5Cwindows%5C…</c> reads arbitrary files off the DM's machine
    /// (Part 4, Part 5).
    /// </summary>
    public bool IsWellFormed =>
        Value is { Length: Length } value && value.All(IsLowerHexDigit);

    private static bool IsLowerHexDigit(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f');

    public override string ToString() => Value;
}

/// <summary>
/// The address of a screen across all devices. Everything is addressed with this: the scene
/// store, every <see cref="ScreenOp"/>, control.json, the tile order, the view rotation.
/// <para>
/// Two cloned display PCs can report literally the same <see cref="ScreenId"/>; the
/// <see cref="DeviceId"/> in front of it is a GUID each device makes up on its own first start,
/// so a collision is ruled out by construction rather than made unlikely (Part 3).
/// </para>
/// </summary>
public readonly record struct ScreenRef(DeviceId Device, ScreenId Screen)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Device}/{Screen}");
}
