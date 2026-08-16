using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DnDOverlay.Imaging;
using DnDOverlay.TestData;
using WpfAnimatedGif;

namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The other half of the animation: <c>AnimationBudget</c> in <c>Core</c> says WHICH pictures move,
/// this says whether one actually does.
/// <para>
/// Checked against the real animated GIF the codec produces, not against a hand-made one - the
/// question is whether our own output animates, which is the same seam the decoder had.
/// </para>
/// </summary>
public sealed class AnimatedPictureTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;
    private readonly MagickCodec _codec = new();

    /// <summary>
    /// The picture the table would get: normalised by the codec and handed to the animation, in a
    /// real window - because that is the only place it can be observed.
    /// <para>
    /// <b>Measured, and the first two attempts measured nothing.</b> The library sets its animation
    /// up when the control is LOADED into a visual tree; an <c>Image</c> on its own reports no
    /// controller however good its source is. So the assertion needs a window, and without one this
    /// test would have said "no animation" for a reason that has nothing to do with the picture.
    /// </para>
    /// </summary>
    [Fact]
    public void An_animated_gif_from_our_own_codec_really_animates()
    {
        var bytes = _codec
            .Normalise(File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")))
            .Bytes;

        var frames = InAWindow(
            image => AnimatedPicture.Run(image, bytes),
            image => ImageBehavior.GetAnimationController(image)?.FrameCount ?? 0);

        Assert.Equal(3, frames);
    }

    /// <summary>
    /// Freezing stops the clock. A timer nobody can see is the exact cost the ceiling exists to
    /// avoid (Part 6), and a picture the DM paused must not keep paying it.
    /// <para>
    /// <b>Measured, and it caught a false claim of mine.</b> Clearing the animated source alone
    /// does NOT stop it - afterwards the controller is still there and still reports its three
    /// frames. The clock is now paused explicitly, and this asserts the state rather than the call.
    /// </para>
    /// </summary>
    [Fact]
    public void Freezing_stops_the_clock()
    {
        var normalised = _codec.Normalise(
            File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")));

        var running = InAWindow(image =>
        {
            AnimatedPicture.Run(image, normalised.Bytes);
            AnimatedPicture.Freeze(image, (BitmapSource)PictureDecoder.Decode(normalised.Bytes));
        },
        image => ImageBehavior.GetAnimationController(image) is { IsPaused: false });

        Assert.False(running, "the animation was still running after it had been frozen");
    }

    /// <summary>
    /// The DM's pause stops the picture where it stands, and starting it again does not build a new
    /// animation. Measured at the table (hand-run of M2b, step 24): stopping snapped back to the
    /// first frame a moment later, and every further change to the scene started the whole thing
    /// over.
    /// <para>
    /// <b>What this can and cannot see.</b> The window is off-screen and therefore never composited,
    /// so the clock does not advance and <c>CurrentFrame</c> stays where it started however often it
    /// is seeked - the first version of this test asserted a frame index and was measuring the
    /// harness. What it CAN establish is that the same controller survives both calls: one clock,
    /// paused and started again, is what "carries on from where it was" means at this level.
    /// </para>
    /// <para>
    /// The holding and the reading happen in <c>observe</c> and not in <c>arrange</c>, and that is
    /// not tidiness: the library builds its controller when the control is LOADED, so before the
    /// window has settled there is nothing to hold. Each reading is taken at the moment it is true,
    /// too - the first version read them all at the end, by which time resuming had undone the
    /// pause it was asserting.
    /// </para>
    /// </summary>
    [Fact]
    public void Holding_pauses_in_place_and_resuming_uses_the_same_clock()
    {
        var bytes = _codec
            .Normalise(File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")))
            .Bytes;

        var (held, running, sameClock) = InAWindow(
            image => AnimatedPicture.Run(image, bytes),
            image =>
            {
                var before = ImageBehavior.GetAnimationController(image);

                var stopped = AnimatedPicture.Hold(image) && before is { IsPaused: true };
                var started = AnimatedPicture.Resume(image) && before is { IsPaused: false };

                return (stopped, started, ReferenceEquals(before, ImageBehavior.GetAnimationController(image)));
            });

        Assert.True(held, "holding did not pause the animation");
        Assert.True(running, "resuming did not start it again");
        Assert.True(sameClock, "resuming built a new animation instead of carrying the old one on");
    }

    /// <summary>
    /// The other half of the pair, and the reason there are two calls rather than one: freezing
    /// puts the decoded still in the picture's place and lets the animated source go. That is what
    /// the budget's refusals need - and it is exactly what a pause must not do, because a picture
    /// showing its own first frame can no longer say where it was.
    /// <para>
    /// <b>Measured, against a claim of mine.</b> Freezing does NOT remove the controller - it is
    /// still there afterwards, paused. So the two are told apart by what the picture SHOWS, which
    /// is the thing the DM sees, and not by whether some object survived.
    /// </para>
    /// </summary>
    [Fact]
    public void Freezing_shows_the_still_where_holding_leaves_the_animation_showing()
    {
        var normalised = _codec.Normalise(
            File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")));

        var (afterHold, afterFreeze) = InAWindow(
            image => AnimatedPicture.Run(image, normalised.Bytes),
            image =>
            {
                var still = (BitmapSource)PictureDecoder.Decode(normalised.Bytes);

                AnimatedPicture.Hold(image);
                var held = ReferenceEquals(image.Source, still);

                AnimatedPicture.Freeze(image, still);
                var frozen = ReferenceEquals(image.Source, still);

                return (held, frozen);
            });

        Assert.False(afterHold, "holding replaced the running picture with its first frame");
        Assert.True(afterFreeze, "freezing did not put the still picture in its place");
    }

    /// <summary>
    /// Runs <paramref name="arrange"/> against an <c>Image</c> that is really in a window, then
    /// reports what <paramref name="observe"/> makes of it.
    /// <para>
    /// The window is put far off-screen rather than hidden: a hidden one does not lay its content
    /// out, and then the very thing being measured never happens.
    /// </para>
    /// </summary>
    private static T InAWindow<T>(Action<Image> arrange, Func<Image, T> observe)
    {
        var observed = default(T);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            Window? window = null;

            try
            {
                var image = new Image();

                window = new Window
                {
                    Width = 64,
                    Height = 64,
                    Left = -20000,
                    Top = -20000,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                };

                window.Content = image;
                window.Show();

                arrange(image);

                // Let the loaded handlers and the library's set-up run before asking.
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                observed = observe(image);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(20)))
        {
            throw new InvalidOperationException("the window never settled");
        }

        return failure is null ? observed! : throw new InvalidOperationException("the window failed", failure);
    }

}
