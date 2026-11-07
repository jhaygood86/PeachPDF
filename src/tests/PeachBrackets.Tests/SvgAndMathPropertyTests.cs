using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary><c>shape-rendering</c>, <c>vector-effect</c>, <c>buffered-rendering</c> (SVG) and <c>math-style</c>, <c>math-depth</c>, <c>math-shift</c> (MathML Core), painted through the bitmap canvas.</summary>
public class SvgAndMathPropertyTests
{
    private static async Task<RasterCanvas> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>@page { margin: 0 } math { font-size: 24pt }</style>"), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static int PartialPixels(RasterCanvas canvas, int size)
    {
        var count = 0;
        var buffer = canvas.ToPixelBuffer();
        for (var y = 0; y < Math.Min(size, buffer.Height); y++)
        {
            for (var x = 0; x < Math.Min(size, buffer.Width); x++)
            {
                var (r, _, _, _) = TestDocument.Pixel(canvas, x, y);
                if (r is > 30 and < 225) count++;
            }
        }

        return count;
    }

    private static int InkRows(RasterCanvas canvas, int width, int height)
    {
        var rows = 0;
        var buffer = canvas.ToPixelBuffer();
        for (var y = 0; y < Math.Min(height, buffer.Height); y++)
        {
            if (TestDocument.HasInk(canvas, 0, y, Math.Min(width, buffer.Width), 1)) rows++;
        }

        return rows;
    }

    private static string Diagonal(string attributes, string groupAttributes = "") =>
        $"<svg width='40' height='40' viewBox='0 0 40 40'><g {groupAttributes}><line x1='2' y1='2' x2='38' y2='27' stroke='black' stroke-width='1' {attributes}/></g></svg>";

    private static string Horizontal(string attributes) =>
        $"<svg width='100' height='20' viewBox='0 0 10 2'><line x1='0' y1='1' x2='10' y2='1' stroke='black' stroke-width='1' {attributes}/></svg>";

    [Fact]
    public async Task AutoShapeRenderingAntialiasesADiagonal()
    {
        Assert.True(PartialPixels(await PaintAsync(Diagonal("")), 40) > 10);
        Assert.True(PartialPixels(await PaintAsync(Diagonal("shape-rendering='geometricPrecision'")), 40) > 10);
    }

    [Theory]
    [InlineData("shape-rendering='crispEdges'", "")]
    [InlineData("shape-rendering='optimizeSpeed'", "")]
    [InlineData("", "shape-rendering='crispEdges'")]
    [InlineData("style='shape-rendering: crispEdges'", "")]
    public async Task CrispEdgesAndOptimizeSpeedDrawWithoutAntialiasing_AndTheValueIsInherited(string attributes, string group)
    {
        var canvas = await PaintAsync(Diagonal(attributes, group));

        Assert.True(TestDocument.HasInk(canvas, 0, 0, 40, 40));
        Assert.Equal(0, PartialPixels(canvas, 40));
    }

    [Fact]
    public async Task AChildCanTurnAntialiasingBackOn()
    {
        var canvas = await PaintAsync(Diagonal("shape-rendering='auto'", "shape-rendering='crispEdges'"));

        Assert.True(PartialPixels(canvas, 40) > 10);
    }

    [Fact]
    public async Task ANonScalingStrokeKeepsItsWidthUnderAScalingViewBox()
    {
        var scaling = InkRows(await PaintAsync(Horizontal("")), 100, 20);
        var nonScaling = InkRows(await PaintAsync(Horizontal("vector-effect='non-scaling-stroke'")), 100, 20);
        var none = InkRows(await PaintAsync(Horizontal("vector-effect='none'")), 100, 20);

        Assert.True(scaling is >= 9 and <= 11, $"scaling {scaling} nonScaling {nonScaling}");
        Assert.InRange(nonScaling, 1, 2);
        Assert.Equal(scaling, none);
    }

    [Fact]
    public async Task VectorEffectDoesNotInherit()
    {
        var svg = "<svg width='100' height='20' viewBox='0 0 10 2'><g vector-effect='non-scaling-stroke'><line x1='0' y1='1' x2='10' y2='1' stroke='black' stroke-width='1'/></g></svg>";

        Assert.InRange(InkRows(await PaintAsync(svg), 100, 20), 9, 11);
    }

    [Fact]
    public async Task BufferedRenderingIsAcceptedAndChangesNothing()
    {
        var plain = await PaintAsync(Diagonal(""));
        var buffered = await PaintAsync(Diagonal("buffered-rendering='static'"));

        Assert.Equal(plain.ToPixelBuffer().PremultipliedRgba.ToArray(), buffered.ToPixelBuffer().PremultipliedRgba.ToArray());
    }

    [Theory]
    [InlineData("add(2)")]
    [InlineData("2")]
    public async Task MathDepthMakesTheContentsSmaller(string depth)
    {
        var plain = InkRows(await PaintAsync("<p><math><mi>H</mi></math></p>"), 400, 200);
        var deeper = InkRows(await PaintAsync($"<p><math><mstyle style='math-depth: {depth}'><mi>H</mi></mstyle></math></p>"), 400, 200);

        Assert.True(deeper < plain, $"{deeper} vs {plain}");
    }

    [Fact]
    public async Task MathDepthAbsoluteZeroUndoesADeeperParent_AndAutoAddChangesNothing()
    {
        var plain = InkRows(await PaintAsync("<p><math><mi>H</mi></math></p>"), 400, 200);
        var undone = InkRows(await PaintAsync("<p><math><mstyle style='math-depth: 3'><mstyle style='math-depth: 0'><mi>H</mi></mstyle></mstyle></math></p>"), 400, 200);
        var auto = InkRows(await PaintAsync("<p><math><mstyle style='math-depth: auto-add'><mi>H</mi></mstyle></math></p>"), 400, 200);

        Assert.Equal(plain, undone);
        Assert.Equal(plain, auto);
    }

    [Fact]
    public async Task MathShiftIsAcceptedAndCascades()
    {
        var plain = await PaintAsync("<p><math><msup><mi>x</mi><mn>2</mn></msup></math></p>");
        var compact = await PaintAsync("<p><math style='math-shift: compact'><msup><mi>x</mi><mn>2</mn></msup></math></p>");

        Assert.True(TestDocument.HasInk(compact, 0, 0, 400, 200));
        Assert.Equal(plain.ToPixelBuffer().PremultipliedRgba.ToArray(), compact.ToPixelBuffer().PremultipliedRgba.ToArray());
    }
}
