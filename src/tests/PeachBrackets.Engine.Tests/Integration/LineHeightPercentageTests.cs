using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A <c>line-height</c> given as a percentage is that share of the element's own font size (CSS 2.1 section 10.8.1). It was resolved against the
/// box's own height, which is 0 while its lines are laid out, so every line was 0 tall: consecutive blocks were drawn on top of each other and had
/// no height.
/// </summary>
public class LineHeightPercentageTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<(CssBox First, CssBox Second)> LayoutAsync(string style, string secondStyle = "")
    {
        var html = LayoutHarness.Wrap(
            $"<div id='a' style='font-size:20px;{style}'>First</div><div id='b' style='font-size:20px;{style};{secondStyle}'>Second</div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "a")!, LayoutHarness.FindById(root, "b")!);
    }

    [Theory]
    [InlineData("line-height:150%", 30)]
    [InlineData("line-height:200%", 40)]
    [InlineData("line-height:50%", 10)]
    [InlineData("line-height:calc(100% + 5px)", 25)]
    // The unit-less number and the em, which already worked, resolve to the same lengths.
    [InlineData("line-height:1.5", 30)]
    [InlineData("line-height:1.5em", 30)]
    public async Task APercentage_IsAShareOfTheFontSize(string style, double heightInPx)
    {
        var (first, second) = await LayoutAsync(style);

        Assert.Equal(heightInPx * PointsPerPx, first.ActualBottom - first.Location.Y, 1);
        Assert.Equal(heightInPx * PointsPerPx, second.Location.Y - first.Location.Y, 1);
    }

    [Fact]
    public async Task APercentage_FollowsTheFontSizeOfTheBoxItIsSetOn()
    {
        var html = LayoutHarness.Wrap("<div id='a' style='line-height:150%;font-size:20px'>x</div><div id='b' style='line-height:150%;font-size:40px'>x</div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(30 * PointsPerPx, LayoutHarness.FindById(root, "a")!.ActualBottom - LayoutHarness.FindById(root, "a")!.Location.Y, 1);
        Assert.Equal(60 * PointsPerPx, LayoutHarness.FindById(root, "b")!.ActualBottom - LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }
}
