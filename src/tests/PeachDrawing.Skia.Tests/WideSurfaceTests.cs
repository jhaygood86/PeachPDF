using PeachDrawing.Core;

namespace PeachDrawing.Skia.Tests;

/// <summary>A surface of half-float pixels keeps colors and pictures outside 0 to 1 whole.</summary>
public class WideSurfaceTests
{
    [Fact]
    public void AWideSurface_KeepsAColorOutsideZeroToOne_AndGivesItsEightBitViewBroughtIntoRange()
    {
        using var scene = Scene.Create();
        using var surface = scene.Canvas.CreateOffscreenSurface(4, 4, wide: true);
        Assert.NotNull(surface);
        Assert.True(surface!.IsWide);

        surface.Canvas.DrawRectangle(new SolidBrush(new WideColor(2f, -0.5f, 0.25f, 1f)), 0, 0, 4, 4);

        var wide = surface.ReadWidePixels()!;
        Assert.Equal(2f, wide[0], 2);
        Assert.Equal(-0.5f, wide[1], 2);
        Assert.Equal(0.25f, wide[2], 2);
        Assert.Equal(1f, wide[3], 2);

        var bytes = surface.ReadPixels().PremultipliedRgba.Span;
        Assert.Equal(255, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.InRange((int)bytes[2], 63, 65);
    }

    [Fact]
    public void ASurfaceOfEightBits_IsNotWide_AndAnImageOfFloatsIsDrawnWhole()
    {
        using var scene = Scene.Create();
        using var plain = scene.Canvas.CreateOffscreenSurface(4, 4);
        Assert.False(plain!.IsWide);
        Assert.Null(plain.ReadWidePixels());

        using var image = scene.Canvas.CreateWideImage(2, 2, [3f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f]);
        Assert.NotNull(image);
        using var surface = scene.Canvas.CreateOffscreenSurface(2, 2, wide: true);
        surface!.Canvas.DrawImage(image!, new Rect(0, 0, 2, 2), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        Assert.Equal(3f, surface.ReadWidePixels()![0], 2);

        plain.Canvas.DrawImage(image!, new Rect(0, 0, 2, 2), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        Assert.Equal(255, plain.ReadPixels().PremultipliedRgba.Span[0]);
    }
}
