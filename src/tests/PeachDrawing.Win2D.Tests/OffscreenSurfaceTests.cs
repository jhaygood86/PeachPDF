using PeachDrawing.Core;

namespace PeachDrawing.Win2D.Tests;

public class OffscreenSurfaceTests
{
    [Fact]
    public void ASurface_KeepsWhatIsDrawnIntoItAcrossReads()
    {
        using var scene = Scene.Create();
        using var surface = scene.Canvas.CreateOffscreenSurface(10, 8);
        Assert.NotNull(surface);

        surface!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 8);
        Assert.Equal((255, 0, 0, 255), At(surface.ReadPixels(), 2, 4));

        surface.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 5, 0, 5, 8);
        var pixels = surface.ReadPixels();
        Assert.Equal((255, 0, 0, 255), At(pixels, 2, 4));
        Assert.Equal((0, 0, 255, 255), At(pixels, 7, 4));
    }

    [Fact]
    public void ASnapshot_KeepsItsContent_AndDrawsOntoACanvasOfTheBackend()
    {
        using var scene = Scene.Create();
        using var surface = scene.Canvas.CreateOffscreenSurface(10, 10);
        surface!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        using var snapshot = surface.CreateSnapshot();
        surface.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 0, 0, 10, 10);

        Assert.True(surface.CanDraw(scene.Canvas));
        scene.Canvas.DrawImage(snapshot, new Rect(2, 3, 10, 10));

        Assert.Equal((255, 0, 0, 255), scene.Px(5, 5));
        Assert.Equal((0, 0, 255, 255), At(surface.ReadPixels(), 5, 5));
    }

    [Fact]
    public void Clear_MakesEveryPixelTransparent()
    {
        using var scene = Scene.Create();
        using var surface = scene.Canvas.CreateOffscreenSurface(4, 4);
        surface!.Canvas.DrawRectangle(scene.Solid(0, 255, 0), 0, 0, 4, 4);
        surface.Clear();

        Assert.Equal((0, 0, 0, 0), At(surface.ReadPixels(), 1, 1));
    }

    [Fact]
    public void ASurface_ComposesWithOperators()
    {
        using var scene = Scene.Create();
        using var surface = scene.Canvas.CreateOffscreenSurface(10, 10);
        Assert.True(surface!.Canvas.SupportsCompositeOperators);
        surface.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        using (var layer = surface.Canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.DestinationOut, Bounds: new Rect(0, 0, 10, 10))))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 10);

        var pixels = surface.ReadPixels();
        Assert.Equal((0, 0, 0, 0), At(pixels, 2, 5));
        Assert.Equal((255, 0, 0, 255), At(pixels, 7, 5));
    }

    [Fact]
    public void ASurfaceAndItsSnapshots_AreAllReleased()
    {
        using var scene = Scene.Create();
        var surface = scene.Canvas.CreateOffscreenSurface(6, 6)!;
        var snapshot = surface.CreateSnapshot();
        scene.Canvas.DrawImage(snapshot, new Rect(0, 0, 6, 6));
        Assert.True(scene.Context.Counts.Total > 0);

        snapshot.Dispose();
        surface.Dispose();

        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, -2)]
    public void ASizeThatIsNotPositive_GivesNoSurface(int width, int height)
    {
        using var scene = Scene.Create();

        Assert.Null(scene.Canvas.CreateOffscreenSurface(width, height));
    }

    private static (byte R, byte G, byte B, byte A) At(PixelBuffer pixels, int x, int y)
    {
        var span = pixels.PremultipliedRgba.Span;
        var i = (y * pixels.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }
}
