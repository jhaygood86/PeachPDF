using PeachDrawing.Core;
using SkiaSharp;

namespace PeachDrawing.Skia.Tests;

/// <summary>An image made on the GPU (<see cref="SkiaRenderContext.CreateGpuImage"/>): imported on a GPU context, pixels otherwise.</summary>
public class GpuImageTests
{
    private static PixelBuffer Solid(byte r, byte g, byte b)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }

        return new PixelBuffer(4, 4, pixels);
    }

    [Fact]
    public void WithoutAGpuContext_TheImportIsNotCalledAndThePixelsAreDrawn()
    {
        using var scene = Scene.Create(4, 4);
        var imported = 0;
        using var image = scene.Context.CreateGpuImage(4, 4, _ => { imported++; return null; }, () => Solid(0, 200, 0));

        scene.Canvas.DrawImage(image, new Rect(0, 0, 4, 4));
        var pixel = scene.Px(1, 1);

        Assert.Equal(0, imported);
        Assert.Equal(0, pixel.R);
        Assert.Equal(200, pixel.G);
    }

    [Fact]
    public void GetPixels_ReadsOnceAndKeepsWhatItRead()
    {
        using var scene = Scene.Create(4, 4);
        var reads = 0;
        using var image = scene.Context.CreateGpuImage(4, 4, _ => null, () => { reads++; return Solid(9, 8, 7); });

        Assert.Equal(9, image.GetPixels()!.Value.PremultipliedRgba.Span[0]);
        Assert.Equal(8, image.GetPixels()!.Value.PremultipliedRgba.Span[1]);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void AnImageWithNeitherATextureNorPixels_HasNoPixels()
    {
        using var scene = Scene.Create(4, 4);
        using var image = scene.Context.CreateGpuImage(4, 4, _ => null);

        Assert.Null(image.GetPixels());
    }

    [Fact]
    public void ZeroSizeAndMissingImportAreRefused()
    {
        using var scene = Scene.Create(4, 4);

        Assert.Throws<ArgumentNullException>(() => scene.Context.CreateGpuImage(4, 4, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Context.CreateGpuImage(0, 4, _ => null));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Context.CreateGpuImage(4, 0, _ => null));
    }
}
