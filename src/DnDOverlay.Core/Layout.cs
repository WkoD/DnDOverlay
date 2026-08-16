namespace DnDOverlay.Core;

/// <summary>
/// The one place a scene turns into rectangles. Table, thumbnail and every later preview use
/// this and nothing else - "one computation, three renderings" is what makes the thumbnail
/// trustworthy, and the architecture test asserts that <see cref="ItemToRect"/> exists exactly
/// once (Part 1, rule 9; Part 11).
/// <para>
/// Everything here is a pure function over a <see cref="ScreenContext"/> that is handed in. The
/// display parameters therefore actually take effect without the reducer ever reaching for
/// configuration (Part 3).
/// </para>
/// </summary>
public static class Layout
{
    /// <summary>
    /// Where an item lies, in normalised screen coordinates - 0…1 on each axis.
    /// <para>
    /// <c>Scale</c> means the HEIGHT as a fraction of the screen height, so the width has to
    /// travel through both aspect ratios: the item's own and the screen's. Normalised X is a
    /// fraction of the screen WIDTH, and forgetting that is the mistake that makes everything
    /// look almost right on 16:9 and plainly wrong on 21:9.
    /// </para>
    /// <para>
    /// This is the UNROTATED placement rectangle. The edge clamp needs the rotated hull instead,
    /// because at 45° the axis-parallel extent is a quite different thing -
    /// <see cref="ItemToHullRect"/> derives it from this one rather than computing a second time.
    /// </para>
    /// </summary>
    public static Rect ItemToRect(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var (width, height) = NormalisedSize(item.Scale, item.AspectRatio, screen);

        return new Rect(item.CenterX - (width / 2), item.CenterY - (height / 2), width, height);
    }

    /// <summary>
    /// How large an item of this scale and shape renders, in normalised screen coordinates.
    /// Placement needs the same number as <see cref="ItemToRect"/>, so it comes from here rather
    /// than from a second copy of the two-aspect-ratio dance.
    /// </summary>
    internal static (double Width, double Height) NormalisedSize(
        double scale,
        double aspectRatio,
        ScreenContext screen)
    {
        var width = screen.AspectRatio <= 0
            ? scale * aspectRatio
            : scale * aspectRatio / screen.AspectRatio;

        return (width, scale);
    }

    /// <summary>
    /// The axis-parallel hull of the rotated item, around the same centre. This is what the edge
    /// clamp measures against (Part 6).
    /// </summary>
    public static Rect ItemToHullRect(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);

        var rect = ItemToRect(item, screen);

        if (item.RotationDeg % 360 == 0)
        {
            return rect;
        }

        var radians = item.RotationDeg * Math.PI / 180d;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));

        var width = (rect.Width * cos) + (rect.Height * sin);
        var height = (rect.Width * sin) + (rect.Height * cos);

        return new Rect(item.CenterX - (width / 2), item.CenterY - (height / 2), width, height);
    }

    /// <summary>
    /// The scale a freshly inserted image gets, capped on BOTH axes.
    /// <para>
    /// A 5000×500 panorama at <c>ScaleOnLoad</c> 0.5 would be 540 px tall and 5400 px wide -
    /// three times the width of a 1080p table, of which a middle slice would be visible, and in
    /// flow mode such an item wrecks the slot arithmetic for everything after it. Because
    /// <c>Scale</c> means height while <c>MaxWidthOnLoad</c> means width, the SCREEN's aspect
    /// ratio has to enter the computation - without it the cap bites 1.78 times too hard on
    /// 16:9 (Part 3).
    /// </para>
    /// </summary>
    public static double ScaleOnLoad(double aspectRatio, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (aspectRatio <= 0)
        {
            return screen.ScaleOnLoad;
        }

        var widthCap = screen.MaxWidthOnLoad * screen.AspectRatio / aspectRatio;

        return ClampScale(Math.Min(screen.ScaleOnLoad, widthCap), aspectRatio, screen);
    }

    /// <summary>
    /// Holds a scale between the two bounds of the screen.
    /// <para>
    /// The lower bound is not a plain number: Part 6 phrases it as "80 DIP on the shorter edge",
    /// so what <see cref="ScreenContext.MinScale"/> carries is the smallest rendered SHORTER
    /// edge, and which edge that is depends on the item. An image that gets too small to hit is
    /// irretrievably lost for the players without being gone.
    /// </para>
    /// </summary>
    public static double ClampScale(double scale, double aspectRatio, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var shorterEdgeFactor = aspectRatio <= 0 ? 1 : Math.Min(1, aspectRatio);
        var minimum = shorterEdgeFactor <= 0 ? screen.MinScale : screen.MinScale / shorterEdgeFactor;

        return Math.Clamp(scale, minimum, Math.Max(minimum, screen.MaxScale));
    }

    /// <summary>
    /// Where the background picture lies, in the same normalised screen coordinates everything else
    /// here uses. The screen itself is <c>(0, 0, 1, 1)</c>, so under
    /// <see cref="BackgroundFit.Cover"/> the rectangle reaches PAST it on one axis - that overhang
    /// is the crop, and whoever draws it clips to the screen.
    /// <para>
    /// Two values and no free scaling, deliberately: <c>Cover</c> fills and crops, <c>Contain</c>
    /// shows everything with a margin, and a panorama is unusable under <c>Cover</c> without it.
    /// A freely scalable background would be a gesture this layer deliberately does not have
    /// (Part 6).
    /// </para>
    /// </summary>
    /// <param name="offsetX">
    /// Which part of the crop is seen: <c>0</c> is centred, <c>-1</c> the left edge, <c>+1</c> the
    /// right. <b>It moves only inside the crop</b> and can never run past the edge - the value is
    /// clamped, and where there is no overhang it has no effect at all. Under <c>Contain</c> that
    /// means it does nothing, which is the honest outcome rather than a special case: the picture
    /// is entirely visible, so there is nothing to choose between (Part 6, Part 11).
    /// </param>
    public static Rect BackgroundRect(
        double aspectRatio, BackgroundFit fit, double offsetX, double offsetY, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (aspectRatio <= 0 || screen.AspectRatio <= 0)
        {
            // Nothing to fit against. Filling the screen is the answer that shows a picture rather
            // than an empty layer.
            return new Rect(0, 0, 1, 1);
        }

        // Normalised height is a fraction of the screen height and normalised width a fraction of
        // the screen WIDTH, so the picture's shape has to travel through both aspect ratios - the
        // same trap as in ItemToRect, and the reason both live here rather than at two call sites.
        var relative = aspectRatio / screen.AspectRatio;

        // Wider than the screen, in normalised terms, iff relative > 1.
        var (width, height) = fit switch
        {
            BackgroundFit.Contain when relative >= 1 => (1d, 1d / relative),
            BackgroundFit.Contain => (relative, 1d),
            _ when relative >= 1 => (relative, 1d),
            _ => (1d, 1d / relative),
        };

        return new Rect(Edge(width, offsetX), Edge(height, offsetY), width, height);
    }

    /// <summary>
    /// Where one edge starts: centred when the offset is zero, and at most as far as the overhang
    /// allows. <c>Contain</c> needs no case of its own here - it simply has no overhang.
    /// </summary>
    private static double Edge(double extent, double offset) =>
        -(Math.Max(0, extent - 1) / 2) * (1 + Math.Clamp(offset, -1, 1));
}
