using System.Runtime.ExceptionServices;
using System.Windows;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The drawing of a scene fills step by step, like the table (Part 10, M5b): a few previews per pass
/// rather than all of them in the first one. Laid out on a thread of its own - an element,
/// no window.
/// </summary>
public sealed class SceneThumbnailTests
{
    private static readonly AssetMeta Meta = new(800, 600, "png", 1024, IsAnimated: false, new string('b', 64));

    [Fact]
    public void Seven_hundred_pictures_arriving_at_once_come_in_a_few_per_pass()
    {
        OnUiThread(() =>
        {
            var pictures = Enumerable.Range(1, 700).Select(Picture).ToList();
            var thumbnail = new SceneThumbnail(new Pictures(new OpenStock(() => throw new InvalidOperationException("no campaign here"))));

            thumbnail.Show(SceneState.Empty with { Items = pictures }, ScreenContext.Default(new PixelSize(1920, 1080), 96), default);

            Render(thumbnail);

            Assert.Equal(SceneThumbnail.PreviewsPerPass, pictures.Count(picture => thumbnail.Shows(picture.AssetId)));

            Render(thumbnail);

            Assert.Equal(2 * SceneThumbnail.PreviewsPerPass, pictures.Count(picture => thumbnail.Shows(picture.AssetId)));
        });
    }

    /// <summary>One drawing pass: the first arrange draws, every later pass is asked for as the stage asks.</summary>
    private static void Render(FrameworkElement element)
    {
        if (element.RenderSize.Width == 0)
        {
            element.Measure(new Size(320, 180));
            element.Arrange(new System.Windows.Rect(0, 0, 320, 180));
            return;
        }

        element.InvalidateVisual();
        element.UpdateLayout();
    }

    private static ImageItem Picture(int number) =>
        new(
            new ItemId(Guid.NewGuid()),
            0.5,
            0.5,
            0.05,
            4.0 / 3,
            0,
            number,
            false,
            false,
            1,
            new AssetId(number.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)),
            Meta,
            "Bild",
            false,
            false);

    /// <summary>WPF elements live on a single-threaded apartment; the test runner's threads are not one.</summary>
    private static void OnUiThread(Action body)
    {
        ExceptionDispatchInfo? fault = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                fault = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        fault?.Throw();
    }
}
