using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachDrawing.PDF.Tests.Engine;

/// <summary>
/// A raster surface embedded as the streams of a PNG pass-through embed (see <see cref="RasterPlaneCodec"/>) has to carry exactly the pixels the PNG
/// route carried: the straight (unpremultiplied) color, and the alpha as its own plane when the surface is not opaque.
/// </summary>
public class RasterPlaneCodecTests
{
    private static RasterSurface RandomSurface(Random random, int width, int height, bool opaque)
    {
        var surface = new RasterSurface(width, height, 0, 0, 1, 1);
        for (var y = 0; y < height; y++)
        {
            var row = surface.Row(y);
            for (var x = 0; x < width; x++)
            {
                var kind = random.Next(5);
                var a = opaque ? 255 : kind switch { 0 => 0, 1 => 255, _ => random.Next(256) };
                row[x * 4] = (byte)random.Next(a + 1);
                row[x * 4 + 1] = (byte)random.Next(a + 1);
                row[x * 4 + 2] = (byte)random.Next(a + 1);
                row[x * 4 + 3] = (byte)a;
            }
        }

        return surface;
    }

    private static byte[] Straight(RasterSurface surface, bool opaque)
    {
        var bytes = new byte[surface.Width * surface.Height * 4];
        for (var y = 0; y < surface.Height; y++)
            surface.Row(y)[..(surface.Width * 4)].CopyTo(bytes.AsSpan(y * surface.Width * 4));

        var straight = new byte[bytes.Length];
        if (opaque)
            bytes.CopyTo(straight, 0);
        else
            PixelMath.Unpremultiply(bytes, straight);
        return straight;
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 17, false)]
    [InlineData(17, 1, false)]
    [InlineData(31, 29, false)]
    [InlineData(64, 40, false)]
    [InlineData(31, 29, true)]
    [InlineData(1, 5, true)]
    public void TheStreamsCarryTheStraightPixels(int width, int height, bool opaque)
    {
        var random = new Random(width * 1000 + height);
        using var surface = RandomSurface(random, width, height, opaque);

        var data = RasterPlaneCodec.Encode(surface.Pixels, width, height, opaque);

        Assert.Equal(PngPassthroughColorSpace.Rgb, data.ColorSpace);
        Assert.Equal(8, data.BitDepth);
        Assert.Equal(opaque, data.AlphaIdatData is null);

        var expected = Straight(surface, opaque);
        var actual = RasterPlaneCodec.ToRgba(data, width, height);
        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{width}x{height}, opaque {opaque}");
    }

    [Fact]
    public void TheFallbackSourceGivesBackTheSamePixels()
    {
        using var surface = RandomSurface(new Random(5), 23, 19, opaque: false);
        var data = RasterPlaneCodec.Encode(surface.Pixels, 23, 19, opaque: false);
        var source = new RasterPassthroughImageSource("*test", 23, 19, data);

        Assert.True(source.TryGetRgba(out var width, out var height, out var rgba));

        Assert.Equal((23, 19), (width, height));
        Assert.True(Straight(surface, opaque: false).AsSpan().SequenceEqual(rgba));
    }

    [Fact]
    public void TheRowFiltersAreReversedExactly()
    {
        // A surface of smooth ramps (where Sub and Up both win rows) next to noise (where the choice flips between rows).
        var width = 40;
        var height = 30;
        using var surface = new RasterSurface(width, height, 0, 0, 1, 1);
        var random = new Random(9);
        for (var y = 0; y < height; y++)
        {
            var row = surface.Row(y);
            for (var x = 0; x < width; x++)
            {
                var value = y < 15 ? (byte)(x * 5 + y) : (byte)random.Next(256);
                row[x * 4] = value;
                row[x * 4 + 1] = (byte)(255 - value);
                row[x * 4 + 2] = (byte)(value / 2);
                row[x * 4 + 3] = 255;
            }
        }

        var data = RasterPlaneCodec.Encode(surface.Pixels, width, height, opaque: true);

        Assert.True(Straight(surface, opaque: true).AsSpan().SequenceEqual(RasterPlaneCodec.ToRgba(data, width, height)));
    }

    [Fact]
    public void HardEdgedTransparencyIsLeftToThePngRoute()
    {
        using var surface = new RasterSurface(8, 8, 0, 0, 1, 1);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var row = surface.Row(y);
                var on = (x + y) % 2 == 0;
                row[x * 4] = on ? (byte)200 : (byte)0;
                row[x * 4 + 1] = 0;
                row[x * 4 + 2] = 0;
                row[x * 4 + 3] = on ? (byte)255 : (byte)0;
            }
        }

        Assert.True(RasterPlaneCodec.HasOnlyBinaryAlpha(surface.Pixels));

        surface.Row(3)[3] = 128; // one partly transparent pixel
        Assert.False(RasterPlaneCodec.HasOnlyBinaryAlpha(surface.Pixels));
    }

    [Fact]
    public void AnOpaqueSurfaceOfFewColorsIsLeftToThePngRoute()
    {
        using var flat = new RasterSurface(40, 40, 0, 0, 1, 1);
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var row = flat.Row(y);
                row[x * 4] = (byte)(x < 20 ? 10 : 200);
                row[x * 4 + 1] = 30;
                row[x * 4 + 2] = (byte)(y < 20 ? 5 : 90);
                row[x * 4 + 3] = 255;
            }
        }

        Assert.True(RasterPlaneCodec.HasFewColors(flat.Pixels, 256));

        // A ramp has a color for every pixel of a row, so far more than 256 over the surface.
        using var ramp = new RasterSurface(64, 64, 0, 0, 1, 1);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var row = ramp.Row(y);
                row[x * 4] = (byte)(x * 4);
                row[x * 4 + 1] = (byte)(y * 4);
                row[x * 4 + 2] = 50;
                row[x * 4 + 3] = 255;
            }
        }

        Assert.False(RasterPlaneCodec.HasFewColors(ramp.Pixels, 256));
        Assert.True(RasterPlaneCodec.HasFewColors(ramp.Pixels, 5000));
    }
}
