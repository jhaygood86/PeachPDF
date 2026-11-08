using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A flex container in a vertical writing mode with an <c>auto</c> width, a definite height and an <c>aspect-ratio</c>: its width is its block
/// size, which the preferred ratio fixes from the definite inline size (CSS Box Sizing 4 section 5.1, css-writing-modes-4 section 3). The
/// container's main size - physical width for a <c>column</c> - is that width, not the whole containing block.
/// </summary>
public class FlexboxVerticalAspectRatioTests
{
    private static double WidthOf(CssBox box) => box.ActualRight - box.Location.X;

    [Theory]
    [InlineData("vertical-rl", "column")]
    [InlineData("vertical-lr", "column")]
    [InlineData("vertical-rl", "row")]
    public async Task AnAutoWidth_TakesTheRatioOfTheDefiniteHeight(string writingMode, string direction)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='f' style='display:flex;flex-direction:{direction};writing-mode:{writingMode};height:200pt;aspect-ratio:1/2'>"
            + "<div id='a' style='flex:1'>A</div><div id='b' style='flex:1'>B</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);
        var flex = LayoutHarness.FindById(root, "f")!;

        Assert.Equal(100, WidthOf(flex), 2);
        Assert.Equal(200, flex.ActualBottom - flex.Location.Y, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task ColumnItemsShareTheRatioDerivedMainSize(string writingMode)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='f' style='display:flex;flex-direction:column;writing-mode:{writingMode};height:200pt;aspect-ratio:1/2'>"
            + "<div id='a' style='flex:1'>A</div><div id='b' style='flex:1'>B</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);
        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;

        Assert.Equal(50, WidthOf(a), 2);
        Assert.Equal(50, WidthOf(b), 2);
    }

    [Fact]
    public async Task APaddedBorderBoxContainer_ReadsTheRatioOnItsBorderBox()
    {
        var html = LayoutHarness.Wrap(
            "<div id='f' style='display:flex;flex-direction:column;writing-mode:vertical-rl;box-sizing:border-box;height:200pt;aspect-ratio:1/2;padding:10pt'>"
            + "<div id='a' style='flex:1'>A</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);

        Assert.Equal(100, WidthOf(LayoutHarness.FindById(root, "f")!), 2);
    }

    [Fact]
    public async Task AnExplicitWidth_StillWinsOverTheRatio()
    {
        var html = LayoutHarness.Wrap(
            "<div id='f' style='display:flex;flex-direction:column;writing-mode:vertical-rl;width:150pt;height:200pt;aspect-ratio:1/2'>"
            + "<div id='a' style='flex:1'>A</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);

        Assert.Equal(150, WidthOf(LayoutHarness.FindById(root, "f")!), 2);
    }

    [Fact]
    public async Task WithoutAnAspectRatio_TheAutoWidthStillFillsTheContainingBlock()
    {
        var html = LayoutHarness.Wrap(
            "<div id='f' style='display:flex;flex-direction:column;writing-mode:vertical-rl;height:200pt'><div id='a' style='flex:1'>A</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0, pageWidth: 500);

        Assert.Equal(500, WidthOf(LayoutHarness.FindById(root, "f")!), 2);
    }

    [Fact]
    public async Task AHorizontalFlexContainer_KeepsStretchingItsWidthDespiteTheRatio()
    {
        // The width of a horizontal block is its inline size: stretch-fit wins over the ratio (CSS Box Sizing 4 section 5.1).
        var html = LayoutHarness.Wrap(
            "<div id='f' style='display:flex;height:200pt;aspect-ratio:1/2'><div id='a' style='flex:1'>A</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0, pageWidth: 500);

        Assert.Equal(500, WidthOf(LayoutHarness.FindById(root, "f")!), 2);
    }
}
