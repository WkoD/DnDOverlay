namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// How many pixels of a picture are actually decoded, and when that number goes up.
/// <para>
/// <b>A decoded bitmap is uncompressed memory</b> - width × height × 4 bytes - and the file size
/// says nothing about it: a 6000×4000 photo is 96 MB in memory while its JPEG weighs 5 (Part 6). A
/// table holding twenty of them at source resolution is the whole memory budget spent on pixels
/// nobody can see, because the screen has fewer of them than the picture does.
/// </para>
/// <para>
/// So the base step is the screen's longer edge, the step above it is twice that, and the source is
/// the cap. Confirmed by spike B and binding since (Part 10): the sharpening costs about 21 ms in
/// the median, which is a frame - noticeable if it happened per gesture, invisible when it happens
/// once on crossing a step.
/// </para>
/// <para>
/// <b>Zooming out never decodes back.</b> The memory is already spent, giving it back would cost a
/// second decode, and the picture would go soft on a movement that asked for nothing.
/// </para>
/// </summary>
public static class DecodeSteps
{
    /// <summary>
    /// The width to decode a picture at when it first arrives on a screen with this longer edge, in
    /// physical pixels.
    /// <para>
    /// It is the WIDTH because that is what WIC is told; the longer edge is what the step is about,
    /// so a portrait picture gets a smaller width than a landscape one on the same screen. A picture
    /// that is already smaller than its step is decoded as it is - asking for more pixels than the
    /// source has does not add detail, it only scales up and spends the memory twice.
    /// </para>
    /// </summary>
    public static int Base(int screenLongerEdge, int sourceWidth, int sourceHeight)
    {
        if (screenLongerEdge <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            // Nothing to reckon with - decode as it comes. A wrong step here would be a picture
            // scaled to a guess, which is worse than a picture at its own size.
            return 0;
        }

        var longer = Math.Max(sourceWidth, sourceHeight);

        if (longer <= screenLongerEdge)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Round(sourceWidth * (double)screenLongerEdge / longer));
    }

    /// <summary>
    /// The next step up, or <see langword="null"/> when there is nothing to gain.
    /// <para>
    /// One step per crossing rather than straight to what is needed: a pinch that carries on
    /// zooming crosses the next step on the next frame and asks again, and each of those decodes is
    /// half the size of the one it would have done in a single jump.
    /// </para>
    /// </summary>
    /// <param name="decodedWidth">What the bitmap on the screen has now.</param>
    /// <param name="neededWidth">What the item is being drawn at, in physical pixels.</param>
    /// <param name="sourceWidth">The cap. There are no pixels beyond it to fetch.</param>
    public static int? Next(int decodedWidth, int neededWidth, int sourceWidth)
    {
        if (decodedWidth <= 0 || decodedWidth >= sourceWidth || neededWidth <= decodedWidth)
        {
            // Already at the source, or nothing is being asked for. Zooming out lands here too, and
            // that is the rule rather than an accident of the arithmetic.
            return null;
        }

        return Math.Min(decodedWidth * 2, sourceWidth);
    }
}
