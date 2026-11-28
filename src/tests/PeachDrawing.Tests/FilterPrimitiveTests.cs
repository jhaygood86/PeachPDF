using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>The filter primitives a layer can apply, drawn by the raster canvas: the reference the native backends are held to.</summary>
public class FilterPrimitiveTests
{
    private static (RasterRenderContext Context, RasterCanvas Canvas) NewCanvas(int size = 60)
    {
        var context = new RasterRenderContext();
        return (context, context.CreateCanvas(size, size));
    }

    private static byte[] Alphas(RasterCanvas canvas, int y, params int[] xs)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.ToArray();
        return xs.Select(x => span[(y * buffer.Width + x) * 4 + 3]).ToArray();
    }

    [Fact]
    public void AComponentTransfer_MapsEachChannelThroughItsTable()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            var invertRed = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();
            using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new ComponentTransferEffect(invertRed, null, null, null)])))
                layer!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 30, 30);

            var span = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
            var i = (20 * 60 + 20) * 4;
            Assert.Equal(0, span[i]);
            Assert.Equal(255, span[i + 3]);
        }
    }

    [Fact]
    public void AConvolution_TurnsItsKernelRoundAndPutsItsTargetOverThePixel()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60),
                Effects: [new ConvolveMatrixEffect(3, 1, [1, 0, 0], 1, 0, 1, 0, FilterEdgeMode.None, false)])))
                layer!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 20, 20, 20, 20);

            Assert.Equal([255, 0, 255], Alphas(canvas, 30, 19, 39, 38));
        }
    }

    [Fact]
    public void Turbulence_ReplacesTheLayerWithNoise_AndNoOctavesDrawNothing()
    {
        var (context, canvas) = NewCanvas();
        using (canvas)
        {
            using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new TurbulenceEffect(0.1, 0.1, 2, 7, false, false)])))
                layer!.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 5, 5);

            Assert.True(Alphas(canvas, 40, 20, 25, 30, 35, 40, 45, 50, 55).Distinct().Count() > 3);
        }

        var (context2, empty) = NewCanvas();
        using (empty)
        {
            using (var layer = empty.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new TurbulenceEffect(0.1, 0.1, 0, 7, false, false)])))
                layer!.Canvas.DrawRectangle(context2.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 60, 60);

            Assert.All(Alphas(empty, 30, 5, 30, 55), a => Assert.Equal(0, a));
        }
    }

    [Fact]
    public void ATransferTable_FollowsTheFunction()
    {
        var linear = ComponentTransferEffect.BuildTable(TransferFunctionKind.Linear, null, 0.5, 0.25, 1, 1, 0);
        var discrete = ComponentTransferEffect.BuildTable(TransferFunctionKind.Discrete, [0.0, 1.0], 1, 0, 1, 1, 0);
        var table = ComponentTransferEffect.BuildTable(TransferFunctionKind.Table, [0.5], 1, 0, 1, 1, 0);

        Assert.Equal(64, linear[0]);
        Assert.Equal(191, linear[255]);
        Assert.Equal(0, discrete[100]);
        Assert.Equal(255, discrete[200]);
        Assert.All(table, v => Assert.Equal(128, v));
        Assert.Equal(7, ComponentTransferEffect.BuildTable(TransferFunctionKind.Identity, null, 1, 0, 1, 1, 0)[7]);
    }
}
