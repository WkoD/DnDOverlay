namespace DnDOverlay.Core;

/// <summary>
/// The rules of a focus that the hub and the drawing share (Part 3).
/// <para>
/// <b>A focus is a way of drawing, not a transformation</b>: the items keep their place, size and
/// angle in the scene, and only the drawing puts them elsewhere - which is why lifting the focus
/// brings back exactly what was there. The table and the thumbnail both draw it from
/// <see cref="Arrange"/>, so the two cannot disagree about where a focused picture stands (rule 9).
/// </para>
/// </summary>
public static class Focus
{
    /// <summary>
    /// How many items a focus may hold. Above it it is no longer a highlight but a second scene, so
    /// the focus is refused and the tile says why (Part 3: "proposal six").
    /// </summary>
    public const int Limit = 6;

    /// <summary>
    /// How dark the rest of the screen goes - a half-transparent black that reads from two metres as
    /// "not this, now". No blur: it looks like a fault from that distance and costs the detail that
    /// maps and handouts carry (Part 3).
    /// </summary>
    public const double Dim = 0.7;

    /// <summary>Margin around the screen and between the places, in normalised units.</summary>
    private const double Margin = 0.04;

    /// <summary>
    /// The focused items as they are DRAWN, in the order of the focus - each moved into its place and
    /// sized to fit it with its own angle (Part 3). One stands in the middle, two side by side, three
    /// or four in a grid of two by two, five or six in two rows of three; a short last row is centred.
    /// <para>
    /// Only <c>CenterX</c>, <c>CenterY</c> and <c>Scale</c> differ from the scene. The angle is the
    /// DM's - taking it away in the most dramatic moment would be wrong - so the fit is measured
    /// against the turned hull, as everywhere else (Part 6). An id the scene no longer holds is left
    /// out; the reducer drops it from the list anyway.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SceneItem> Arrange(SceneState scene, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var focused = scene.FocusItems
            .Select(id => scene.Items.FirstOrDefault(item => item.ItemId == id))
            .OfType<SceneItem>()
            .ToList();

        if (focused.Count == 0)
        {
            return [];
        }

        var columns = focused.Count switch
        {
            1 => 1,
            2 or 3 or 4 => 2,
            _ => 3,
        };
        var rows = (focused.Count + columns - 1) / columns;
        var cellWidth = (1 - (Margin * (columns + 1))) / columns;
        var cellHeight = (1 - (Margin * (rows + 1))) / rows;
        var screenAspect = screen.AspectRatio > 0 ? screen.AspectRatio : 1;
        var arranged = new List<SceneItem>(focused.Count);

        for (var index = 0; index < focused.Count; index++)
        {
            var row = index / columns;
            var inRow = Math.Min(columns, focused.Count - (row * columns));
            var column = index % columns;

            // A short last row moves in by half a place per missing one, so it stands centred.
            var shift = (columns - inRow) * (cellWidth + Margin) / 2;
            var centreX = Margin + shift + (column * (cellWidth + Margin)) + (cellWidth / 2);
            var centreY = Margin + (row * (cellHeight + Margin)) + (cellHeight / 2);

            var item = focused[index];
            var (hullWidth, hullHeight) = HullAtScaleOne(item);

            // The hull at scale 1, in units of the screen height; its width becomes a fraction of
            // the screen WIDTH through the screen's aspect ratio - the dance Layout does everywhere.
            var scale = Math.Min(cellWidth * screenAspect / hullWidth, cellHeight / hullHeight);

            arranged.Add(item with { CenterX = centreX, CenterY = centreY, Scale = scale });
        }

        return arranged;
    }

    private static (double Width, double Height) HullAtScaleOne(SceneItem item)
    {
        var aspect = item.AspectRatio > 0 ? item.AspectRatio : 1;
        var radians = item.RotationDeg * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));

        return ((aspect * cos) + sin, (aspect * sin) + cos);
    }
}
