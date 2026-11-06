using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachImage;
using PeachImage.Formats.Gif;
using PeachImage.Formats.Png;

namespace PeachDrawing.Win2D.Tests;

public class AnimatedImageTests
{
    private static Stream Solid(int r, int g, int b)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(8, 8);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, r, g, b)), 0, 0, 8, 8);
        var stream = new MemoryStream();
        canvas.Save(stream, "png", new PngEncoderOptions());
        stream.Position = 0;
        return stream;
    }

    // Red for 100 ms, then blue for 300 ms, looping forever.
    private static byte[] TwoFrameGif()
    {
        var frames = new[]
        {
            new AnimatedImageFrame(PeachImage.Image.Load(Solid(255, 0, 0)), TimeSpan.FromMilliseconds(100), default),
            new AnimatedImageFrame(PeachImage.Image.Load(Solid(0, 0, 255)), TimeSpan.FromMilliseconds(300), default),
        };

        var animation = new AnimatedImage(frames, 8, 8, 0);
        using var output = new MemoryStream();
        animation.Save(output, "gif", new GifEncoderOptions());
        return output.ToArray();
    }

    [Fact]
    public void An_animated_gif_has_its_frames_and_their_durations()
    {
        using var scene = Scene.Create(16, 16);
        using var image = scene.Context.ImageFromStream(new MemoryStream(TwoFrameGif()));

        Assert.Equal(2, image.FrameCount);
        Assert.Equal(0, image.LoopCount);
        Assert.Equal(TimeSpan.FromMilliseconds(100), image.GetFrameDuration(0));
        Assert.Equal(TimeSpan.FromMilliseconds(300), image.GetFrameDuration(1));
        Assert.Equal(0, image.CurrentFrame);
    }

    [Fact]
    public void The_current_frame_is_the_one_that_is_drawn()
    {
        using var scene = Scene.Create(16, 16);
        using var image = scene.Context.ImageFromStream(new MemoryStream(TwoFrameGif()));

        scene.Canvas.DrawImage(image, new Rect(0, 0, 8, 8));
        image.CurrentFrame = 1;
        scene.Canvas.DrawImage(image, new Rect(8, 8, 8, 8));

        Assert.True(scene.IsRed(4, 4));
        Assert.True(scene.Px(12, 12) is { B: > 200, R: < 40 });
    }

    [Fact]
    public void A_still_image_has_one_frame_and_ignores_the_current_frame()
    {
        using var scene = Scene.Create(16, 16);
        using var image = scene.Context.ImageFromStream(Solid(255, 0, 0));

        image.CurrentFrame = 3;

        Assert.Equal(1, image.FrameCount);
        Assert.Equal(0, image.CurrentFrame);
    }
}
