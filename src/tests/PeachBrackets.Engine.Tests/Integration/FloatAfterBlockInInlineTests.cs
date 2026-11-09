using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.2.1.1: a block box inside an inline box splits the inline into pieces around it, in source order. A float in the piece after the
/// block is placed after the block, so a <c>clear</c> on or in the block is not cleared by it.
/// </summary>
public class FloatAfterBlockInInlineTests
{
    private static async Task<CssBox> LayoutAsync(string content)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(content), pageHeight: 600);

        return root;
    }

    private static double Y(CssBox root, string id) => LayoutHarness.FindById(root, id)!.Location.Y;

    // Chrome (offsets from the first block, 7.5pt = 10px blocks): a at 0, b at 7.5, the float after b at 15.
    [Fact]
    public async Task FloatAfterTwoBlocksInASpan_IsPlacedAfterThem_SoAClearInTheSecondIsNotCleared()
    {
        var root = await LayoutAsync(
            "<div id='w' style='width:300pt'><span><div id='a' style='height:7.5pt'></div><div><div id='b' style='clear:both;height:7.5pt'></div></div>" +
            "<div id='f' style='float:left;width:37.5pt;height:37.5pt'></div></span></div>");

        Assert.Equal(Y(root, "a") + 7.5, Y(root, "b"), 1);
        Assert.Equal(Y(root, "b") + 7.5, Y(root, "f"), 1);
    }

    [Fact]
    public async Task FloatAfterThreeBlocksInASpan_KeepsSourceOrder()
    {
        var root = await LayoutAsync(
            "<div id='w' style='width:300pt'><span><div id='a' style='height:7.5pt'></div><div id='b' style='height:7.5pt'></div>" +
            "<div id='c' style='height:7.5pt'></div><div id='f' style='float:left;width:37.5pt;height:37.5pt'></div></span></div>");

        Assert.Equal(Y(root, "a") + 7.5, Y(root, "b"), 1);
        Assert.Equal(Y(root, "b") + 7.5, Y(root, "c"), 1);
        Assert.Equal(Y(root, "c") + 7.5, Y(root, "f"), 1);
    }

    [Fact]
    public async Task FloatBetweenBlocksInASpan_IsPlacedBetweenThem()
    {
        var root = await LayoutAsync(
            "<div id='w' style='width:300pt'><span><div id='a' style='height:7.5pt'></div><div id='f' style='float:left;width:37.5pt;height:37.5pt'></div>" +
            "<div id='b' style='height:7.5pt'></div></span></div>");

        Assert.Equal(Y(root, "a") + 7.5, Y(root, "f"), 1);
        Assert.Equal(Y(root, "a") + 7.5, Y(root, "b"), 1);
    }
}
