namespace PeachDrawing.Svg.Tests;

public class SvgOffscreenSurfaceTests
{
    [Fact]
    public void ADocumentWriterHasNoBitmapToKeepDrawingInto()
    {
        using var scene = SvgScene.Create();

        Assert.Null(scene.Canvas.CreateOffscreenSurface(10, 10));
        Assert.False(scene.Canvas.SupportsCompositeOperators);
    }
}
