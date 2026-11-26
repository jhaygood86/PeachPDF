using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>The Porter-Duff operators of <see cref="PaintBlendMode"/>: a transparent 40x20 canvas with opaque red on the left 20 columns, and opaque blue drawn over the middle 20.</summary>
public class CompositeOperatorTests
{
    private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
    private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);

    // Columns 2 (red only), 15 (red and blue), 28 (blue only) and 38 (neither).
    private static readonly int[] Columns = [2, 15, 28, 38];

    private static (byte R, byte G, byte B, byte A)[] Scene(PaintBlendMode mode, bool inLayer)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(40, 20);
        canvas.DrawRectangle(context.GetSolidBrush(Red), 0, 0, 20, 20);

        if (inLayer)
        {
            // How a 2D canvas applies an operator: the shape is drawn in a layer over the whole region and the layer is composited with the operator.
            using var layer = canvas.BeginLayer(new LayerOptions(1.0, mode, Bounds: new Rect(0, 0, 40, 20)));
            Assert.NotNull(layer);
            layer.Canvas.DrawRectangle(context.GetSolidBrush(Blue), 10, 0, 20, 20);
        }
        else
        {
            canvas.PushBlendMode(mode);
            canvas.DrawRectangle(context.GetSolidBrush(Blue), 10, 0, 20, 20);
            canvas.PopBlendMode();
        }

        var bytes = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
        return [.. Columns.Select(x => (bytes[x * 4], bytes[x * 4 + 1], bytes[x * 4 + 2], bytes[x * 4 + 3]))];
    }

    private static readonly (byte, byte, byte, byte) Clear = (0, 0, 0, 0);
    private static readonly (byte, byte, byte, byte) R = (255, 0, 0, 255);
    private static readonly (byte, byte, byte, byte) B = (0, 0, 255, 255);

    public static TheoryData<PaintBlendMode, string> InLayer => new()
    {
        { PaintBlendMode.SourceIn, "clear B clear clear" },
        { PaintBlendMode.SourceOut, "clear clear B clear" },
        { PaintBlendMode.SourceAtop, "R B clear clear" },
        { PaintBlendMode.DestinationOver, "R R B clear" },
        { PaintBlendMode.DestinationIn, "clear R clear clear" },
        { PaintBlendMode.DestinationOut, "R clear clear clear" },
        { PaintBlendMode.DestinationAtop, "clear R B clear" },
        { PaintBlendMode.Xor, "R clear B clear" },
        { PaintBlendMode.Copy, "clear B B clear" },
        { PaintBlendMode.Clear, "clear clear clear clear" },
    };

    private static (byte, byte, byte, byte) Named(string name) => name switch { "R" => R, "B" => B, _ => Clear };

    [Theory]
    [MemberData(nameof(InLayer))]
    public void AnOperator_InALayerOverTheRegion_CombinesTheShapeWithTheBackdropEverywhere(PaintBlendMode mode, string expected)
    {
        var actual = Scene(mode, inLayer: true);

        Assert.Equal(expected.Split(' ').Select(Named).ToArray(), actual.Select(p => (p.R, p.G, p.B, p.A)).ToArray());
    }

    [Fact]
    public void DestinationOut_DrawnDirectly_ErasesOnlyWhereTheShapeIs()
    {
        // Reaches no further than the source does, so it needs no layer: the red outside the shape stays.
        var actual = Scene(PaintBlendMode.DestinationOut, inLayer: false);

        Assert.Equal([R, Clear, Clear, Clear], actual.Select(p => (p.R, p.G, p.B, p.A)).ToArray());
    }

    [Fact]
    public void SourceIn_DrawnDirectly_ReachesOnlyAsFarAsTheShape()
    {
        // Without a layer there is no source outside the shape, so the backdrop there is left alone.
        var actual = Scene(PaintBlendMode.SourceIn, inLayer: false);

        Assert.Equal([R, B, Clear, Clear], actual.Select(p => (p.R, p.G, p.B, p.A)).ToArray());
    }

    [Fact]
    public void ACanvasOfARasterBackend_SaysItCompositesWithOperators()
    {
        using var canvas = new RasterRenderContext().CreateCanvas(4, 4);

        Assert.True(canvas.SupportsCompositeOperators);
    }

    [Fact]
    public void AnOperator_OverATranslucentBackdrop_UsesTheAlphaOfBothSides()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(10, 10);
        canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(128, 255, 0, 0)), 0, 0, 10, 10);

        using (var layer = canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.SourceIn, Bounds: new Rect(0, 0, 10, 10))))
            layer!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(128, 0, 0, 255)), 0, 0, 10, 10);

        // source-in: the source scaled by the backdrop's alpha, 128/255 of 128/255.
        var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
        Assert.InRange(span[3], 63, 65);
        Assert.Equal(0, span[0]);
        Assert.InRange(span[2], 63, 65);
    }
}
