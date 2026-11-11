using System.Security.Cryptography;
using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests;

/// <summary>
/// Pins the exact bytes <see cref="FilterOps.ConvertColorSpace"/> and <see cref="FilterOps.Arithmetic"/> write for fixed inputs - fully transparent,
/// opaque, and every partial alpha, with color above alpha as well as valid premultiplied color - on surfaces whose pixel counts are not
/// multiples of the vector width. The hashes were recorded from the scalar loops that preceded the vector rewrite. A hash cannot say what
/// changed: compare the outputs pixel by pixel.
/// </summary>
public class FilterOpsGoldenTests
{
    /// <summary>Every alpha from 0 to 255 appears, with color that is premultiplied-valid for most pixels and above alpha for some.</summary>
    private static RasterSurface Surface(int width, int height, int salt)
    {
        var surface = new RasterSurface(width, height, 0, 0, 1, 1);
        var n = 0;
        for (var y = 0; y < height; y++)
        {
            var row = surface.Row(y);
            for (var x = 0; x < width; x++, n++)
            {
                var alpha = (n * 7 + salt * 31) % 256;
                var limit = n % 11 == 0 ? 255 : alpha;
                row[x * 4] = (byte)((n * 13 + salt) % (limit + 1));
                row[x * 4 + 1] = (byte)((n * 29 + salt * 3) % (limit + 1));
                row[x * 4 + 2] = (byte)((n * 53 + salt * 5) % (limit + 1));
                row[x * 4 + 3] = (byte)alpha;
            }
        }

        return surface;
    }

    private static string Hash(RasterSurface surface)
    {
        var bytes = new byte[surface.Width * surface.Height * 4];
        for (var y = 0; y < surface.Height; y++)
            surface.Row(y)[..(surface.Width * 4)].CopyTo(bytes.AsSpan(y * surface.Width * 4));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    [Theory]
    [InlineData(true, 67, 31, "COLORSPACE_TO_LINEAR")]
    [InlineData(false, 67, 31, "COLORSPACE_TO_SRGB")]
    [InlineData(true, 3, 2, "COLORSPACE_TO_LINEAR_TINY")]
    [InlineData(false, 256, 5, "COLORSPACE_TO_SRGB_EVERY_ALPHA")]
    public void ConvertColorSpace_IsByteIdentical(bool toLinear, int width, int height, string name)
    {
        using var surface = Surface(width, height, 5);

        FilterOps.ConvertColorSpace(surface, toLinear);

        FilterOpsGolden.Check(name, Hash(surface));
    }

    [Fact]
    public void ConvertColorSpace_RoundTrip_IsByteIdentical()
    {
        using var surface = Surface(131, 17, 9);

        FilterOps.ConvertColorSpace(surface, toLinear: true);
        FilterOps.ConvertColorSpace(surface, toLinear: false);

        FilterOpsGolden.Check("COLORSPACE_ROUND_TRIP", Hash(surface));
    }

    [Theory]
    [InlineData(0.0, 0.5, 0.5, 0.0, 67, 31, "ARITHMETIC_AVERAGE")]
    [InlineData(1.0, 0.0, 0.0, 0.0, 67, 31, "ARITHMETIC_MULTIPLY")]
    [InlineData(0.3, -0.4, 1.2, 0.05, 67, 31, "ARITHMETIC_MIXED_SIGNS")]
    [InlineData(-1.0, 1.5, 1.5, -0.5, 129, 7, "ARITHMETIC_CLAMPING")]
    [InlineData(0.0, 1.0, 1.0, 0.0, 3, 1, "ARITHMETIC_TINY")]
    public void Arithmetic_IsByteIdentical(double k1, double k2, double k3, double k4, int width, int height, string name)
    {
        using var a = Surface(width, height, 1);
        using var b = Surface(width, height, 4);
        using var result = new RasterSurface(width, height, 0, 0, 1, 1);

        FilterOps.Arithmetic(a, b, result, k1, k2, k3, k4);

        FilterOpsGolden.Check(name, Hash(result));
    }
}

/// <summary>The recorded hashes of <see cref="FilterOpsGoldenTests"/>, by case name.</summary>
internal static class FilterOpsGolden
{
    /// <summary>Fails unless <paramref name="actual"/> is the hash recorded for <paramref name="name"/>; the message carries the whole hash, to record a new case.</summary>
    public static void Check(string name, string actual) =>
        Assert.True(Hashes.TryGetValue(name, out var hash) && hash == actual, $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["ARITHMETIC_AVERAGE"] = "BA0DC4B1A4EACA9C19ADD5DF98CEF82EA0E9E6897D47A687C0B6CCE467EC8EE0",
        ["ARITHMETIC_CLAMPING"] = "D1C08524DFB9D43D85D727FB3CF86D472A567E9E0F1235477851A134FAA0E8C2",
        ["ARITHMETIC_MIXED_SIGNS"] = "AAEE0C08F9580DABCCE119FFABDED9E1C5AC516F5324ACE8AF8402D89C77E2F0",
        ["ARITHMETIC_MULTIPLY"] = "2E721B5BBCAA118323152E252A29ABDC239190094A7511CF18C5075C761BA7F6",
        ["ARITHMETIC_TINY"] = "E3BB12F516E3DFC14C2DF4FA779C5EA005A54008DCCE2F898BB1AF712A659521",
        ["COLORSPACE_ROUND_TRIP"] = "96846056BDFAD52B5571416347D9EC6FC0C38F659190BD38FD745306B77EFF8F",
        ["COLORSPACE_TO_LINEAR"] = "21A2C681EAAD7459123FE4992C0A2A8DCD77F8FBE583B11B9EE5D6D2419FAAC7",
        ["COLORSPACE_TO_LINEAR_TINY"] = "E33D9056A490B03CDA85CFD05C1649D8009BEAE7D3302E058344AB2F07FE5463",
        ["COLORSPACE_TO_SRGB"] = "25104969FCB67547C618BB4C4E042C888BFACF54E6F176C8B865F3A56CFC629D",
        ["COLORSPACE_TO_SRGB_EVERY_ALPHA"] = "7961141734FDFCCD4B7AD4A6D16BDC503401BA69E259D5A7793BC61307C1B4B7",
    };
}
