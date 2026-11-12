using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// The value functions beyond calc/min/max/clamp, written the way a host uses them: a document painted onto a bitmap and read back by pixel.
/// </summary>
public class ValueFunctionPaintTests
{
    private const string Reset = "<style>@page { margin: 0 } html, body { margin: 0 }</style>";

    private static async Task<RasterCanvas> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body + "<div style='position:absolute;left:0;top:0;width:200pt;height:200pt'></div>", Reset),
            new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static (int R, int G, int B) Rgb(RasterCanvas canvas, int x, int y)
    {
        var pixel = TestDocument.Pixel(canvas, x, y);
        return (pixel.R, pixel.G, pixel.B);
    }

    private static string Box(string style) =>
        $"<div style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;{style}'></div>";

    [Theory]
    [InlineData("#777", 0)]
    [InlineData("#eee", 0)]
    [InlineData("#123", 255)]
    [InlineData("navy", 255)]
    [InlineData("yellow", 0)]
    public async Task ContrastColorIsBlackOrWhiteWhicheverReadsBetter(string operand, int channel)
    {
        var page = await PaintAsync(Box("background: contrast-color(" + operand + ")"));

        Assert.Equal((channel, channel, channel), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ContrastColorTakesCurrentColorAsItsOperand()
    {
        var page = await PaintAsync(Box("color: navy; background: contrast-color(currentcolor)"));

        Assert.Equal((255, 255, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ContrastColorWorksOnTheTextColorProperty()
    {
        var page = await PaintAsync(Box("background: navy") + "<p style='position:absolute;left:10pt;top:10pt;margin:0;font: 40pt sans-serif;color: contrast-color(navy)'>M</p>");

        // White text over navy: somewhere on the glyph's rows there is a light pixel.
        var light = Enumerable.Range(10, 50).Any(x => Enumerable.Range(15, 40).Any(y => Rgb(page, x, y).R > 200));
        Assert.True(light);
    }

    [Fact]
    public async Task AMathFunctionSizesABox()
    {
        var page = await PaintAsync(Box("background: rgb(0,0,255); width: round(up, 33.3pt, 10pt)"));

        Assert.Equal((0, 0, 255), Rgb(page, 10 + 38, 40));
        Assert.NotEqual((0, 0, 255), Rgb(page, 10 + 42, 40));
    }

    [Fact]
    public async Task EnvFallsBackWhenTheNameIsNotDefined()
    {
        var page = await PaintAsync(Box("background: rgb(0,0,255); width: env(unknown-name, 30pt)"));

        Assert.Equal((0, 0, 255), Rgb(page, 10 + 28, 40));
        Assert.NotEqual((0, 0, 255), Rgb(page, 10 + 32, 40));
    }

    [Fact]
    public async Task SiblingIndexMakesEachSiblingADifferentSize()
    {
        var page = await PaintAsync(
            "<style>i { display: block; height: 10pt; background: rgb(0,0,255); width: calc(sibling-index() * 20pt) }</style>"
            + "<div style='position:absolute;left:0;top:0'><i></i><i></i><i></i></div>");

        // Rows are 10pt tall: widths 20, 40, 60.
        Assert.Equal((0, 0, 255), Rgb(page, 18, 5));
        Assert.NotEqual((0, 0, 255), Rgb(page, 22, 5));
        Assert.Equal((0, 0, 255), Rgb(page, 38, 15));
        Assert.NotEqual((0, 0, 255), Rgb(page, 42, 15));
        Assert.Equal((0, 0, 255), Rgb(page, 58, 25));
        Assert.NotEqual((0, 0, 255), Rgb(page, 62, 25));
    }
}
