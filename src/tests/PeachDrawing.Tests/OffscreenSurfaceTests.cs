using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class OffscreenSurfaceTests
{
    private static (RasterRenderContext Context, RasterCanvas Page) Page(int size = 20)
    {
        var context = new RasterRenderContext();
        return (context, context.CreateCanvas(size, size));
    }

    private static (byte R, byte G, byte B, byte A) At(PixelBuffer pixels, int x, int y)
    {
        var span = pixels.PremultipliedRgba.Span;
        var i = (y * pixels.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }

    [Fact]
    public void ASurface_KeepsWhatIsDrawnIntoItAcrossReads()
    {
        var (context, page) = Page();
        using (page)
        using (var surface = page.CreateOffscreenSurface(10, 8))
        {
            Assert.NotNull(surface);
            Assert.Equal((10, 8), (surface!.Width, surface.Height));

            surface.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 5, 8);
            Assert.Equal((255, 0, 0, 255), At(surface.ReadPixels(), 2, 4));

            // Drawing again after a read adds to what is there.
            surface.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 255)), 5, 0, 5, 8);
            var pixels = surface.ReadPixels();
            Assert.Equal((255, 0, 0, 255), At(pixels, 2, 4));
            Assert.Equal((0, 0, 255, 255), At(pixels, 7, 4));
        }
    }

    [Fact]
    public void ASnapshot_KeepsTheContentItWasTakenWith_AndDrawsOntoACanvasOfTheBackend()
    {
        var (context, page) = Page();
        using (page)
        using (var surface = page.CreateOffscreenSurface(10, 10))
        {
            surface!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 10, 10);
            using var snapshot = surface.CreateSnapshot();
            surface.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 255)), 0, 0, 10, 10);

            Assert.True(surface.CanDraw(page));
            page.DrawImage(snapshot, new Rect(2, 3, 10, 10));

            var pixels = page.ToPixelBuffer();
            Assert.Equal((255, 0, 0, 255), At(pixels, 5, 5));
            Assert.Equal((0, 0, 0, 0), At(pixels, 15, 15));
            Assert.Equal((0, 0, 255, 255), At(surface.ReadPixels(), 5, 5));
        }
    }

    [Fact]
    public void Clear_MakesEveryPixelTransparent_AndAReadIsACopy()
    {
        var (context, page) = Page();
        using (page)
        using (var surface = page.CreateOffscreenSurface(4, 4))
        {
            surface!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 0, 255, 0)), 0, 0, 4, 4);
            var before = surface.ReadPixels();
            surface.Clear();

            Assert.Equal((0, 255, 0, 255), At(before, 1, 1));
            Assert.Equal((0, 0, 0, 0), At(surface.ReadPixels(), 1, 1));
        }
    }

    [Fact]
    public void ASurface_ComposesWithOperators_LikeAnyCanvasOfTheBackend()
    {
        var (context, page) = Page();
        using (page)
        using (var surface = page.CreateOffscreenSurface(10, 10))
        {
            Assert.True(surface!.Canvas.SupportsCompositeOperators);
            surface.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 10, 10);
            using (var layer = surface.Canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.DestinationOut, Bounds: new Rect(0, 0, 10, 10))))
                layer!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.Black), 0, 0, 5, 10);

            var pixels = surface.ReadPixels();
            Assert.Equal((0, 0, 0, 0), At(pixels, 2, 5));
            Assert.Equal((255, 0, 0, 255), At(pixels, 7, 5));
        }
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(-1, 5)]
    public void ASizeThatIsNotPositive_GivesNoSurface(int width, int height)
    {
        var (_, page) = Page();
        using (page)
            Assert.Null(page.CreateOffscreenSurface(width, height));
    }

    [Fact]
    public void ASurfaceBiggerThanTheContextAllows_GivesNoSurface()
    {
        var context = new RasterRenderContext { MaxRasterPixels = 100 };
        using var page = context.CreateCanvas(4, 4);

        Assert.Null(page.CreateOffscreenSurface(11, 10));
        using var small = page.CreateOffscreenSurface(10, 10);
        Assert.NotNull(small);
    }
}
