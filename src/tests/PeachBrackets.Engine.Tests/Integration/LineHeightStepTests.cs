using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// <c>line-height-step</c> (CSS Rhythmic Sizing 1 section 3.1) rounds each line box of a block up to a multiple of the step, the added
/// space split equally above and below.
/// </summary>
public class LineHeightStepTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<CssBox> LayoutAsync(string style, string content = "a<br>b<br>c", string extra = "")
    {
        var html = LayoutHarness.Wrap($"<div id='a' style='font-size:10px;margin:0;{style}'>{content}</div>{extra}");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 800);
        return LayoutHarness.FindById(root, "a")!;
    }

    private static double HeightPx(CssBox box) => (box.ActualBottom - box.Location.Y) / PointsPerPx;

    [Theory]
    [InlineData("line-height:13px;line-height-step:20px", 60)]
    [InlineData("line-height:20px;line-height-step:20px", 60)]
    [InlineData("line-height:21px;line-height-step:20px", 120)]
    [InlineData("line-height:13px;line-height-step:10px", 60)]
    [InlineData("line-height:13px;line-height-step:none", 39)]
    [InlineData("line-height:13px", 39)]
    public async Task EachLineIsRoundedUpToAMultipleOfTheStep(string style, double heightPx)
    {
        Assert.Equal(heightPx, HeightPx(await LayoutAsync(style)), 1);
    }

    [Fact]
    public async Task TheStepIsInherited()
    {
        var box = await LayoutAsync("line-height:13px;line-height-step:20px", "<p style='margin:0'>a<br>b</p>");

        Assert.Equal(40, HeightPx(box), 1);
    }

    [Fact]
    public async Task TheGlyphsAreCentredInTheSteppedLine()
    {
        var plain = await LayoutAsync("line-height:10px");
        var stepped = await LayoutAsync("line-height:10px;line-height-step:30px");

        var plainTop = plain.LineBoxes[0].LineTop;
        var steppedLine = stepped.LineBoxes[0];

        // 10px line in a 30px step: 10px added above, 10px below, so the baseline sits 10px lower.
        Assert.Equal(plain.LineBoxes[0].BaselineY!.Value - plainTop + 10 * PointsPerPx, steppedLine.BaselineY!.Value - steppedLine.LineTop, 1);
    }

    [Fact]
    public async Task AVerticalColumnIsRoundedUpToTheStepToo()
    {
        var box = await LayoutAsync("writing-mode:vertical-rl;line-height:13px;line-height-step:20px;height:100px", "a<br>b<br>c");

        Assert.Equal(60, (box.ActualRight - box.Location.X) / PointsPerPx, 1);
    }
}
