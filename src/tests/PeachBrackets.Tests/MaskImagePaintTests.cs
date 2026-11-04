using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary><c>mask-image</c> (CSS Masking 1 section 6) painted through the bitmap canvas: the element shows through where its mask is opaque.</summary>
public class MaskImagePaintTests
{
    private const string Styles = "<style>@page { margin: 0 } div { position: absolute; left: 10pt; top: 10pt; width: 100pt; height: 20pt; background: #00f }</style>";

    private static async Task<RasterCanvas> PaintAsync(string style)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap($"<div style='{style}'></div>", Styles), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static (byte R, byte G, byte B, byte A) At(RasterCanvas canvas, double fraction) =>
        TestDocument.Pixel(canvas, 10 + (int)(fraction * 100), 20);

    [Fact]
    public async Task AGradientFadingToTransparentFadesTheElement()
    {
        var canvas = await PaintAsync("mask-image: linear-gradient(to right, #000, transparent)");

        var left = At(canvas, 0.03);
        var middle = At(canvas, 0.5);
        var right = At(canvas, 0.97);

        Assert.True(left.R < 60 && left.B > 240, $"left stays blue, got {left}");
        Assert.InRange(middle.R, 90, 170);
        Assert.True(middle.B > 240, $"the middle is blue washed toward white, got {middle}");
        Assert.True(right.R > 225 && right.G > 225, $"right is nearly the white page, got {right}");
    }

    [Fact]
    public async Task WithNoMask_TheElementIsSolid()
    {
        var canvas = await PaintAsync("mask-image: none");

        Assert.True(At(canvas, 0.9).R < 20);
    }

    [Fact]
    public async Task LuminanceMode_ShowsTheElementWhereTheMaskIsBright()
    {
        var canvas = await PaintAsync("mask-image: linear-gradient(to right, #fff, #000); mask-mode: luminance");

        Assert.True(At(canvas, 0.03).R < 60, "bright on the left");
        Assert.True(At(canvas, 0.97).R > 225, "dark on the right hides the element");
    }

    [Fact]
    public async Task AlphaIsTheDefault_SoTheMaskColorDoesNotMatter()
    {
        var black = await PaintAsync("mask-image: linear-gradient(to right, #000, #000)");
        var white = await PaintAsync("mask-image: linear-gradient(to right, #fff, #fff)");

        Assert.True(At(black, 0.5).R < 20);
        Assert.True(At(white, 0.5).R < 20);
    }

    [Fact]
    public async Task MaskSizeAndRepeatTileTheMask()
    {
        // A 20pt tile that is opaque in its left half and transparent in its right half, repeated across the box.
        var canvas = await PaintAsync(
            "mask-image: linear-gradient(to right, #000 50%, transparent 50%); mask-size: 20pt 20pt; mask-repeat: repeat-x");

        Assert.True(At(canvas, 0.05).R < 60, $"inside an opaque half: {string.Join(' ', Enumerable.Range(0, 20).Select(i => At(canvas, i / 20.0).R))}");
        Assert.True(At(canvas, 0.15).R > 225, "inside a transparent half");
        Assert.True(At(canvas, 0.25).R < 60, "the next tile's opaque half");
    }

    [Fact]
    public async Task TheWebkitPrefixedNamesAreAccepted()
    {
        var canvas = await PaintAsync("-webkit-mask-image: linear-gradient(to right, #000, transparent)");

        Assert.True(At(canvas, 0.97).R > 225, "the prefixed property masks too");
    }

    [Fact]
    public async Task OpacityAppliesToTheMaskedResult()
    {
        var canvas = await PaintAsync("opacity: .5; mask-image: linear-gradient(to right, #000, #000)");

        var p = At(canvas, 0.5);
        Assert.InRange(p.R, 110, 145);
        Assert.True(p.B > 240);
    }

    [Fact]
    public async Task OnlyNoneLayers_LeaveTheElementUnmasked()
    {
        var canvas = await PaintAsync("mask-image: none, none");

        Assert.True(At(canvas, 0.9).R < 20);
    }

    [Theory]
    [InlineData("padding-box")]
    [InlineData("content-box")]
    public async Task MaskOrigin_PositionsTheMaskInsideTheBorderAndPadding(string origin)
    {
        // A 10pt border and 10pt padding: the origin box is smaller than the border box, and a no-repeat mask fills only it.
        var canvas = await PaintAsync(
            $"box-sizing: border-box; border: 10pt solid #00f; padding: 10pt; mask-image: linear-gradient(#000, #000); mask-origin: {origin}; mask-repeat: no-repeat");

        // The border box corner is outside the origin box, so the mask does not reach it.
        Assert.True(TestDocument.Pixel(canvas, 12, 12).R > 225, "outside the mask");
    }

    [Fact]
    public async Task LuminanceIsReadFromTheFirstImageLayer()
    {
        var canvas = await PaintAsync("mask-image: none, linear-gradient(to right, #fff, #000); mask-mode: alpha, luminance");

        Assert.True(At(canvas, 0.97).R > 225);
    }
}
