using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// The value functions again, read back by measuring: a blue box at (10pt, 10pt) is scanned for its width along a row and its
/// height down a column. Each case goes through a different path of the calc parser, type checker, serializer, evaluator or the
/// text substitution.
/// </summary>
public class ValueFunctionMeasuredTests
{
    private const string Reset = "<style>@page { margin: 0 } html, body { margin: 0 }</style>";

    private static async Task<RasterCanvas> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body + "<div style='position:absolute;left:0;top:0;width:300pt;height:300pt'></div>", Reset),
            new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static bool IsBlue(RasterCanvas page, int x, int y)
    {
        var pixel = TestDocument.Pixel(page, x, y);
        return (pixel.R, pixel.G, pixel.B) == (0, 0, 255);
    }

    private static int Extent(RasterCanvas page, int x, int y, int dx, int dy)
    {
        var n = 0;
        while (n < 400 && IsBlue(page, x + (dx * n), y + (dy * n))) n++;
        return n;
    }

    private static async Task<(int Width, int Height)> MeasureAsync(string style, string content = "", string wrapStyle = "")
    {
        var page = await PaintAsync(
            $"<div style='position:absolute;left:10pt;top:10pt;{wrapStyle}'><div style='background:rgb(0,0,255);color:rgb(0,0,255);font:12pt sans-serif;{style}'>{content}</div></div>");
        return (Extent(page, 11, 12, 1, 0), Extent(page, 11, 12, 0, 1));
    }

    [Theory]
    [InlineData("width: calc(abs(-30pt) + sign(-5pt) * 10pt); height: 4pt", 20)]
    [InlineData("width: round(to-zero, 37pt, 10pt); height: 4pt", 30)]
    [InlineData("width: round(down, 39pt, 10pt); height: 4pt", 30)]
    [InlineData("width: round(nearest, 35pt, 10pt); height: 4pt", 40)]
    [InlineData("width: mod(47pt, 20pt); height: 4pt", 7)]
    [InlineData("width: rem(47pt, 20pt); height: 4pt", 7)]
    [InlineData("width: calc(hypot(30pt, 40pt) + 1em * 0); height: 4pt", 50)]
    [InlineData("width: calc(sin(pi / 6) * 100pt); height: 4pt", 50)]
    [InlineData("width: calc(tan(45deg) * 30pt); height: 4pt", 30)]
    [InlineData("width: calc(cos(0deg) * e * 0pt + 20pt); height: 4pt", 20)]
    [InlineData("width: calc(log(100, 10) * 10pt); height: 4pt", 20)]
    [InlineData("width: calc(exp(0) * sqrt(25) * 6pt); height: 4pt", 30)]
    [InlineData("width: calc(pow(2, 3) * 5pt); height: 4pt", 40)]
    [InlineData("width: calc(sign(10pt - 4pt) * 33pt); height: 4pt", 33)]
    [InlineData("width: 25pt; height: 4pt; width: round(5pt)", 25)]
    [InlineData("width: 25pt; height: 4pt; width: sin(1pt)", 25)]
    [InlineData("width: 25pt; height: 4pt; width: asin(1deg)", 25)]
    [InlineData("width: 25pt; height: 4pt; width: pow(2pt, 2)", 25)]
    [InlineData("width: 25pt; height: 4pt; width: mod(1pt, 2deg)", 25)]
    [InlineData("width: 25pt; height: 4pt; width: env(not-a-name)", 0)]
    [InlineData("width: 25pt; height: 4pt; width: calc(sibling-index(1) * 1pt)", 0)]
    [InlineData("width: 25pt; height: 4pt !important; width: env(not-a-name, 33pt) !important", 33)]
    [InlineData("width: calc-size(40pt, size + 10pt); height: 4pt", 50)]
    [InlineData("width: calc-size(40pt, size * 2 - 20pt); height: 4pt", 60)]
    [InlineData("width: 25pt; height: 4pt; width: calc-size(40pt)", 0)]
    public async Task AFunctionSizesTheBox(string style, int expectedWidth)
    {
        var (width, _) = await MeasureAsync(style);

        Assert.InRange(width, Math.Max(expectedWidth - 1, 0), expectedWidth + 1);
    }

    [Theory]
    [InlineData("height: calc-size(30pt, size + 10pt); width: 6pt", 38)]
    [InlineData("height: 8pt; min-height: calc-size(30pt, size + 10pt); width: 6pt", 38)]
    [InlineData("height: 90pt; max-height: calc-size(30pt, size + 10pt); width: 6pt", 38)]
    [InlineData("height: 20pt; width: 6pt; height: calc-size(auto, size + 100pt)", 98)]
    [InlineData("width: 6pt; min-height: calc-size(max-content, size + 10pt); max-height: calc-size(fit-content, size)", 0)]
    public async Task AFunctionSizesTheBoxVertically(string style, int expectedHeight)
    {
        var (_, height) = await MeasureAsync(style);

        if (expectedHeight == 0) Assert.True(height < 60);
        else Assert.InRange(height, expectedHeight - 1, expectedHeight + 1);
    }

    [Fact]
    public async Task CalcSizeMeasuresIntrinsicWidths()
    {
        const string text = "alpha beta gamma";
        var plain = (await MeasureAsync("height: 4pt", text, "width: max-content")).Width;
        var maxContent = (await MeasureAsync("height: 4pt; width: calc-size(max-content, size + 10pt)", text, "width: 400pt")).Width;
        var minContent = (await MeasureAsync("height: 4pt; width: calc-size(min-content, size + 10pt)", text, "width: 400pt")).Width;
        var fitContent = (await MeasureAsync("height: 4pt; width: calc-size(fit-content, size)", text, "width: 400pt")).Width;
        var auto = (await MeasureAsync("height: 4pt; width: calc-size(auto, size / 2)", text, "width: 200pt")).Width;
        var minWidth = (await MeasureAsync("height: 4pt; width: 10pt; min-width: calc-size(auto, size / 4)", text, "width: 200pt")).Width;
        var maxWidth = (await MeasureAsync("height: 4pt; max-width: calc-size(auto, size / 4)", text, "width: 200pt")).Width;

        Assert.True(plain > 20);
        Assert.InRange(maxContent, plain + 8, plain + 12);
        Assert.True(minContent > 10 && minContent < maxContent);
        Assert.InRange(fitContent, plain - 2, plain + 2);
        Assert.InRange(auto, 99, 101);
        Assert.InRange(minWidth, 49, 51);
        Assert.InRange(maxWidth, 49, 51);
    }

    [Fact]
    public async Task SiblingCountAndIndexAreEachSiblingsOwn()
    {
        var page = await PaintAsync(
            "<style>i { display: block; height: 4pt; background: rgb(0,0,255); width: calc(100pt / sibling-count() + sibling-index() * 1pt) }</style>"
            + "<div style='position:absolute;left:10pt;top:10pt'><i></i><b></b><i></i><i></i></div>");

        // Four element children: the first <i> is 25 + 1 wide.
        Assert.InRange(Extent(page, 11, 11, 1, 0), 25, 27);
    }

    [Fact]
    public async Task DirPseudoClassMatchesByDirectionality()
    {
        var page = await PaintAsync(
            "<style>i { display: block; height: 4pt; background: rgb(0,0,255) } :dir(rtl) > i { width: 40pt } :dir(ltr) > i { width: 70pt } :dir(sideways) > i { width: 5pt }</style>"
            + "<div style='position:absolute;left:10pt;top:10pt' dir='rtl'><i></i></div>"
            + "<div style='position:absolute;left:10pt;top:60pt'><i></i></div>"
            + "<div style='position:absolute;left:10pt;top:110pt' dir='rtl'><b dir='ltr'><i></i></b></div>");

        Assert.InRange(Extent(page, 11, 11, 1, 0), 39, 41);
        Assert.InRange(Extent(page, 11, 61, 1, 0), 69, 71);
        Assert.InRange(Extent(page, 11, 111, 1, 0), 69, 71);
    }

    [Theory]
    [InlineData("rotate(atan2(1, 1))")]
    [InlineData("rotate(atan2(1pt, 1pt))")]
    [InlineData("rotate(asin(0.5))")]
    [InlineData("rotate(acos(1))")]
    [InlineData("rotate(atan(1))")]
    [InlineData("rotate(calc(abs(-90deg) + hypot(0deg, 0deg)))")]
    [InlineData("rotate(round(up, 20deg, 15deg))")]
    [InlineData("rotate(mod(100deg, 90deg))")]
    [InlineData("rotate(calc(min(1turn, 90deg) * sign(2)))")]
    public async Task AnAngleFunctionRotatesTheBox(string transform)
    {
        var page = await PaintAsync($"<div style='position:absolute;left:10pt;top:10pt;width:40pt;height:40pt;background:rgb(0,0,255);transform:{transform}'></div>");

        Assert.True(IsBlue(page, 30, 30));
    }
}
