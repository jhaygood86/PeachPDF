using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary><c>block-step-*</c> (CSS Rhythmic Sizing 1 section 4): a block container's height is rounded to a multiple of the step.</summary>
public class BlockStepTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<(CssBox A, CssBox B)> LayoutAsync(string style)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='a' style='margin:0;font-size:10px;line-height:10px;{style}'>x<br>y<br>z</div><div id='b' style='margin:0;height:5px'></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 800);
        return (LayoutHarness.FindById(root, "a")!, LayoutHarness.FindById(root, "b")!);
    }

    private static double Px(double value) => value / PointsPerPx;

    [Fact]
    public async Task TheDefaultInsertsTheDifferenceAsMargin_ThePaddingBoxIsUnchanged()
    {
        var (a, b) = await LayoutAsync("block-step-size:25px");

        Assert.Equal(30, Px(a.ActualBottom - a.Location.Y), 1);
        Assert.Equal(50, Px(b.Location.Y - a.Location.Y), 1);
    }

    [Fact]
    public async Task PaddingBoxInsertGrowsTheBox()
    {
        var (a, b) = await LayoutAsync("block-step-size:25px;block-step-insert:padding-box");

        Assert.Equal(50, Px(a.ActualBottom - a.Location.Y), 1);
        Assert.Equal(50, Px(b.Location.Y - a.Location.Y), 1);
    }

    [Fact]
    public async Task CenterAlignSplitsTheMargin()
    {
        var (a, b) = await LayoutAsync("block-step-size:25px;block-step-align:center");
        var (plain, _) = await LayoutAsync("");

        Assert.Equal(Px(plain.Location.Y) + 10, Px(a.Location.Y), 1);
        Assert.Equal(50, Px(b.Location.Y - plain.Location.Y), 1);
    }

    [Theory]
    [InlineData("block-step-round:up", 50)]
    [InlineData("block-step-round:down", 25)]
    [InlineData("block-step-round:nearest", 25)]
    public async Task RoundChoosesTheMultiple(string round, double nextPx)
    {
        var (a, b) = await LayoutAsync($"block-step-size:25px;{round}");

        Assert.Equal(nextPx, Px(b.Location.Y - a.Location.Y), 1);
    }

    [Fact]
    public async Task TheShorthandSetsTheLonghands()
    {
        var (a, b) = await LayoutAsync("block-step:25px padding-box");

        Assert.Equal(50, Px(a.ActualBottom - a.Location.Y), 1);
        Assert.Equal("padding-box", a.ComputedStyle.BoxModel.BlockStepInsert);
    }

    [Fact]
    public async Task NoneChangesNothing()
    {
        var (a, _) = await LayoutAsync("block-step-size:none");

        Assert.Equal(30, Px(a.ActualBottom - a.Location.Y), 1);
    }
}
