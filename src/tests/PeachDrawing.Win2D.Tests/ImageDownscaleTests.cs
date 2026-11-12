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
}
