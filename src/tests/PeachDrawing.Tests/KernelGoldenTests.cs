using System.Numerics;
using System.Security.Cryptography;
using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests;

/// <summary>
/// Pins the exact bytes three kernels write - <see cref="Turbulence"/>, <see cref="ColorMatrixFilter"/> and the masked-image combine behind
/// <see cref="RasterCanvas.DrawImageMasked"/> / <see cref="RasterCanvas.DrawImageAlphaMasked"/> - for fixed inputs, as a SHA-256 of the output.
/// They were written scalar and are being rewritten with vector code; the rewrite has to be byte-identical, so these fail on the first differing
/// byte of any of the cases (odd widths are used on purpose, so the vector body and its scalar tail are both covered). A hash cannot say what
/// changed: when one fails, compare the old and new output pixel by pixel. Update a constant only for a change that is meant to alter the output.
/// </summary>
public class KernelGoldenTests
{
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>A premultiplied RGBA surface of fixed content: smooth gradients, hard edges and fully transparent pixels.</summary>
    private static RasterSurface Pattern(int width, int height)
    {
        var surface = new RasterSurface(width, height, 0, 0, 1, 1);
        for (var y = 0; y < height; y++)
        {
            var row = surface.Row(y);
            for (var x = 0; x < width; x++)
            {
                var inside = (x / 5 + y / 3) % 4 != 0;
                var a = inside ? 40 + (x * 7 + y * 13) % 216 : 0;
                row[x * 4 + 0] = (byte)(a * (x % 17) / 16);
                row[x * 4 + 1] = (byte)(a * (y % 11) / 10);
                row[x * 4 + 2] = (byte)(a * ((x + y) % 23) / 22);
                row[x * 4 + 3] = (byte)a;
            }
        }

        return surface;
    }

    private static string Hash(RasterSurface surface)
    {
        var bytes = new byte[surface.Width * surface.Height * 4];
        for (var y = 0; y < surface.Height; y++)
            surface.Row(y)[..(surface.Width * 4)].CopyTo(bytes.AsSpan(y * surface.Width * 4));
        return Hash(bytes);
    }

    [Theory]
    [InlineData(67, 29, 0.05, 0.08, 3, true, false, "TURBULENCE_FRACTAL")]
    [InlineData(67, 29, 0.05, 0.08, 3, false, false, "TURBULENCE_NOISE")]
    [InlineData(67, 29, 0.11, 0.07, 2, true, true, "TURBULENCE_FRACTAL_STITCHED")]
    [InlineData(31, 17, 0.2, 0.2, 1, false, true, "TURBULENCE_NOISE_STITCHED")]
    public void Turbulence_WritesTheGoldenBytes(int width, int height, double fx, double fy, int octaves, bool fractal, bool stitch, string expected)
    {
        using var surface = new RasterSurface(width, height, 0, 0, 1, 1);

        new Turbulence(seed: 7).Render(surface, 1, 1, fx, fy, octaves, fractal, stitch, 0, 0, width, height);

        Golden.Check(expected, Hash(surface));
    }

    private static readonly ColorMatrix Grayscale = new(
        new Matrix4x4(0.2126f, 0.2126f, 0.2126f, 0, 0.7152f, 0.7152f, 0.7152f, 0, 0.0722f, 0.0722f, 0.0722f, 0, 0, 0, 0, 1),
        Vector4.Zero);

    private static readonly ColorMatrix SepiaWithOffset = new(
        new Matrix4x4(0.393f, 0.349f, 0.272f, 0, 0.769f, 0.686f, 0.534f, 0, 0.189f, 0.168f, 0.131f, 0, 0, 0, 0, 0.9f),
        new Vector4(0.02f, -0.01f, 0.03f, 0.05f));

    private static readonly ColorMatrix ChannelSwap = new(
        new Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1),
        Vector4.Zero);

    public static TheoryData<string, int> ColorMatrices => new()
    {
        { "grayscale", 67 },
        { "sepia", 67 },
        { "swap", 64 },
        { "grayscale", 3 },
    };

    [Theory]
    [MemberData(nameof(ColorMatrices))]
    public void ColorMatrixFilter_ApplyInPlace_WritesTheGoldenBytes(string name, int width)
    {
        using var surface = Pattern(width, 11);

        ColorMatrixFilter.ApplyInPlace(surface, name switch { "grayscale" => Grayscale, "sepia" => SepiaWithOffset, _ => ChannelSwap });

        Golden.Check("COLORMATRIX_INPLACE_" + name + "_" + width, Hash(surface));
    }

    [Fact]
    public void ColorMatrixFilter_ApplyInPlace_WithAChainOfMatrices_WritesTheGoldenBytes()
    {
        using var surface = Pattern(53, 9);

        ColorMatrixFilter.ApplyInPlace(surface, [SepiaWithOffset, Grayscale]);

        Golden.Check("COLORMATRIX_CHAIN", Hash(surface));
    }

    [Fact]
    public void ColorMatrixFilter_Apply_ToASeparateBuffer_WritesTheGoldenBytes()
    {
        using var source = Pattern(61, 7);
        var bytes = new byte[61 * 7 * 4];
        for (var y = 0; y < 7; y++)
            source.Row(y)[..(61 * 4)].CopyTo(bytes.AsSpan(y * 61 * 4));
        var destination = new byte[bytes.Length];

        ColorMatrixFilter.Apply(bytes, destination, SepiaWithOffset);

        Golden.Check("COLORMATRIX_APPLY", Hash(destination));
    }

    /// <summary>A tile of the canvas filled with a fixed pattern (a width and height that are not multiples of anything convenient).</summary>
    private static Image PatternImage(RasterCanvas canvas, int width, int height)
    {
        var (graphics, image) = canvas.CreateTile(width, height)!.Value;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = PaintColor.FromArgb(60 + (x * 11 + y * 5) % 196, (x * 29) % 256, (y * 37) % 256, (x * y * 3) % 256);
                graphics.DrawRectangle(graphics.GetSolidBrush(color), x, y, 1, 1);
            }
        }

        graphics.Dispose();
        return image;
    }

    private static string Masked(Action<RasterCanvas, Image, Image> draw, int maskWidth, int maskHeight)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(40, 30);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 250, 250, 240)), 0, 0, 40, 30);
        var image = PatternImage(canvas, 23, 17);
        var mask = PatternImage(canvas, maskWidth, maskHeight);

        draw(canvas, image, mask);

        return Hash(canvas.ToPixelBuffer().PremultipliedRgba.Span);
    }

    [Theory]
    [InlineData(23, 17, "MASKED_LUMINANCE_SAME")]
    [InlineData(5, 7, "MASKED_LUMINANCE_SMALLER")]
    [InlineData(41, 31, "MASKED_LUMINANCE_LARGER")]
    public void DrawImageMasked_WritesTheGoldenBytes(int maskWidth, int maskHeight, string expected) =>
        Golden.Check(expected, Masked((c, image, mask) => c.DrawImageMasked(image, mask, new Rect(3, 2, 23, 17)), maskWidth, maskHeight));

    [Theory]
    [InlineData(23, 17, false, "MASKED_ALPHA_SAME")]
    [InlineData(5, 7, false, "MASKED_ALPHA_SMALLER")]
    [InlineData(41, 31, true, "MASKED_ALPHA_LARGER_INVERTED")]
    [InlineData(23, 17, true, "MASKED_ALPHA_SAME_INVERTED")]
    public void DrawImageAlphaMasked_WritesTheGoldenBytes(int maskWidth, int maskHeight, bool invert, string expected) =>
        Golden.Check(expected, Masked((c, image, mask) => c.DrawImageAlphaMasked(image, mask, new Rect(3, 2, 23, 17), invert), maskWidth, maskHeight));
}

/// <summary>The recorded hashes of <see cref="KernelGoldenTests"/>, by case name.</summary>
internal static class Golden
{
    /// <summary>Fails unless <paramref name="actual"/> is the hash recorded for <paramref name="name"/>; the message carries the whole hash, to record a new case.</summary>
    public static void Check(string name, string actual) =>
        Assert.True(Hashes.TryGetValue(name, out var hash) && hash == actual, $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["COLORMATRIX_APPLY"] = "DE557152C666C892B7C0ACD37DC2F2E2FC5C0B8E73ADC52CD2EE127B08ABD2F5",
        ["COLORMATRIX_CHAIN"] = "72CB1F56A2CC3B36D21D08B66325C394A5FEA8950CD202BE0320A2C0AFEB0605",
        ["COLORMATRIX_INPLACE_grayscale_3"] = "CBE403FB6E39E73DB6F84DB21B5BA129501E44B6B44205182379681FBE2C139C",
        ["COLORMATRIX_INPLACE_grayscale_67"] = "CFD7E5DF075FB486D8BFBE3998DB092234EFD15F0AB33873447123437CD776B5",
        ["COLORMATRIX_INPLACE_sepia_67"] = "313BD89F9C11C1963137855B1A3401437B5AE2F3A4B4C9AF49D3A997DA498C28",
        ["COLORMATRIX_INPLACE_swap_64"] = "2422C8A48A867506B98CDC22E2E9DF9FD8AFD4CE933DF821159A2780AA4A3B30",
        ["MASKED_ALPHA_LARGER_INVERTED"] = "1F371DE70704D7D0FC88DDE600D5117523C52942CAD7BF2E0E1B591B50224AC9",
        ["MASKED_ALPHA_SAME"] = "D945F93F2F9FC5DB12AD7EC4AD4280EA6FFA2D1E9A562233193DA1FBD68CC821",
        ["MASKED_ALPHA_SAME_INVERTED"] = "E4D4E6E8040CA2C4E1EB838157FD911410B0DFD8B656EB74AB3435E0B8F07970",
        ["MASKED_ALPHA_SMALLER"] = "9CCC52B76A080933CBE77DC8AC010AA73E561A6C578051F4814C085BF4D3BC63",
        ["MASKED_LUMINANCE_LARGER"] = "765CEBA51828768029AD29F7CA47F781BA8A2ECEE928A7DEDBB8FAC31BD55251",
        ["MASKED_LUMINANCE_SAME"] = "6C190FC4670C562452CDE4975EAECC4F5D662665C2B1ADC0D984AA0702B3C036",
        ["MASKED_LUMINANCE_SMALLER"] = "26A66554CCB513D36B5B321058AFAEB111C234CC20A4C15FFD2C7BC8D89F826A",
        ["TURBULENCE_FRACTAL"] = "D36A7D1E9006CB078A82BFB4131147E3958065E10E01006A2700AB035B39593C",
        ["TURBULENCE_FRACTAL_STITCHED"] = "5AECF6201895373DD09FE920B48E0FC6D2A9CC7119693C51C0F94E199A418417",
        ["TURBULENCE_NOISE"] = "768EF8B68EEBEF84150554723D4118D8AF348F99A478A8DF6914EC8C8476706B",
        ["TURBULENCE_NOISE_STITCHED"] = "BDF4C13504F991100C8105B0526E8A248EEBD4068585637FDBF2E6E1BCF11442",
    };
}
