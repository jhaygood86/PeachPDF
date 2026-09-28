using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class LayerTests
{
    private static (RasterRenderContext Context, RasterCanvas Canvas) NewCanvas(int size = 40)
    {
        var context = new RasterRenderContext();
        var canvas = context.CreateCanvas(size, size);
        canvas.DrawRectangle(context.GetSolidBrush(PaintColor.White), 0, 0, size, size);
        return (context, canvas);
    }

    private static (byte R, byte G, byte B, byte A) Pixel(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span;
        var i = (y * buffer.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }

    [Fact]
    public void BeginLayer_CompositesOverlappingContentOnceAtTheLayersOpacity()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            var black = context.GetSolidBrush(PaintColor.Black);

            using (var layer = canvas.BeginLayer(new LayerOptions(0.5)))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(black, 5, 5, 20, 20);
                layer.Canvas.DrawRectangle(black, 15, 15, 20, 20);
            }

            var single = Pixel(canvas, 8, 8);
            var overlap = Pixel(canvas, 20, 20);

            // Drawn straight onto the canvas the overlap would be darker (75% black); as a layer it is the same 50% as elsewhere.
            Assert.InRange(single.R, 120, 135);
            Assert.Equal(single.R, overlap.R);
            Assert.Equal(single.G, overlap.G);
            Assert.Equal(255, Pixel(canvas, 38, 2).R);
        }
    }

    [Fact]
    public void BeginLayer_WithAnExplicitRegion_UsesTheCanvasCoordinatesInsideIt()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            var black = context.GetSolidBrush(PaintColor.Black);

            using (var layer = canvas.BeginLayer(new LayerOptions(1.0, Bounds: new Rect(10, 10, 20, 20))))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(black, 12, 12, 6, 6);
            }

            Assert.Equal(0, Pixel(canvas, 14, 14).R);
            Assert.Equal(255, Pixel(canvas, 5, 5).R);
            Assert.Equal(255, Pixel(canvas, 24, 24).R);
        }
    }

    [Fact]
    public void BeginLayer_AppliesAColorMatrixToTheFinishedLayer()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            // Inverts the colour channels (a black square becomes white on the white page, so it vanishes).
            var invert = new ColorMatrix(
                new System.Numerics.Matrix4x4(-1, 0, 0, 0, 0, -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1),
                new System.Numerics.Vector4(1, 1, 1, 0));

            using (var layer = canvas.BeginLayer(new LayerOptions(1.0, ColorMatrix: invert)))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.Black), 5, 5, 10, 10);
            }

            Assert.Equal(255, Pixel(canvas, 8, 8).R);
        }
    }

    [Fact]
    public void Dispose_MoreThanOnce_CompositesOnce()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            var layer = canvas.BeginLayer(new LayerOptions(0.5));
            Assert.NotNull(layer);
            layer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.Black), 0, 0, 40, 40);

            layer.Dispose();
            var once = Pixel(canvas, 20, 20);
            layer.Dispose();

            Assert.Equal(once, Pixel(canvas, 20, 20));
        }
    }

    [Fact]
    public void CanvasLayer_ValidatesItsArguments()
    {
        var (_, canvas) = NewCanvas();
        using (canvas)
        {
            Assert.Throws<ArgumentNullException>(() => new CanvasLayer(null!, () => { }));
            Assert.Throws<ArgumentNullException>(() => new CanvasLayer(canvas, null!));
        }
    }
}
