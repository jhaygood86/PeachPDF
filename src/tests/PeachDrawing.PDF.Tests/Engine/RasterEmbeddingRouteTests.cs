using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachDrawing.PDF.Tests.Engine;

/// <summary>
/// Which way a raster surface is embedded: the streams built straight from its pixels (RGB, and a soft-mask alpha plane when it has partial transparency),
/// or the PNG route, which gives a color key for hard-edged transparency and an <c>/Indexed</c> palette for an opaque surface of few colors.
/// </summary>
public class RasterEmbeddingRouteTests
{
    private static RasterSurface Surface(Func<int, int, (byte R, byte G, byte B, byte A)> pixel, int size = 64)
    {
        var surface = new RasterSurface(size, size, 0, 0, 1, 1);
        for (var y = 0; y < size; y++)
        {
            var row = surface.Row(y);
            for (var x = 0; x < size; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                row[x * 4] = r;
                row[x * 4 + 1] = g;
                row[x * 4 + 2] = b;
                row[x * 4 + 3] = a;
            }
        }

        return surface;
    }

    private static PngPassthroughData Embedded(RasterSurface surface) => RasterEmbedding.ToXImage(surface).PngPassthrough!.Value;

    [Fact]
    public void ABlurredShadowIsBuiltFromItsPixels_AsRgbWithAnAlphaPlane()
    {
        // Black with every alpha: few distinct colors, but partial transparency.
        using var surface = Surface((x, y) => { var a = (byte)((x * 4) & 255); return (0, 0, 0, a); });

        var data = Embedded(surface);

        Assert.Equal(PngPassthroughColorSpace.Rgb, data.ColorSpace);
        Assert.NotNull(data.AlphaIdatData);
        Assert.Null(data.ColorKeyMask);
    }

    [Fact]
    public void AnOpaqueRichSurfaceIsBuiltFromItsPixels_AsRgbWithNoAlpha()
    {
        using var surface = Surface((x, y) => ((byte)(x * 4), (byte)(y * 4), 50, 255));

        var data = Embedded(surface);

        Assert.Equal(PngPassthroughColorSpace.Rgb, data.ColorSpace);
        Assert.Null(data.AlphaIdatData);
    }

    [Fact]
    public void AnOpaqueSurfaceOfFewColorsKeepsThePngRoute_AsAPalette()
    {
        using var surface = Surface((x, y) => ((byte)(x < 32 ? 10 : 200), 30, (byte)(y < 32 ? 5 : 90), 255));

        var data = Embedded(surface);

        Assert.Equal(PngPassthroughColorSpace.Indexed, data.ColorSpace);
    }

    [Fact]
    public void HardEdgedTransparencyKeepsThePngRoute_AsAColorKeyNotASoftMask()
    {
        using var surface = Surface((x, y) => ((x + y) % 2 == 0) ? ((byte)200, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

        var data = Embedded(surface);

        Assert.Null(data.AlphaIdatData);
        Assert.NotNull(data.ColorKeyMask);
    }

    [Fact]
    public void TheEmbeddedPixelsAreTheSurfacesWhicheverRouteIsTaken()
    {
        using var rich = Surface((x, y) => { var a = (byte)(40 + (x * 3 + y) % 200); return ((byte)(a / 2), (byte)(a / 3), (byte)(a / 4), a); });

        var data = Embedded(rich);
        var rgba = RasterPlaneCodec.ToRgba(data, 64, 64);

        var expected = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            rich.Row(y)[..(64 * 4)].CopyTo(expected.AsSpan(y * 64 * 4));
        var straight = new byte[expected.Length];
        PixelMath.Unpremultiply(expected, straight);
        Assert.True(straight.AsSpan().SequenceEqual(rgba));
    }
}
