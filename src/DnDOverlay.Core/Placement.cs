namespace DnDOverlay.Core;

/// <summary>
/// Where a new image lands when the DM did not aim at a spot.
/// <para>
/// This is computed in the HUB, not in the control, and that is a decision rather than a
/// convenience: placement reads the scene and writes it in the same breath. Were it to live in the
/// control, the paste hotkey, the inventory double tap and later a mobile device would each read
/// the same scene, work out the same place and lay two images exactly on top of each other
/// (Part 3). That the rule is now a plain count rather than a search makes the collision MORE
/// likely, not less - two controls counting the same five items both answer "the sixth place".
/// </para>
/// </summary>
public static class Placement
{
    /// <summary>Gap between slots, and the margin from the screen edge, in normalised units.</summary>
    private const double Gap = 0.02;

    /// <summary>How far each cascaded image steps, in normalised units.</summary>
    private const double CascadeStep = 0.035;

    /// <summary>
    /// The centre for a new item of the given scale and shape.
    /// <para>
    /// <b>There is no caption allowance any more</b>, and that is a decision rather than an
    /// omission (M2b). Until M1 the signature carried a <c>captionHeight</c> because the Java
    /// version drew the name BELOW the picture, which grew the row and made a captioned row
    /// overlap the one under it (commit 37e946c). Ours draws the caption INSIDE the picture, so
    /// an item never reaches past its own rectangle - the bug cannot recur, and a parameter kept
    /// "just in case" would be a second answer to a question that now has one.
    /// </para>
    /// </summary>
    public static Point NextPosition(
        SceneState scene,
        double scale,
        double aspectRatio,
        ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var (width, height) = Layout.NormalisedSize(scale, aspectRatio, screen);

        return screen.Placement switch
        {
            PlacementMode.Cascade => Cascade(scene, width, height),
            _ => Flow(scene, width, height, screen),
        };
    }

    /// <summary>
    /// The shape the grid is measured in. Cells are the same size whatever is put in them, and that
    /// size has to come from somewhere - <c>Scale</c> gives the height, and 4:3 is the shape the
    /// parameter table's numbers were reckoned against.
    /// <para>
    /// It is a REFERENCE and not a promise about the picture: a portrait sits inside its cell with
    /// air on both sides, a wide one fills it and may reach a little past it. What it buys is that
    /// the grid does not change shape with every picture.
    /// </para>
    /// </summary>
    private const double ReferenceAspectRatio = 4d / 3d;

    /// <summary>
    /// A fixed grid, filled in reading order, over and over. Six places on a 1080p table at the
    /// size a picture arrives in: the seventh picture goes back to the first place, the eighth to
    /// the second.
    /// <para>
    /// <b>Nothing is searched for and nothing is skipped</b>, and that is a correction the table
    /// forced twice (hand-run of M2b, steps 16). The ported <c>OlPane.addImage</c> looked for a free
    /// place, which sounds helpful and is not: where a picture lands then depends on where every
    /// other picture happens to lie, so the same action gives a different answer each time and the
    /// DM cannot learn it. Predictable beats clever - the cost is that a picture may land on one
    /// that was dragged onto its place, and that is the DM's own doing and undoable in one drag.
    /// </para>
    /// <para>
    /// <b>The cells are the same size for every picture</b>, which is the other half of the same
    /// correction. Deriving the grid from the picture being placed gave every shape its own grid,
    /// so nothing lined up with anything - measured at the table as "the placement looks odd". Now
    /// each picture is CENTRED in its cell, so rows and columns line up whatever shape arrives.
    /// </para>
    /// <para>
    /// The row is exactly as high as a picture. The Java version's second correction - extra room
    /// for the caption - is gone with the caption having moved inside the image.
    /// </para>
    /// </summary>
    private static Point Flow(
        SceneState scene,
        double width,
        double height,
        ScreenContext screen)
    {
        var cells = Cells(screen);

        if (cells.Count == 0)
        {
            // A screen that holds not one cell - the arrival size is larger than the table itself.
            return new Point(0.5, 0.5);
        }

        var cell = cells[scene.Items.Count % cells.Count];

        return new Point(
            Inside(cell.X + (cell.Width / 2), width),
            Inside(cell.Y + (cell.Height / 2), height));
    }

    /// <summary>
    /// The grid, in reading order. It depends on the SCREEN alone - the arrival size and the
    /// screen's own shape - so every picture sees the same one.
    /// </summary>
    private static List<Rect> Cells(ScreenContext screen)
    {
        var cells = new List<Rect>();

        var height = screen.ScaleOnLoad;

        if (height <= 0)
        {
            return cells;
        }

        // Normalised height is a fraction of the screen height and normalised width a fraction of
        // the screen WIDTH, so the reference shape travels through both aspect ratios - the same
        // trap Layout keeps warning about, and the reason 0.4 gives six cells on 16:9 and would
        // give four on 21:9.
        var width = screen.AspectRatio <= 0
            ? height * ReferenceAspectRatio
            : height * ReferenceAspectRatio / screen.AspectRatio;

        for (var y = Gap; y + height <= 1 - Gap + double.Epsilon; y += height + Gap)
        {
            for (var x = Gap; x + width <= 1 - Gap + double.Epsilon; x += width + Gap)
            {
                cells.Add(new Rect(x, y, width, height));
            }
        }

        return cells;
    }

    /// <summary>
    /// Holds a centre far enough from the edge that the picture lies wholly on the table. A picture
    /// wider than the screen has no such place and goes to the middle, where the most of it shows.
    /// </summary>
    private static double Inside(double centre, double extent) =>
        extent >= 1 ? 0.5 : Math.Clamp(centre, extent / 2, 1 - (extent / 2));

    /// <summary>Stacked from the centre with a growing offset, wrapping before it leaves the screen.</summary>
    private static Point Cascade(SceneState scene, double width, double height)
    {
        const int MaxSteps = 12;

        var step = scene.Items.Count % MaxSteps;
        var offset = step * CascadeStep;

        var x = Math.Clamp(0.5 + offset - (MaxSteps * CascadeStep / 2), width / 2, 1 - (width / 2));
        var y = Math.Clamp(0.5 + offset - (MaxSteps * CascadeStep / 2), height / 2, 1 - (height / 2));

        return new Point(x, y);
    }
}
