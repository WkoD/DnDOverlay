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
