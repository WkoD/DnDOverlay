namespace DnDOverlay.Core;

/// <summary>
/// Where a new image lands when the DM did not aim at a spot.
/// <para>
/// This is computed in the HUB, not in the control, and that is a decision rather than a
/// convenience: placement means looking for a free spot - reading the state and writing it in
/// the same breath. Were it to live in the control, the paste hotkey, the inventory double tap
/// and later a mobile device could work out the SAME slot and lay two images exactly on top of
/// each other (Part 3).
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
    /// Side by side from the top left; a full row moves one row down, the bottom edge starts
    /// over from the top. Ported from <c>OlPane.addImage</c> with one correction: occupied slots
    /// are skipped.
    /// <para>
    /// "Skipped" is what makes the mode usable when images arrive quickly - without it the
    /// second image lands on the first whenever the first was moved out of its slot by hand.
    /// </para>
    /// <para>
    /// <b>When nothing is free the slots start over from the top</b>, and that is a correction the
    /// table forced (hand-run of M2b, step 16). The first version let the FIRST slot win, so from
    /// the moment the grid was full every further picture landed on exactly the same spot - a
    /// growing stack that looked like flow was broken. Starting over spreads the second pass across
    /// the same slots in the same order, so the pictures stay reachable one by one. Overlapping
    /// remains the lesser evil against refusing to show the image at all; WHERE it overlaps is what
    /// changed.
    /// </para>
    /// <para>
    /// The row is exactly as high as the picture. The Java version's second correction - extra
    /// room for the caption - is gone with the caption having moved inside the image.
    /// </para>
    /// </summary>
    private static Point Flow(
        SceneState scene,
        double width,
        double height,
        ScreenContext screen)
    {
        var slots = Slots(width, height);

        if (slots.Count == 0)
        {
            // The item is larger than the screen: there is no grid to speak of.
            return new Point(0.5, 0.5);
        }

        var occupied = scene.Items
            .Select(item => Layout.ItemToRect(item, screen))
            .ToList();

        foreach (var slot in slots)
        {
            if (!occupied.Any(taken => Overlaps(slot, taken)))
            {
                return Centre(slot);
            }
        }

        // Counted over the items already lying here, so a full grid of six is followed by the
        // seventh in slot one, the eighth in slot two, and so on round again.
        return Centre(slots[scene.Items.Count % slots.Count]);
    }

    /// <summary>
    /// The grid, in reading order. It depends only on the size of the picture being placed, which
    /// is why two pictures of different shapes see different grids - each is laid out against the
    /// slots IT fits in.
    /// </summary>
    private static List<Rect> Slots(double width, double height)
    {
        var slots = new List<Rect>();

        for (var y = Gap; y + height <= 1 - Gap + double.Epsilon; y += height + Gap)
        {
            for (var x = Gap; x + width <= 1 - Gap + double.Epsilon; x += width + Gap)
            {
                slots.Add(new Rect(x, y, width, height));
            }
        }

        return slots;
    }

    private static Point Centre(Rect slot) =>
        new(slot.X + (slot.Width / 2), slot.Y + (slot.Height / 2));

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

    private static bool Overlaps(Rect a, Rect b)
    {
        var overlap = a.Intersect(b);

        return overlap.Width > 0 && overlap.Height > 0;
    }
}
