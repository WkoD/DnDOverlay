namespace DnDOverlay.Core;

/// <summary>How a saved scene goes onto a screen (Part 3).</summary>
public enum SceneLoad
{
    /// <summary>Clear the screen and lay the scene - the change of scene in play.</summary>
    Replace,

    /// <summary>Lay it on what is there - the council onto the map already lying.</summary>
    Add,
}

/// <summary>
/// The operations that put a saved scene onto a screen - one patch, worked out against the TARGET
/// (Part 3). The hub runs this under its gate; it lives here so it can be asserted without one.
/// </summary>
public static class SceneLoading
{
    /// <summary>
    /// What loading <paramref name="saved"/> onto a screen holding <paramref name="target"/> comes to.
    /// <list type="bullet">
    /// <item>Every item gets a new <see cref="ItemId"/> - the same scene loaded twice must not collide.</item>
    /// <item>The depths keep their order: from nothing when replacing, above what lies there when adding,
    /// each layer - table and fan - on its own.</item>
    /// <item>Pictures on the table keep their normalised place and are capped and held at the edge of
    /// the target, as a picture moved there would be. Parked ones go into the TARGET's fan, in their
    /// order, at its park edge and arrival size - the fan is computed from the list (Parking).</item>
    /// <item>A background that was filled or fitted is filled or fitted again on the target; one the DM
    /// had placed by hand keeps its place proportionally and is held against gaps like a picture
    /// (DM, 19.10.2026, siting question 5).</item>
    /// <item>No focus: a focus is a moment, and the old ids would point at nothing.</item>
    /// </list>
    /// The two layer switches are left as they stand - they belong to the screen's buttons.
    /// </summary>
    /// <param name="revision">Hands out the next revision; the hub's number space.</param>
    public static IReadOnlyList<PatchOp> Ops(
        SceneState saved,
        SceneState target,
        ScreenContext screen,
        SceneLoad mode,
        Func<long> revision)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(revision);

        var ops = new List<PatchOp>();
        var replacing = mode is SceneLoad.Replace;

        if (replacing && target.Items.Count > 0)
        {
            ops.Add(new ClearItems());
        }

        if (replacing && target.Background is not null && saved.Background is null)
        {
            ops.Add(new ClearBackground());
        }

        var table = replacing ? 0 : target.TopZOrder + 1;
        var fan = replacing ? 0 : target.TopParkedZOrder + 1;

        foreach (var item in saved.Items.OrderBy(item => item.ZOrder))
        {
            var depth = item.Parked ? fan++ : table++;

            var arriving = item with
            {
                ItemId = new ItemId(Guid.NewGuid()),
                ZOrder = depth,
                Revision = revision(),
            };

            if (!arriving.Parked)
            {
                arriving = Manipulation.HoldAtEdge(
                    arriving with
                    {
                        Scale = Math.Max(
                            Math.Min(arriving.Scale, Layout.WidthCap(arriving.AspectRatio, screen)),
                            Layout.Graspable(arriving.AspectRatio, screen)),
                    },
                    screen);
            }

            ops.Add(new AddItem(arriving));
        }

        if (saved.Background is { } background)
        {
            ops.Add(new SetBackground(Fitted(background, screen)));
        }

        return ops;
    }

    private static BackgroundItem Fitted(BackgroundItem background, ScreenContext screen)
    {
        if (background.Fit is { } fit)
        {
            var quarter = Manipulation.NearestQuarter(background.RotationDeg);
            var (centre, scale) = Layout.FitBackground(background.Meta.AspectRatio, fit, screen, quarter);

            return background with { CenterX = centre.X, CenterY = centre.Y, Scale = scale, RotationDeg = quarter };
        }

        return Manipulation.HoldAtEdge(
            background with { Scale = Layout.ClampScale(background.Scale, background.Meta.AspectRatio, screen) },
            screen);
    }
}
