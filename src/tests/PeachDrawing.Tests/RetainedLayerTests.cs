using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>The retained layer every canvas can make (from <c>CreateTile</c>): recorded once, drawn again under any transform and opacity.</summary>
public class RetainedLayerTests
{
    private static PaintColor White => PaintColor.FromArgb(255, 255, 255, 255);

    private static (byte R, byte G, byte B, byte A) Px(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span.Slice(((y * buffer.Width) + x) * 4, 4);
        return (span[0], span[1], span[2], span[3]);
    }

    [Fact]
    public void ALayerIsDrawnWhereItsContentWasRecorded_AndAgainUnderAnotherTransform()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(60, 60);
        canvas.DrawRectangle(canvas.GetSolidBrush(White), 0, 0, 60, 60);

        using var layer = canvas.CreateRetainedLayer(new Rect(10, 10, 10, 10))!;
        layer.Recorder.DrawRectangle(layer.Recorder.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 10, 10);
        layer.Seal();

        Assert.True(layer.IsSealed);
        Assert.True(layer.CanDraw(canvas));
        layer.Draw(canvas);
        canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(30, 30));
        layer.Draw(canvas, 0.5);
        canvas.PopTransform();

        Assert.Equal(255, Px(canvas, 15, 15).R);
        Assert.Equal(0, Px(canvas, 15, 15).G);
        var faded = Px(canvas, 45, 45);
        Assert.InRange(faded.G, 100, 160);
        Assert.Equal(255, Px(canvas, 30, 15).G);
    }

    [Fact]
    public void ALayerCannotBeDrawnBeforeItIsSealedOrAfterItIsDisposed()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);
        var layer = canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;

        Assert.False(layer.IsSealed);
        Assert.Throws<InvalidOperationException>(() => layer.Draw(canvas));

        layer.Seal();
        layer.Dispose();

        Assert.False(layer.CanDraw(canvas));
        Assert.Throws<ObjectDisposedException>(() => layer.Draw(canvas));
        Assert.Throws<InvalidOperationException>(() => _ = layer.Recorder);
    }

    [Fact]
    public void NoLayerIsMadeForAnEmptyRegion()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);

        Assert.Null(canvas.CreateRetainedLayer(new Rect(0, 0, 0, 5)));
        Assert.Null(canvas.CreateRetainedLayer(new Rect(0, 0, 5, 0)));
    }

    [Fact]
    public void AStillImage_HasOneFrameAndIgnoresTheCurrentFrame()
    {
        var context = new RasterRenderContext();
        using var source = new MemoryStream();
        using (var canvas = context.CreateCanvas(4, 4))
        {
            canvas.DrawRectangle(canvas.GetSolidBrush(White), 0, 0, 4, 4);
            canvas.Save(source, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        }

        source.Position = 0;
        using var image = context.ImageFromStream(source);
        image.CurrentFrame = 5;

        Assert.Equal(1, image.FrameCount);
        Assert.Equal(0, image.CurrentFrame);
        Assert.Equal(TimeSpan.Zero, image.GetFrameDuration(0));
        Assert.Equal(1, image.LoopCount);
    }
}
