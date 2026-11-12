using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachImage.Formats.Png;

namespace PeachDrawing.Win2D.Tests;

/// <summary>An image drawn much smaller than it is keeps its thin features: the reduction is filtered, not sampled at a few texels.</summary>
public class ImageDownscaleTests
{
    private static byte[] StripePng()
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(2000, 2000);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 0)), 0, 0, 2000, 2000);

        // A white line two source pixels wide, between the texels a 50:1 bilinear reduction reads.
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 1000, 0, 2, 2000);
        using var stream = new MemoryStream();
        canvas.Save(stream, "png", new PngEncoderOptions());
        return stream.ToArray();
    }

    [Fact]
    public void A_thin_line_survives_a_fifty_to_one_reduction()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 40, 40);
        using var image = context.ImageFromStream(new MemoryStream(StripePng()));

        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 40));

        // Bilinear sampling reads the texels at 25, 75... and never meets the line at 1000 and 1001, so the picture comes out black.
        var brightest = Enumerable.Range(0, 40).Max(x => scene.Px(x, 20).R);
        Assert.True(brightest > 3, $"the line was dropped (brightest pixel {brightest})");

        // And it is the same line all the way down, not a few sampled rows of it.
        Assert.InRange(Enumerable.Range(0, 40).Max(x => scene.Px(x, 5).R), brightest - 2, brightest + 2);
    }

    [Fact]
    public void An_image_drawn_small_is_held_small_and_decoded_again_when_drawn_larger()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 400, 400);
        using var image = Assert.IsType<Win2dImage>(context.ImageFromStream(new MemoryStream(StripePng())));

        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 40));
        Assert.InRange(image.BitmapScale, 1.0 / 64, 0.1);

        // A draw that needs fewer pixels keeps what it has; one that needs more decodes the image again at the size it reads.
        var small = image.BitmapScale;
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 20));
        Assert.Equal(small, image.BitmapScale);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 400, 400));
        Assert.True(image.BitmapScale > small);
        Assert.True(image.BitmapScale <= 1);
    }

    [Fact]
    public void A_part_of_an_image_drawn_from_a_reduced_bitmap_is_the_same_part()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 40, 40);
        using var image = Assert.IsType<Win2dImage>(context.ImageFromStream(new MemoryStream(StripePng())));

        // The white line is at x 1000..1002 of 2000: the right half of the image has none of it, the left half none either, the middle strip all of it.
        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 40), new Rect(900, 0, 200, 2000));
        Assert.True(image.BitmapScale < 1);
        Assert.True(Enumerable.Range(0, 40).Max(x => scene.Px(x, 20).R) > 3, "the line is in the part and was lost");

        using var other = new Scene(context, 40, 40);
        other.Canvas.DrawImage(image, new Rect(0, 0, 40, 40), new Rect(1200, 0, 200, 2000));
        Assert.True(Enumerable.Range(0, 40).Max(x => other.Px(x, 20).R) <= 3, "the line is not in the part but was drawn");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.9)]
    [InlineData(0.6)]
    [InlineData(0.5)]
    [InlineData(0.3)]
    [InlineData(0.2)]
    [InlineData(0.05)]
    public void The_scale_kept_for_a_draw_has_its_resolution_and_no_more_than_a_fifth_over(double needed)
    {
        var scale = Win2dImage.ScaleFor(needed);
        Assert.InRange(scale, needed, Math.Min(1, needed * 1.19) + 1e-9);
    }

    [Fact]
    public void The_scale_kept_is_never_below_a_sixty_fourth()
    {
        Assert.Equal(1.0 / 64, Win2dImage.ScaleFor(0.001));
        Assert.Equal(1.0, Win2dImage.ScaleFor(3));
    }
}
