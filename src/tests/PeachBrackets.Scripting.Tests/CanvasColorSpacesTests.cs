using PeachBrackets.Scripting.Internal.Canvas;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The conversions between the canvas's color spaces on bytes and floats, and the reading of <c>color()</c>.</summary>
public class CanvasColorSpacesTests
{
    [Fact]
    public void ConvertBytes_MovesStraightPixelsBetweenSpaces_AndLeavesAlphaAlone()
    {
        var pixels = new byte[] { 255, 0, 0, 200, 10, 20, 30, 0 };
        var same = (byte[])pixels.Clone();
        CanvasColorSpaces.ConvertBytes(same, "srgb", "srgb");
        Assert.Equal(pixels, same);

        CanvasColorSpaces.ConvertBytes(pixels, "srgb", "display-p3");
        Assert.Equal(200, pixels[3]);
        Assert.True(pixels[0] < 255 && pixels[0] > 200, "sRGB red is inside P3, so its P3 red is lower");
        Assert.True(pixels[1] > 0 && pixels[2] > 0);

        CanvasColorSpaces.ConvertBytes(pixels, "display-p3", "srgb");
        Assert.InRange(pixels[0], 253, 255);
        Assert.InRange(pixels[1], 0, 2);
    }

    [Fact]
    public void ConvertPremultipliedBytes_ConvertsEachPixelStraight_AndSkipsTheTransparentOnes()
    {
        var pixels = new byte[] { 128, 0, 0, 128, 0, 0, 0, 0, 40, 50, 60, 255 };
        var original = (byte[])pixels.Clone();
        CanvasColorSpaces.ConvertPremultipliedBytes(pixels, "srgb", "srgb");
        Assert.Equal(original, pixels);

        CanvasColorSpaces.ConvertPremultipliedBytes(pixels, "srgb", "display-p3");
        Assert.Equal(128, pixels[3]);
        Assert.InRange(pixels[0], 100, 127);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, pixels[4..8]);
        Assert.NotEqual(original[8..11], pixels[8..11]);
    }

    [Fact]
    public void ConvertFloats_KeepsTheValuesOutsideTheGamutUnlessAskedToClip()
    {
        var wide = new float[] { 1f, 0f, 0f, 1f };
        CanvasColorSpaces.ConvertFloats(wide, "display-p3", "srgb", clip: false);
        Assert.True(wide[0] > 1f || wide[1] < 0f, "pure P3 red lies outside sRGB");

        var clipped = new float[] { 1f, 0f, 0f, 1f };
        CanvasColorSpaces.ConvertFloats(clipped, "display-p3", "srgb", clip: true);
        Assert.All(clipped, v => Assert.InRange(v, 0f, 1f));
    }

    [Theory]
    [InlineData("color(srgb 1 0 0)", true, 1.0, 0.0, 0.0, 1.0)]
    [InlineData("color(srgb 50% 0 0 / 0.5)", true, 0.5, 0.0, 0.0, 0.5)]
    [InlineData("color(srgb-linear 0 0 0 / 2)", true, 0.0, 0.0, 0.0, 1.0)]
    [InlineData("color(display-p3 1 1 1)", true, 1.0, 1.0, 1.0, 1.0)]
    [InlineData("color(rec2020 1 0 0)", false, 0, 0, 0, 0)]
    [InlineData("color(srgb 1 0)", false, 0, 0, 0, 0)]
    [InlineData("color(srgb 1 x 0)", false, 0, 0, 0, 0)]
    [InlineData("color(srgb 1 0 0 / x)", false, 0, 0, 0, 0)]
    [InlineData("rgb(1 0 0)", false, 0, 0, 0, 0)]
    public void TryParseExtended_ReadsTheSpacesACanvasKnows(string text, bool valid, double r, double g, double b, double a)
    {
        Assert.Equal(valid, CanvasColorSpaces.TryParseExtended(text, out var color));
        if (valid)
        {
            Assert.Equal(r, color.R, 2);
            Assert.Equal(g, color.G, 2);
            Assert.Equal(b, color.B, 2);
            Assert.Equal(a, color.A, 2);
        }
    }
}
