using PeachDrawing.Core;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The Porter-Duff operators of <see cref="PaintBlendMode"/> on Skia: opaque red on the left half, opaque blue over the middle half, the blue drawn in a layer over the whole canvas.</summary>
public class CompositeOperatorTests
{
    private static string Describe((int R, int G, int B, int A) p) => p switch
    {
        (0, 0, 0, 0) => "clear",
        (255, 0, 0, 255) => "R",
        (0, 0, 255, 255) => "B",
        _ => p.ToString(),
    };

    private static string[] Run(PaintBlendMode mode)
    {
        using var scene = Scene.Create();
        var canvas = scene.Canvas;
        canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 20, 40);
        using (var layer = canvas.BeginLayer(new LayerOptions(1.0, mode, Bounds: new Rect(0, 0, 40, 40))))
        {
            Assert.NotNull(layer);
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 10, 0, 20, 40);
        }

        return [.. new[] { 2, 15, 28, 38 }.Select(x => Describe(scene.Px(x, 20)))];
    }

    [Theory]
    [InlineData(PaintBlendMode.SourceIn, "clear B clear clear")]
    [InlineData(PaintBlendMode.SourceOut, "clear clear B clear")]
    [InlineData(PaintBlendMode.SourceAtop, "R B clear clear")]
    [InlineData(PaintBlendMode.DestinationOver, "R R B clear")]
    [InlineData(PaintBlendMode.DestinationIn, "clear R clear clear")]
    [InlineData(PaintBlendMode.DestinationOut, "R clear clear clear")]
    [InlineData(PaintBlendMode.DestinationAtop, "clear R B clear")]
    [InlineData(PaintBlendMode.Xor, "R clear B clear")]
    [InlineData(PaintBlendMode.Copy, "clear B B clear")]
    [InlineData(PaintBlendMode.Clear, "clear clear clear clear")]
    public void AnOperator_InALayerOverTheRegion_CombinesTheShapeWithTheBackdropEverywhere(PaintBlendMode mode, string expected) =>
        Assert.Equal(expected.Split(' '), Run(mode));

    [Fact]
    public void ACanvas_SaysItCompositesWithOperators()
    {
        using var scene = Scene.Create();

        Assert.True(scene.Canvas.SupportsCompositeOperators);
    }

    [Fact]
    public void ALayerCompositedWithAnOperator_LeavesNothingBehind()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.DestinationOut, Bounds: new Rect(0, 0, 40, 40))))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 5, 5, 10, 10);

        Assert.Equal("clear", Describe(scene.Px(10, 10)));
        Assert.Equal("R", Describe(scene.Px(30, 30)));
        Assert.Equal(0, scene.Context.Counts.Total);
    }
}
