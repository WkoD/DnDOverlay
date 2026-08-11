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
    /// </summary>
    /// <param name="captionHeight">
    /// Extra room below each item, in normalised units, for the name caption. Flow has to know
    /// it, because the row height is what it wrecks - that was the bug in the Java version
    /// (commit 37e946c), where a captioned row overlapped the one below it.
    /// <para>
    /// It is handed in rather than read from <see cref="ScreenContext"/>: the text size is a
    /// display parameter in Part 6 that the model in Part 3 does not carry, and adding a field
    /// to <see cref="ScreenContext"/> is a structural decision that belongs to the milestone
    /// which starts drawing captions (M2), not to this one. Until then callers pass zero.
    /// </para>
    /// </param>
    public static Point NextPosition(
        SceneState scene,
        double scale,
        double aspectRatio,
        ScreenContext screen,
        double captionHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var (width, height) = Layout.NormalisedSize(scale, aspectRatio, screen);

        return screen.Placement switch
        {
            PlacementMode.Cascade => Cascade(scene, width, height),
            _ => Flow(scene, width, height, captionHeight, screen),
        };
    }

    /// <summary>
    /// Side by side from the top left; a full row moves one row down, the bottom edge starts
    /// over from the top. Ported from <c>OlPane.addImage</c> with two corrections: the row
    /// height accounts for the caption, and occupied slots are skipped.
    /// <para>
    /// "Skipped" is what makes the mode usable when images arrive quickly - without it the
    /// second image lands on the first whenever the first was moved out of its slot by hand.
    /// If nothing free is left, the first slot wins: overlapping is the lesser evil against
    /// refusing to show the image at all.
    /// </para>
    /// </summary>
    private static Point Flow(
        SceneState scene,
        double width,
        double height,
        double captionHeight,
        ScreenContext screen)
    {
        var occupied = scene.Items
            .Select(item => Layout.ItemToRect(item, screen))
            .ToList();

        // The caption sits BELOW the image, so it grows the row without moving the picture.
        // Centring the item in image-plus-caption would push every captioned image half a
        // caption downwards - a shift nobody asked for and nobody would find again.
        var rowHeight = height + captionHeight;
        var first = (Point?)null;

        for (var y = Gap; y + rowHeight <= 1 - Gap + double.Epsilon; y += rowHeight + Gap)
        {
            for (var x = Gap; x + width <= 1 - Gap + double.Epsilon; x += width + Gap)
            {
                var candidate = new Point(x + (width / 2), y + (height / 2));

                first ??= candidate;

                var rect = new Rect(x, y, width, rowHeight);

                if (!occupied.Any(taken => Overlaps(rect, taken)))
                {
                    return candidate;
                }
            }
        }

        // Nothing free, or the item is larger than the screen: back to the beginning.
        return first ?? new Point(0.5, 0.5);
    }

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
