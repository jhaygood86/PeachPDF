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

public class LayerEffectTests
{
    private static (RasterRenderContext Context, RasterCanvas Canvas) NewCanvas(int size = 60)
    {
        var context = new RasterRenderContext();
        var canvas = context.CreateCanvas(size, size);
        canvas.DrawRectangle(context.GetSolidBrush(PaintColor.White), 0, 0, size, size);
        return (context, canvas);
    }

    private static int Red(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        return buffer.PremultipliedRgba.Span[(y * buffer.Width + x) * 4];
    }

    [Fact]
    public void BlurEffect_SpreadsInkPastTheShapesEdge()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: new Rect(5, 5, 50, 50), Effects: [new BlurEffect(3)])))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.Black), 20, 20, 20, 20);
            }

            // Well inside stays black, just outside the edge is a grey the un-blurred shape would leave white, far outside is untouched.
            Assert.True(Red(canvas, 30, 30) < 20);
            var nearEdge = Red(canvas, 18, 30);
            Assert.InRange(nearEdge, 30, 235);
            Assert.Equal(255, Red(canvas, 8, 8));
        }
    }

    [Fact]
    public void DropShadowEffect_DrawsAnOffsetCopyBeneathTheContent()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            var shadow = new DropShadowEffect(8, 8, 0.5, 0.5, PaintColor.FromArgb(255, 0, 0, 0));
            using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [shadow])))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 20, 20);
            }

            Assert.Equal(255, Red(canvas, 20, 20));      // the red square itself
            Assert.True(Red(canvas, 34, 34) < 30);      // its shadow, below and to the right
            Assert.Equal(255, Red(canvas, 50, 8));
        }
    }

    [Fact]
    public void Effects_TogetherWithOpacity_FadeTheResult()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            using (var layer = canvas.BeginLayer(new LayerOptions(0.5, Bounds: new Rect(0, 0, 60, 60), Effects: [new BlurEffect(0.5)])))
            {
                Assert.NotNull(layer);
                layer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.Black), 10, 10, 40, 40);
            }

            Assert.InRange(Red(canvas, 30, 30), 115, 140);
        }
    }

    [Fact]
    public void GetInkMargin_GrowsWithBlurAndShadowOffset()
    {
        var margin = RasterLayerEffects.GetInkMargin([new BlurEffect(2, 4), new DropShadowEffect(3, -5, 1, 1, PaintColor.Black)]);

        Assert.Equal(6 + 3 + 3, margin.X);
        Assert.Equal(12 + 5 + 3, margin.Y);
        Assert.Equal((0, 0), RasterLayerEffects.GetInkMargin([]));
    }

    [Fact]
    public void RasterLayerEffects_ValidatesItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => RasterLayerEffects.GetInkMargin(null!));
        Assert.Throws<ArgumentNullException>(() => RasterLayerEffects.Apply(null!, []));
    }
}

public class AntiAliasTests
{
    private static int EdgeAlpha(RasterCanvas canvas)
    {
        // A diagonal edge: the pixel it cuts through is partly covered when smoothed and all-or-nothing when not.
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span;
        var partial = 0;
        for (var i = 3; i < span.Length; i += 4)
        {
            if (span[i] is > 0 and < 255)
                partial++;
        }

        return partial;
    }

    private static int DrawTriangle(Action<RasterCanvas>? around)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);
        around?.Invoke(canvas);
        canvas.DrawPolygon(context.GetSolidBrush(PaintColor.Black), [new PaintPoint(0, 0), new PaintPoint(19, 7), new PaintPoint(3, 19)]);
        return EdgeAlpha(canvas);
    }

    [Fact]
    public void PushAntiAlias_False_DrawsHardEdges()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);

        canvas.PushAntiAlias(false);
        canvas.DrawPolygon(context.GetSolidBrush(PaintColor.Black), [new PaintPoint(0, 0), new PaintPoint(19, 7), new PaintPoint(3, 19)]);
        canvas.PopAntiAlias();

        Assert.Equal(0, EdgeAlpha(canvas));
        Assert.True(DrawTriangle(null) > 0);
    }

    [Fact]
    public void PopAntiAlias_RestoresTheRenderWideSetting()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);

        canvas.PushAntiAlias(false);
        canvas.PopAntiAlias();
        canvas.DrawPolygon(context.GetSolidBrush(PaintColor.Black), [new PaintPoint(0, 0), new PaintPoint(19, 7), new PaintPoint(3, 19)]);

        Assert.True(EdgeAlpha(canvas) > 0);
    }

    [Fact]
    public void PushAntiAlias_Calls_Nest()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);

        canvas.PushAntiAlias(false);
        canvas.PushAntiAlias(true);
        canvas.PopAntiAlias();
        canvas.DrawPolygon(context.GetSolidBrush(PaintColor.Black), [new PaintPoint(0, 0), new PaintPoint(19, 7), new PaintPoint(3, 19)]);
        canvas.PopAntiAlias();

        Assert.Equal(0, EdgeAlpha(canvas));
    }

    [Fact]
    public void PopAntiAlias_WithNothingPushed_DoesNothing()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);

        canvas.PopAntiAlias();
        canvas.DrawPolygon(context.GetSolidBrush(PaintColor.Black), [new PaintPoint(0, 0), new PaintPoint(19, 7), new PaintPoint(3, 19)]);

        Assert.True(EdgeAlpha(canvas) > 0);
    }
}
