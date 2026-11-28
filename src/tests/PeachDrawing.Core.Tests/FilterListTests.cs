using System.Numerics;

namespace PeachDrawing.Core.Tests;

public class FilterListTests
{
    private static IReadOnlyList<LayerEffect> Parse(string text, out bool valid)
    {
        valid = FilterList.TryParse(text, color => color == "red" ? PaintColor.FromArgb(255, 255, 0, 0) : null, PaintColor.Black, 16, out var effects);
        return effects;
    }

    [Fact]
    public void None_IsAValidListOfNoEffects()
    {
        Assert.Empty(Parse("none", out var valid));
        Assert.True(valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("inherit")]
    [InlineData("blur(10)")]
    [InlineData("blur 10px")]
    [InlineData("blur(-1px)")]
    [InlineData("grayscale(-1)")]
    [InlineData("blur(1px) nonsense(2)")]
    [InlineData("hue-rotate(10)")]
    [InlineData("drop-shadow(1px)")]
    [InlineData("drop-shadow(1px 2px 3px 4px)")]
    [InlineData("url(#f)")]
    [InlineData("blur(1px 2px)")]
    [InlineData("opacity(1 2)")]
    [InlineData("hue-rotate(1deg 2deg)")]
    [InlineData("blur(1px")]
    [InlineData("drop-shadow(1px 2px -3px)")]
    [InlineData("drop-shadow(1px 2px nonsense)")]
    [InlineData("sparkle(1)")]
    [InlineData("grayscale(abc)")]
    [InlineData("grayscale(%)")]
    [InlineData("1blur(2px)")]
    public void ATextThatIsNotAFilterList_IsNotValidAndHasNoEffects(string text)
    {
        Assert.Empty(Parse(text, out var valid));
        Assert.False(valid);
    }

    [Fact]
    public void TheFunctionsBecomeEffectsInTheOrderTheyAreWritten()
    {
        var effects = Parse("blur( 5px ) grayscale(50%) drop-shadow(2px 3px 4px red) hue-rotate(0.5turn) opacity(0.25)", out var valid);

        Assert.True(valid);
        Assert.Equal(5, effects.Count);
        Assert.Equal(new BlurEffect(5), effects[0]);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.Grayscale(0.5)), effects[1]);
        Assert.Equal(new DropShadowEffect(2, 3, 4, 4, PaintColor.FromArgb(255, 255, 0, 0)), effects[2]);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.HueRotate(Math.PI)), effects[3]);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.Opacity(0.25)), effects[4]);
    }

    [Fact]
    public void ADropShadow_TakesItsColorBeforeOrAfterTheLengths_AndOtherwiseTheCurrentColor()
    {
        Assert.Equal(new DropShadowEffect(1, 2, 0, 0, PaintColor.FromArgb(255, 255, 0, 0)), Parse("drop-shadow(red 1px 2px)", out _)[0]);
        Assert.Equal(new DropShadowEffect(1, 2, 0, 0, PaintColor.Black), Parse("drop-shadow(1px 2px)", out _)[0]);
    }

    [Fact]
    public void Lengths_ArePixels_AndEmIsTheFontSize()
    {
        Assert.Equal(new BlurEffect(12), Parse("blur(9pt)", out _)[0]);
        Assert.Equal(new BlurEffect(32), Parse("blur(2em)", out _)[0]);
        Assert.Empty(Parse("blur(0)", out var valid));
        Assert.True(valid);
    }

    [Fact]
    public void TheColorMatrices_AreTheOnesTheSpecificationGives()
    {
        // Brightness scales; invert(1) is the negative; grayscale(1) makes a red the luminance in every channel; a saturate of 1 and a hue rotation of 0 change nothing.
        Assert.Equal(new Vector4(0.5f, 0.25f, 0, 1), ColorMatrix.Brightness(0.5).Apply(new Vector4(1, 0.5f, 0, 1)));
        Assert.Equal(new Vector4(0, 1, 1, 1), ColorMatrix.Invert(1).Apply(new Vector4(1, 0, 0, 1)));
        var grey = ColorMatrix.Grayscale(1).Apply(new Vector4(1, 0, 0, 1));
        Assert.Equal(0.2126f, grey.X, 4);
        Assert.Equal(grey.X, grey.Y, 4);
        Assert.Equal(grey.X, grey.Z, 4);
        var same = ColorMatrix.HueRotate(0).Compose(ColorMatrix.Saturate(1)).Apply(new Vector4(0.2f, 0.4f, 0.6f, 0.8f));
        Assert.Equal(0.4f, same.Y, 4);
        Assert.Equal(0.8f, same.W, 4);
    }

    [Fact]
    public void ATableOf20Numbers_ReadsRowsAsOutputs()
    {
        // Row two (output green) is the sum of every input channel's red, green, blue and alpha; the others pass nothing.
        var matrix = ColorMatrix.FromTable([0, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0]);
        var result = matrix.Apply(new Vector4(0.1f, 0.2f, 0.3f, 1));

        Assert.Equal(new Vector4(0, 1.6f, 0, 1), result);
        Assert.Throws<ArgumentException>(() => ColorMatrix.FromTable([1, 2, 3]));
    }

    [Fact]
    public void LuminanceToAlpha_PutsTheLuminanceInTheAlphaAndNothingInTheColor()
    {
        var result = ColorMatrix.LuminanceToAlpha().Apply(new Vector4(1, 0, 0, 1));

        Assert.Equal(new Vector4(0, 0, 0, 0.2125f), result);
    }

    [Fact]
    public void AFunctionWithNoArgument_TakesItsDefault_AndTheColorMayComeFirstInDropShadow()
    {
        var effects = Parse("grayscale() invert(100%) sepia(0.5) drop-shadow(red 1px 2px)", out var valid);

        Assert.True(valid);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.Grayscale(1)), effects[0]);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.Invert(1)), effects[1]);
        Assert.Equal(new ColorMatrixEffect(ColorMatrix.Sepia(0.5)), effects[2]);
        Assert.Equal(new DropShadowEffect(1, 2, 0, 0, PaintColor.FromArgb(255, 255, 0, 0)), effects[3]);
    }
}
