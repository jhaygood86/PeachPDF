using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// <c>display: flow-root</c> (css-display-3 §2.4) is a block that establishes an independent block formatting context:
/// it contains its floats (CSS 2.1 §10.6.7), and its margins do not collapse with its children's (§8.3.1).
/// </summary>
public class FlowRootTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 }</style></head><body>" + body + "</body></html>";

    [Fact]
    public async Task FlowRoot_ContainsItsFloat_SoWhatFollowsGoesBelowIt()
    {
        var body = "<div style='display:flow-root'><div style='float:left;width:120pt;height:86pt'></div></div>w9_1 w9_2 ";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.True(positions["w9_1"].Y >= 20 + 86 - 0.5, $"the text started at y={positions["w9_1"].Y}, beside the float");
    }

    [Fact]
    public async Task PlainBlock_StillDoesNotContainItsFloat()
    {
        var body = "<div><div style='float:left;width:120pt;height:86pt'></div></div>w9_1 w9_2 ";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.True(positions["w9_1"].Y < 20 + 86 - 0.5, $"the text started at y={positions["w9_1"].Y}");
    }

    // A flow-root's own margin and its first child's do not collapse (§8.3.1), so they add; in a plain block the larger wins.
    [Theory]
    [InlineData("display:flow-root", 55)]
    [InlineData("overflow:hidden", 55)]
    [InlineData("display:block", 50)]
    public async Task FlowRoot_KeepsItsChildsMarginInsideIt(string display, double expectedY)
    {
        // 20pt page margin + the 10pt spacer + (5pt + 20pt, or the larger of the two where they collapse).
        var body = $"<div style='height:10pt'></div><div style='{display};margin-top:5pt'><p style='margin-top:20pt'>w9_1</p></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.InRange(positions["w9_1"].Y, expectedY - 0.5, expectedY + 1);
    }

    // A floated flow-root is blockified like any float, and still contains its own float.
    [Fact]
    public async Task FloatedFlowRoot_IsABlockThatContainsItsFloat()
    {
        var body = "<div style='float:left;display:flow-root;width:150pt'><div style='float:left;width:60pt;height:40pt'></div>w9_1 </div>w9_2 ";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.InRange(positions["w9_1"].X, 20 + 60 - 0.5, 20 + 150);
        Assert.InRange(positions["w9_2"].X, 20 + 150 - 0.5, 300);
    }

    [Fact]
    public async Task FlowRoot_WithNoContent_IsEmptyBlockThatDoesNotCollapseThrough()
    {
        // An empty flow-root has no height, but its margins do not pass through it to what follows.
        var body = "<p>w9_1</p><div style='display:flow-root;margin:30pt 0'></div><p>w9_2</p>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.InRange(positions["w9_2"].Y - positions["w9_1"].Y, 12 + 60 - 1, 12 + 60 + 1);
    }
}
