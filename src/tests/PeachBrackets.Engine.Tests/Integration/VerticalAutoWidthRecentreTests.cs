using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A vertical box's auto width is its content's block-axis extent. Its auto horizontal margins are resolved against that final width, not the
/// width it had before its content shrank it, so <c>margin: 0 auto</c> keeps centering it in the containing block (CSS 2.1 section 10.3.3).
/// </summary>
public class VerticalAutoWidthRecentreTests
{
    private static async Task<(CssBox Parent, CssBox Child)> LayoutAsync(string writingMode, string childStyle)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='parent' style='width:400pt'><div id='child' style='writing-mode:{writingMode};height:120pt;font-size:16pt;{childStyle}'>ab cd</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);

        return (LayoutHarness.FindById(root, "parent")!, LayoutHarness.FindById(root, "child")!);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task AMaxWidthClampedBox_ThatShrinksToItsContent_StaysCentred(string writingMode)
    {
        var (parent, child) = await LayoutAsync(writingMode, "max-width:300pt;margin:0 auto");

        var width = child.ActualRight - child.Location.X;
        Assert.True(width < 100, $"the box should have shrunk to its content, but is {width} wide");
        Assert.Equal(parent.ClientLeft + (400 - width) / 2, child.Location.X, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task APlainAutoWidthBox_WithAutoMargins_IsCentredAtItsFinalWidth(string writingMode)
    {
        var (parent, child) = await LayoutAsync(writingMode, "margin:0 auto");

        var width = child.ActualRight - child.Location.X;
        Assert.Equal(parent.ClientLeft + (400 - width) / 2, child.Location.X, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task AnAutoLeftMargin_PushesTheShrunkBoxToTheContainingBlocksRightEdge(string writingMode)
    {
        var (parent, child) = await LayoutAsync(writingMode, "margin-left:auto;margin-right:10pt");

        Assert.Equal(parent.ClientLeft + 400 - 10, child.ActualRight, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheBoxesWordsMoveWithIt(string writingMode)
    {
        var (_, child) = await LayoutAsync(writingMode, "margin:0 auto");

        foreach (var word in child.LineBoxes.SelectMany(l => l.Words))
        {
            Assert.InRange(word.Left, child.Location.X - 1, child.ActualRight + 1);
            Assert.InRange(word.Right, child.Location.X - 1, child.ActualRight + 1);
        }
    }

    [Fact]
    public async Task AFixedMarginBox_IsNotMoved()
    {
        // No auto margin to resolve: vertical-rl keeps its block-start (right) edge where it was and vertical-lr its left one.
        var (parent, rl) = await LayoutAsync("vertical-rl", "margin-left:7pt");
        Assert.Equal(parent.ClientLeft + 400, rl.ActualRight, 2);

        var (parentLr, lr) = await LayoutAsync("vertical-lr", "margin-left:7pt");
        Assert.Equal(parentLr.ClientLeft + 7, lr.Location.X, 2);
    }
}
