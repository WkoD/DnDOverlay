using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>Small factories, so a test says what it is about instead of filling fifteen fields.</summary>
internal static class Build
{
    /// <summary>A 1080p screen at 100 % scaling - the table the plan measures against.</summary>
    internal static ScreenContext Screen(
        int width = 1920,
        int height = 1080,
        double dpi = 96,
        PlacementMode placement = PlacementMode.Flow) =>
        ScreenContext.Default(new PixelSize(width, height), dpi) with { Placement = placement };

    internal static AssetMeta Meta(int width = 800, int height = 600) =>
        new(width, height, "png", Bytes: 1024, IsAnimated: false, ContentHash: new string('a', 64));

    internal static ImageItem Item(
        double centerX = 0.5,
        double centerY = 0.5,
        double scale = 0.5,
        double aspectRatio = 4d / 3d,
        double rotationDeg = 0,
        int zOrder = 0,
        ItemId? id = null) =>
        new(
            ItemId: id ?? new ItemId(Guid.NewGuid()),
            CenterX: centerX,
            CenterY: centerY,
            Scale: scale,
            AspectRatio: aspectRatio,
            RotationDeg: rotationDeg,
            ZOrder: zOrder,
            Locked: false,
            Parked: false,
            Revision: 1,
            AssetId: new AssetId(new string('b', 64)),
            Meta: Meta(),
            Name: "Grimmbart",
            ShowName: false,
            AnimationPaused: false);

    internal static SceneState SceneWith(params SceneItem[] items) =>
        SceneState.Empty with { Items = items };
}
