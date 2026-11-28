using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>The floating-point blend and erase that wide canvases use, held to the same Porter-Duff algebra as the eight-bit path.</summary>
public class WideBlendTests
{
    // A backdrop of half-covered red (premultiplied 0.5, 0, 0, 0.5) and a source of three-quarters blue (0, 0, 0.75, 0.75).
    private static float[] Blend(PaintBlendMode mode)
    {
        var dst = new[] { 0.5f, 0f, 0f, 0.5f };
        BlendModes.BlendWide(mode, dst, 0f, 0f, 0.75f, 0.75f);
        return dst;
    }

    [Theory]
    [InlineData(PaintBlendMode.SourceIn, 0f, 0f, 0.375f, 0.375f)]
    [InlineData(PaintBlendMode.SourceOut, 0f, 0f, 0.375f, 0.375f)]
    [InlineData(PaintBlendMode.SourceAtop, 0.125f, 0f, 0.375f, 0.5f)]
    [InlineData(PaintBlendMode.DestinationOver, 0.5f, 0f, 0.375f, 0.875f)]
    [InlineData(PaintBlendMode.DestinationIn, 0.375f, 0f, 0f, 0.375f)]
    [InlineData(PaintBlendMode.DestinationOut, 0.125f, 0f, 0f, 0.125f)]
    [InlineData(PaintBlendMode.DestinationAtop, 0.375f, 0f, 0.375f, 0.75f)]
    [InlineData(PaintBlendMode.Xor, 0.125f, 0f, 0.375f, 0.5f)]
    [InlineData(PaintBlendMode.Copy, 0f, 0f, 0.75f, 0.75f)]
    [InlineData(PaintBlendMode.Clear, 0f, 0f, 0f, 0f)]
    [InlineData(PaintBlendMode.Plus, 0.5f, 0f, 0.75f, 1f)]
    [InlineData(PaintBlendMode.Normal, 0.125f, 0f, 0.75f, 0.875f)]
    public void ThePorterDuffOperators_FollowTheirAlgebra(PaintBlendMode mode, float r, float g, float b, float a)
    {
        var result = Blend(mode);
        Assert.Equal([r, g, b, a], result, new FloatTolerance());
    }

    [Fact]
    public void ASourceWithNoAlpha_LeavesTheBackdropAloneExceptForTheOperatorsThatErase()
    {
        var dst = new[] { 0.5f, 0f, 0f, 0.5f };
        BlendModes.BlendWide(PaintBlendMode.Multiply, dst, 0f, 0f, 0f, 0f);
        Assert.Equal([0.5f, 0f, 0f, 0.5f], dst);
    }

    [Theory]
    [InlineData(PaintBlendMode.Multiply)]
    [InlineData(PaintBlendMode.Screen)]
    [InlineData(PaintBlendMode.Difference)]
    [InlineData(PaintBlendMode.Luminosity)]
    public void TheSeparableAndNonSeparableBlends_KeepTheResultAValidPremultipliedColor(PaintBlendMode mode)
    {
        var result = Blend(mode);
        Assert.InRange(result[3], 0.874f, 0.876f);
        Assert.All(result.Take(3), c => Assert.InRange(c, 0f, result[3] + 0.001f));
    }

    [Fact]
    public void ABlendOverAnEmptyBackdrop_IsTheSourceItself()
    {
        var dst = new float[4];
        BlendModes.BlendWide(PaintBlendMode.Multiply, dst, 0f, 0f, 0.75f, 0.75f);
        Assert.Equal([0f, 0f, 0.75f, 0.75f], dst, new FloatTolerance());
    }

    [Fact]
    public void Erase_OnAWideCanvas_RemovesTheFloatingPointInkAsWell()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(8, 8, 96, wide: true);
        canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 8, 8);
        canvas.EraseRectangle(new Rect(0, 0, 4, 8));

        var wide = canvas.Surface.WidePixels;
        Assert.Equal(0f, wide[(0 * 8 + 1) * 4 + 3]);
        Assert.Equal(0f, wide[(0 * 8 + 1) * 4]);
        Assert.Equal(1f, wide[(0 * 8 + 6) * 4 + 3], 3);
    }

    private sealed class FloatTolerance : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) < 0.002f;

        public int GetHashCode(float value) => 0;
    }
}
