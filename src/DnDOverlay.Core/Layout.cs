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

        return ToRect(item.CenterX, item.CenterY, item.Scale, item.AspectRatio, screen);
    }

    /// <summary>
    /// The rectangle a centre, a scale and a shape occupy on this screen. Both layers ask it: an
    /// item through <see cref="ItemToRect"/>, the background through
    /// <see cref="BackgroundRect"/>. Since M4 that is one formula rather than two that agree
    /// (rule 9).
    /// </summary>
    private static Rect ToRect(
        double centreX, double centreY, double scale, double aspectRatio, ScreenContext screen)
    {
        var (width, height) = NormalisedSize(scale, aspectRatio, screen);

        return new Rect(centreX - (width / 2), centreY - (height / 2), width, height);
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

        // Through units of screen HEIGHT on both axes, and that detour is the whole point: the
        // rotation happens on the glass, not in the normalised system, where one unit across is not
        // one unit down. Mixing a normalised width with a normalised height in one trigonometric
        // sum gives a rectangle that is neither - on 16:9 at 46 degrees it came out a third too
        // wide and a quarter too short.
        //
        // <b>Found at the table</b> (hand-run of M3, step 18a): the clamp allows the centre out to
        // "half a hull past the edge", so an over-wide hull hands out slack that does not exist. A
        // picture at 46 degrees walked entirely off the left of the screen while the clamp believed
        // 96 DIP of it were still showing - measured at centre -0.285, every one of the four
        // corners at x below zero.
        //
        // The warning was already written down, one file over, on Manipulation.Pivot: "a rotation
        // applied to normalised offsets directly shears the picture across the table on anything
        // that is not square". The sibling function did it anyway.
        var aspect = screen.AspectRatio <= 0 ? 1 : screen.AspectRatio;

        var acrossInHeights = rect.Width * aspect;

        var width = ((acrossInHeights * cos) + (rect.Height * sin)) / aspect;
        var height = (acrossInHeights * sin) + (rect.Height * cos);

        return new Rect(item.CenterX - (width / 2), item.CenterY - (height / 2), width, height);
    }

    /// <summary>
    /// The four corners of the item as it is actually drawn, normalised, clockwise from the top
    /// left of the unturned picture.
    /// <para>
    /// The hull is the box around these; where the two are needed apart is a corner, because the
    /// box reaches into it and the picture need not.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Point> ItemToQuad(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var rect = ItemToRect(item, screen);
        var aspect = screen.AspectRatio <= 0 ? 1 : screen.AspectRatio;

        // Through screen heights on both axes, where a rotation is a rotation.
        var across = rect.Width / 2 * aspect;
        var down = rect.Height / 2;

        var radians = item.RotationDeg * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return
        [
            .. new[] { (-across, -down), (across, -down), (across, down), (-across, down) }
                .Select(corner => new Point(
                    item.CenterX + (((corner.Item1 * cos) - (corner.Item2 * sin)) / aspect),
                    item.CenterY + (corner.Item1 * sin) + (corner.Item2 * cos))),
        ];
    }

    /// <summary>
    /// How much of the picture is on the glass, in square DIP.
    /// <para>
    /// <b>This is the number the "nothing vanishes" promise is really about</b>, and the reason it
    /// exists is a hand-run: the edge clamp holds each axis on its own against the hull, which is
    /// exactly right at an edge and says nothing at a CORNER. There the two axes can be satisfied
    /// by two different corners of a turned picture - the box pokes into the visible strip above,
    /// the picture pokes into it below - and between them lies nothing at all. Measured at 37
    /// degrees: both axes reporting their full 96 DIP, and <b>zero</b> square DIP of picture on the
    /// screen (checks/M3.md, G31).
    /// </para>
    /// </summary>
    public static double VisibleAreaInDip(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var polygon = ItemToQuad(item, screen).ToList();

        // Clipped against the four edges of the screen in turn - the ordinary way to intersect a
        // convex shape with a rectangle, and a turned picture is convex.
        foreach (var (nx, ny, offset) in Edges)
        {
            if (polygon.Count == 0)
            {
                return 0;
            }

            polygon = Clip(polygon, nx, ny, offset);
        }

        if (polygon.Count < 3)
        {
            return 0;
        }

        var twice = 0d;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            twice += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(twice) / 2 * screen.WidthInDip * screen.HeightInDip;
    }

    /// <summary>The screen as four inward half-planes: left, right, top, bottom.</summary>
    private static readonly (double X, double Y, double Offset)[] Edges =
        [(1, 0, 0), (-1, 0, -1), (0, 1, 0), (0, -1, -1)];

    private static List<Point> Clip(List<Point> polygon, double nx, double ny, double offset)
    {
        var kept = new List<Point>(polygon.Count + 1);

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            var da = (nx * a.X) + (ny * a.Y) - offset;
            var db = (nx * b.X) + (ny * b.Y) - offset;

            if (da >= 0)
            {
                kept.Add(a);
            }

            if (da >= 0 != db >= 0)
            {
                var t = da / (da - db);

                kept.Add(new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)));
            }
        }

        return kept;
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

        var widthCap = WidthCap(aspectRatio, screen);

        // The LOWER bound has no business here, and that is a correction the table forced.
        //
        // ClampScale holds a scale above MinScale - "80 DIP on the shorter edge", so nothing can
        // become too small to grab. Measured in the hand-run of M2b, step 15: on an extreme shape
        // that bound does not merely bind, it EXPLODES, because it is expressed against the
        // shorter edge. A 6500x39 panorama caps to 0.0096 and came back as 0.0741 - 694 % of the
        // screen width. Its mirror image, 39x6500, came back at 1235 % of the screen height,
        // because the shorter-edge factor divides by six thousandths.
        //
        // Which demand gives way is not a close call: a picture that does not FIT is unusable for
        // everyone, while one whose short edge is under 80 DIP is merely hard to grab - and a
        // sliver is a sliver whatever we do with it. The lower bound keeps its job where it
        // belongs, at the GESTURE (M3): it stops the DM zooming a picture away. How large a
        // picture ARRIVES is a different question, and it has two answers already - the configured
        // size and the width cap.
        return Math.Min(screen.ScaleOnLoad, widthCap);
    }

    /// <summary>
    /// The largest scale at which a picture of this shape still fits the screen's width.
    /// <para>
    /// It is the cap of <see cref="ScaleOnLoad"/>, pulled out because a second caller needs the
    /// same number: <b>a picture moved or copied onto a screen with a different aspect ratio is
    /// capped again</b> (Part 3, Part 11). Arriving is arriving, whether the picture comes from a
    /// file or from the next screen along - and a panorama that wrecks the flow arithmetic does so
    /// either way.
    /// </para>
    /// <para>
    /// <b>Only the upper bound, and that is the same decision as in <see cref="ScaleOnLoad"/>:</b>
    /// the lower one explodes on extreme shapes because it is expressed against the shorter edge,
    /// and how large a picture ARRIVES is a different question from how small the DM may zoom it.
    /// </para>
    /// </summary>
    public static double WidthCap(double aspectRatio, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return aspectRatio <= 0
            ? double.PositiveInfinity
            : screen.MaxWidthOnLoad * screen.AspectRatio / aspectRatio;
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
    /// here uses - and by the same formula as <see cref="ItemToRect"/>, because since M4 the
    /// background carries a centre, a scale and an angle like any picture.
    /// <para>
    /// The screen itself is <c>(0, 0, 1, 1)</c>, so a background that fills it reaches PAST it on
    /// one axis; that overhang is the crop, and whoever draws it clips to the screen.
    /// </para>
    /// </summary>
    public static Rect BackgroundRect(BackgroundItem background, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(screen);

        return background.Meta.AspectRatio <= 0 || screen.AspectRatio <= 0

            // Nothing to fit against. Filling the screen is the answer that shows a picture rather
            // than an empty layer.
            ? new Rect(0, 0, 1, 1)
            : ToRect(background.CenterX, background.CenterY, background.Scale, background.Meta.AspectRatio, screen);
    }

    /// <summary>
    /// The centre and the scale one of the two fit buttons produces - <c>Cover</c> fills the screen
    /// and crops, <c>Contain</c> shows everything with a margin.
    /// <para>
    /// <b>They compute a state now instead of being one.</b> Until M4 the fit was a field of the
    /// background and the renderer asked it on every pass; it is now what the DM presses to put the
    /// picture back into one of the two obvious positions, and everything after that is a free
    /// place, size and angle (Part 6).
    /// </para>
    /// <para>
    /// The centre is the middle of the screen in both cases. That is not a simplification: under
    /// <c>Cover</c> the choice of section is what an offset used to express, and it is now made by
    /// moving the picture - which can do everything the offset could and more.
    /// </para>
    /// </summary>
    public static (Point Centre, double Scale) FitBackground(
        double aspectRatio, BackgroundFit fit, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var centre = new Point(0.5, 0.5);

        if (aspectRatio <= 0 || screen.AspectRatio <= 0)
        {
            return (centre, 1);
        }

        // Normalised height is a fraction of the screen height and normalised width a fraction of
        // the screen WIDTH, so the picture's shape has to travel through both aspect ratios - the
        // same trap as in ItemToRect, and the reason both live here rather than at two call sites.
        var upright = screen.AspectRatio / aspectRatio;

        return (centre, fit is BackgroundFit.Contain ? Math.Min(1, upright) : Math.Max(1, upright));
    }
}
