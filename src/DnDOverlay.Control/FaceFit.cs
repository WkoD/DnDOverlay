using System.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Where the face of a tile lies inside the room it is given - the rectangle every layer is laid on and
/// every grip is measured against (Part 7).
/// <para>
/// <b>The face keeps the table's shape</b> as the DM sees it, and is centred in what is offered, so an
/// open tile wider than the table has its margin on both sides. <b>In the overview the height leads</b>:
/// the tiles are rows of a wrapping arrangement, and rows of unequal height leave holes in it.
/// </para>
/// <para>
/// <b>A grip subtracts the same centring the layers were arranged with</b> - one calculation for both,
/// or a finger in the single view lands beside the picture by exactly the width of the margin (rule 9;
/// the menu that appeared far left of the pointer, hand-run of M4, third run, 24a).
/// </para>
/// </summary>
internal static class FaceFit
{
    /// <summary>How tall a face is in the overview, in DIP. The width follows the table's shape.</summary>
    internal const double Small = 150;

    /// <summary>
    /// The largest rectangle of the table's shape that fits in what is offered - the overview's fixed
    /// height, or the room an open tile is given; nothing for a table without a shape yet.
    /// </summary>
    /// <param name="shape">Width over height of the table as the DM sees it.</param>
    internal static Size Wanted(Size available, double shape, bool opened)
    {
        if (shape <= 0)
        {
            return new Size(0, 0);
        }

        var height = opened
            ? double.IsInfinity(available.Height) ? Small : available.Height
            : Small;

        var width = height * shape;

        return double.IsInfinity(available.Width) || width <= available.Width
            ? new Size(width, height)
            : new Size(available.Width, available.Width / shape);
    }

    /// <summary>Where the face's corner lies in the panel: centred, never off its top or left edge.</summary>
    internal static Point Corner(Size panel, Size face) =>
        new(Math.Max(0, (panel.Width - face.Width) / 2), Math.Max(0, (panel.Height - face.Height) / 2));

    /// <summary>A place in the panel as a place on the face.</summary>
    internal static Point OnFace(Point inPanel, Size panel, Size face)
    {
        var corner = Corner(panel, face);

        return new Point(inPanel.X - corner.X, inPanel.Y - corner.Y);
    }

    /// <summary>Whether a place on the face lies on it - the edges included.</summary>
    internal static bool Within(Point onFace, Size face) =>
        onFace.X >= 0 && onFace.Y >= 0 && onFace.X <= face.Width && onFace.Y <= face.Height;

    /// <summary>A place on the face held at its edge - a selection frame stops there, visibly (Part 3: a selection does not cross screens).</summary>
    internal static Point Clamped(Point onFace, Size face) =>
        new(Math.Clamp(onFace.X, 0, Math.Max(0, face.Width)), Math.Clamp(onFace.Y, 0, Math.Max(0, face.Height)));
}
