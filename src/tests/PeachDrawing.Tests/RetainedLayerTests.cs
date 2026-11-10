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

    private static RetainedLayer RedSquareLayer(RasterCanvas canvas)
    {
        var layer = canvas.CreateRetainedLayer(new Rect(40, 40, 20, 20))!;
        layer.Recorder.DrawRectangle(layer.Recorder.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 40, 40, 20, 20);
        layer.Seal();
        return layer;
    }

    [Fact]
    public void ALayerDrawnWithABlurSpreadsBeyondItsBounds_AndTheContentIsNotRecordedAgain()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(100, 100);
        using var layer = RedSquareLayer(canvas);

        layer.Draw(canvas, 1.0, new LayerEffect[] { new BlurEffect(4) });

        // Ink past the 20 x 20 bounds (the blur's margin makes room for it) and a softened edge.
        Assert.True(Px(canvas, 36, 50).A > 20, $"left of the bounds: {Px(canvas, 36, 50)}");
        Assert.InRange(Px(canvas, 40, 50).A, 60, 200);
        Assert.True(Px(canvas, 50, 50).A > 240, "the middle stays opaque");
        Assert.Equal(0, Px(canvas, 20, 50).A);

        // The same layer drawn again with a different blur: the recorded content is still there, now spread further.
        layer.Draw(canvas, 1.0, new LayerEffect[] { new BlurEffect(8) });
        Assert.True(Px(canvas, 30, 50).A > 15, $"a wider blur reaches further: {Px(canvas, 30, 50)}");
    }

    [Fact]
    public void NoEffectsOrAnEmptyListDrawsTheLayerPlainly()
    {
        var context = new RasterRenderContext();
        using var plain = context.CreateCanvas(100, 100);
        using var viaNull = context.CreateCanvas(100, 100);
        using var viaEmpty = context.CreateCanvas(100, 100);
        using (var layer = RedSquareLayer(plain))
        {
            layer.Draw(plain, 0.7);
        }

        using (var layer = RedSquareLayer(viaNull))
        {
            layer.Draw(viaNull, 0.7, null);
        }

        using (var layer = RedSquareLayer(viaEmpty))
        {
            layer.Draw(viaEmpty, 0.7, Array.Empty<LayerEffect>());
        }

        Assert.True(plain.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(viaNull.ToPixelBuffer().PremultipliedRgba.Span));
        Assert.True(plain.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(viaEmpty.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public void TheOpacityOfAnEffectDrawIsAppliedOnceToTheBlurredLayer()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(100, 100);
        using var layer = RedSquareLayer(canvas);

        layer.Draw(canvas, 0.5, new LayerEffect[] { new BlurEffect(3) });

        // The middle is the layer's red at half opacity, not at a quarter (opacity applied to the content and again to the blur).
        Assert.InRange(Px(canvas, 50, 50).A, 120, 135);
    }

    [Fact]
    public void AnEffectDrawIsMovedByTheTargetTransform()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(100, 100);
        using var layer = RedSquareLayer(canvas);

        canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(-30, -30));
        layer.Draw(canvas, 1.0, new LayerEffect[] { new BlurEffect(2) });
        canvas.PopTransform();

        Assert.True(Px(canvas, 20, 20).A > 240);
        Assert.Equal(0, Px(canvas, 50, 50).A);
    }

    [Fact]
    public void AnEffectDrawOfAnUnsealedLayerThrows()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(40, 40);
        using var layer = canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;

        Assert.Throws<InvalidOperationException>(() => layer.Draw(canvas, 1.0, new LayerEffect[] { new BlurEffect(2) }));
    }
}
