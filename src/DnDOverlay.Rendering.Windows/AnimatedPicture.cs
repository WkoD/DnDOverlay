using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfAnimatedGif;

namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// Making a picture move, and the one place in this repository that names the animation library
/// (rule 8). Correct GIF disposal handling - which frame is composited onto which - is fiddly
/// enough to be worth a dependency; everything that depends on that choice is this file.
/// <para>
/// <b>Which</b> pictures move is not decided here. That is <c>AnimationBudget</c> in <c>Core</c>,
/// because it is a decision over the scene and therefore testable without a window - the same
/// split the decoder has. This half only knows how to start one and how to stop it.
/// </para>
/// <para>
/// A continuous animation on a software-rendered transparent overlay is the most expensive case
/// this application has (Part 6), which is why nobody may call this without having asked the
/// budget first.
/// </para>
/// </summary>
public static class AnimatedPicture
{
    /// <summary>
    /// Puts a moving picture into an <see cref="Image"/> and starts it, looping.
    /// <para>
    /// A source with a single frame comes out as an ordinary still picture, so the caller does not
    /// have to ask first - and a picture whose animation could not be read is shown rather than
    /// dropped.
    /// </para>
    /// </summary>
    public static void Run(Image target, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bytes);

        // It takes the BYTES, not a decoded picture, and that is measured rather than preferred:
        // handed the BitmapSource that PictureDecoder produces, the library reports ZERO frames.
        // That decoder reads with OnLoad and lets its stream go, which is right for a still
        // picture - but the frames of a GIF are read a second time, from the source, and there is
        // nothing left to read.
        //
        // So the animated path builds its own source and KEEPS the stream: the BitmapImage holds
        // it, and both go when the picture does. The cost is the compressed bytes staying in
        // memory beside the decoded frame, and it is paid only for pictures that actually move.
        var stream = new MemoryStream(bytes, writable: false);

        var source = new BitmapImage();

        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();

        // Frozen, like every other picture we build, and here it is not merely good manners:
        // the library keeps a static cache of animations keyed by the source object, and comparing
        // two keys READS a property off a cached source. An unfrozen one belongs to the thread that
        // built it, so the second thread to animate anything throws - measured, and it made the
        // animation tests pass or fail by the order they happened to run in.
        source.Freeze();

        ImageBehavior.SetRepeatBehavior(target, RepeatBehavior.Forever);
        ImageBehavior.SetAnimatedSource(target, source);
    }

    /// <summary>
    /// Stops the picture <b>where it is</b>, on the frame that was showing, and leaves it able to
    /// carry on from there. Returns whether there was an animation to stop.
    /// <para>
    /// This is the DM's pause switch, and it is a different thing from <see cref="Freeze"/>: a
    /// pause is meant to be undone, so jumping back to the first frame would throw away the very
    /// thing the DM stopped on. Measured at the table (hand-run of M2b, step 24): stopping snapped
    /// back to frame one a moment after it stopped.
    /// </para>
    /// <para>
    /// The animation stays attached, which is what makes carrying on possible at all - and that is
    /// why the budget's own refusals go to <see cref="Freeze"/> instead. A picture the ceiling
    /// turned away should let go of what it holds; one the DM paused is meant to be un-paused.
    /// </para>
    /// </summary>
    public static bool Hold(Image target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ImageBehavior.GetAnimationController(target) is not { } controller)
        {
            return false;
        }

        controller.Pause();

        return true;
    }

    /// <summary>
    /// Starts a held picture again, from the frame it stopped on. Returns whether there was one.
    /// </summary>
    public static bool Resume(Image target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ImageBehavior.GetAnimationController(target) is not { } controller)
        {
            return false;
        }

        controller.Play();

        return true;
    }

    /// <summary>
    /// Shows the picture without moving it - the outcome for everything the budget did not admit,
    /// and for a picture that never had an animation to begin with. It stands on its first frame,
    /// which is a still picture and not a missing one.
    /// <para>
    /// A picture the DM paused goes to <see cref="Hold"/> instead: this one gives the frames back
    /// and therefore cannot say where it was.
    /// </para>
    /// </summary>
    public static void Freeze(Image target, BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        // The clock is stopped FIRST and explicitly. Clearing the animated source alone does not do
        // it - measured: afterwards the controller is still there and still reports its frames, so
        // a picture that may not move would have gone on paying for a timer nobody can see, which
        // is the exact cost this whole ceiling exists to avoid (Part 6).
        ImageBehavior.GetAnimationController(target)?.Pause();
        ImageBehavior.SetAnimatedSource(target, null);

        target.Source = source;
    }
}
