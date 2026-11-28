using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>A raster surface that keeps floating-point pixels: colors and pictures outside 0 to 1 survive painting, blending and erasing.</summary>
public class WideSurfaceTests
{
    private static RasterCanvas Canvas(int size = 8) => new RasterRenderContext().CreateCanvas(size, size, 96, wide: true);

    [Fact]
    public void AColorOutsideZeroToOne_IsKeptWhole_WhileTheEightBitViewIsBroughtIntoRange()
    {
        using var canvas = Canvas();
        canvas.DrawRectangle(new SolidBrush(new WideColor(2f, -0.5f, 0.25f, 1f)), 0, 0, 8, 8);

        var wide = canvas.Surface.WidePixels;
        Assert.Equal([2f, -0.5f, 0.25f, 1f], wide[..4].ToArray());
        Assert.Equal([255, 0, 64, 255], canvas.Surface.Pixels[..4].ToArray());
    }

    [Fact]
    public void ABrushOfEightBitsIsPaintedTheSameOnAWideSurface()
    {
        using var canvas = Canvas();
        canvas.DrawRectangle(new SolidBrush(PaintColor.FromArgb(255, 255, 128, 0)), 0, 0, 8, 8);

        Assert.Equal([255, 128, 0, 255], canvas.Surface.Pixels[..4].ToArray());
        Assert.Equal(128 / 255f, canvas.Surface.WidePixels[1], 4);
    }

    [Fact]
    public void SourceOver_KeepsAnOverrangeBackdrop_AndAPorterDuffOperatorActsOnFloats()
    {
        using var canvas = Canvas();
        canvas.DrawRectangle(new SolidBrush(new WideColor(3f, 0f, 0f, 1f)), 0, 0, 8, 8);
        canvas.DrawRectangle(new SolidBrush(new WideColor(0f, 1f, 0f, 0.5f)), 0, 0, 8, 8);
        var over = canvas.Surface.WidePixels[..4].ToArray();
        Assert.Equal(1.5f, over[0], 4);
        Assert.Equal(0.5f, over[1], 4);

        canvas.PushBlendMode(PaintBlendMode.Copy);
        canvas.DrawRectangle(new SolidBrush(new WideColor(-1f, 0f, 0f, 1f)), 0, 0, 8, 8);
        canvas.PopBlendMode();
        Assert.Equal(-1f, canvas.Surface.WidePixels[0], 4);
    }

    [Fact]
    public void AnImageOfFloats_IsDrawnWithItsFloats_AndOnAPlainSurfaceAsTheEightBitViewOfThem()
    {
        using var wideCanvas = Canvas(4);
        var image = wideCanvas.CreateWideImage(2, 2, [2f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 0.5f]);
        Assert.NotNull(image);
        wideCanvas.PushBlendMode(PaintBlendMode.Copy);
        wideCanvas.DrawImage(image, new Rect(0, 0, 2, 2), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        wideCanvas.PopBlendMode();
        Assert.Equal(2f, wideCanvas.Surface.WidePixels[0], 4);

        using var plain = new RasterRenderContext().CreateCanvas(4, 4);
        plain.DrawImage(image, new Rect(0, 0, 2, 2), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        Assert.Equal([255, 0, 0, 255], plain.Surface.Pixels[..4].ToArray());
    }

    [Fact]
    public void ErasingScalesTheFloatsToo_AndAnOffscreenSurfaceGivesThemBackStraight()
    {
        using var measuring = new RasterRenderContext().CreateCanvas(1, 1);
        using var surface = measuring.CreateOffscreenSurface(4, 4, wide: true)!;
        Assert.True(surface.IsWide);
        surface.Canvas.DrawRectangle(new SolidBrush(new WideColor(2f, 0f, 0f, 0.5f)), 0, 0, 4, 4);

        var read = surface.ReadWidePixels()!;
        Assert.Equal(2f, read[0], 4);
        Assert.Equal(0.5f, read[3], 4);
        Assert.Null(measuring.CreateOffscreenSurface(4, 4, wide: false)!.ReadWidePixels());
    }

    [Fact]
    public void AWideColorKnowsItsEightBitSelf()
    {
        Assert.Equal(PaintColor.FromArgb(255, 255, 0, 128), new WideColor(1.5f, -2f, 0.5f, 1f).ToPaintColor());
        Assert.Equal(1f, WideColor.FromPaintColor(PaintColor.FromArgb(255, 255, 255, 255)).Red);
    }
}
