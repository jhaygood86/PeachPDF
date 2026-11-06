using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.2: a box with <c>clear</c> goes below the floats of its own formatting context that it clears, whatever box they are in.
/// The search looked at the floats one and three levels down an earlier sibling and never at those two (or four) levels down, and in a
/// multi-column container at the floats of every column, whose <c>Y</c> is measured from the top of their own column.
/// </summary>
public class ClearConsidersEveryFloatTests
{
    private const string Float = "<div id='a' style='float:left;width:75pt;height:45pt'></div>";

    private static async Task<(CssBox C, CssBox B, CssBox Root)> LayoutAsync(string content)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(content), pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "b")!, root);
    }

    private static string Nested(string inner, int depth)
    {
        for (var i = 0; i < depth; i++) inner = $"<div>{inner}</div>";

        return inner;
    }

    private static string ClearingFloat(string clear = "left") =>
        $"<div id='b' style='float:left;clear:{clear};width:112.5pt;height:22.5pt'></div>";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task FloatAtAnyDepthInAnEarlierSibling_IsCleared(int depth)
    {
        var (c, b, _) = await LayoutAsync($"<div id='c' style='width:300pt'>{Nested(Float, depth)}{ClearingFloat()}</div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    [Fact]
    public async Task FloatTwoLevelsDown_IsClearedByABlockWithClear()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Nested(Float, 2)}<div id='b' style='clear:both;height:15pt'></div></div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    // Only the floats on the side a box clears count, at any depth.
    [Theory]
    [InlineData("left", 0)]
    [InlineData("right", 45)]
    [InlineData("both", 45)]
    public async Task RightFloatTwoLevelsDown_IsClearedOnlyByClearRightOrBoth(string clear, double below)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Nested("<div id='a' style='float:right;width:75pt;height:45pt'></div>", 2)}{ClearingFloat(clear)}</div>");

        Assert.Equal(c.Location.Y + below, b.Location.Y, 1);
    }

    // What a float does inside a box that is a formatting context of its own stays there (CSS 2.1 §9.4.1): the 7.5pt box does not hold it.
    [Theory]
    [InlineData("overflow:hidden;height:7.5pt", 0)]
    [InlineData("overflow:hidden;height:7.5pt", 1)]
    [InlineData("display:flow-root;height:7.5pt", 0)]
    [InlineData("display:flow-root;height:7.5pt", 2)]
    public async Task FloatInAFormattingContextOfItsOwn_IsNotCleared(string style, int depth)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'><div style='{style}'>{Nested(Float, depth)}</div>{ClearingFloat()}</div>");

        Assert.Equal(c.Location.Y + 7.5, b.Location.Y, 1);
    }

    // A box that is only moved by relative positioning is cleared where it is in the flow (CSS 2.1 §9.4.3), not where it is drawn.
    [Theory]
    [InlineData("top:7.5pt")]
    [InlineData("top:-7.5pt")]
    [InlineData("left:30pt;top:15pt")]
    public async Task FloatInARelativelyPositionedBox_IsClearedAtItsStaticPosition(string offset)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'><div style='position:relative;{offset}'><div>{Float}</div></div>{ClearingFloat()}</div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    private const string Columns = "columns:2;column-gap:10pt;width:300pt;height:225pt;column-fill:auto";

    // The float is in the first column and the box that clears starts the second: there is nothing for it to clear there, whatever the Y of
    // the float in the column before.
    [Fact]
    public async Task FloatInAnEarlierColumn_IsNotCleared()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div style='height:7.5pt'></div>" +
            "<div id='a' style='float:left;width:75pt;height:67.5pt;margin:6pt 0'></div>" +
            "<div style='break-before:column;height:7.5pt'></div>" +
            "<div id='b' style='float:left;clear:left;width:75pt;height:22.5pt;margin:15pt 0 3.75pt'></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the float starts the second column");
        Assert.Equal(c.Location.Y + 7.5 + 15, b.Location.Y, 1);
    }

    [Fact]
    public async Task FloatInTheSameColumn_IsStillCleared()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div style='height:7.5pt'></div>" +
            "<div id='a' style='float:left;width:75pt;height:67.5pt;margin:6pt 0'></div>" +
            "<div id='b' style='float:left;clear:left;width:75pt;height:22.5pt;margin:15pt 0 3.75pt'></div></div>");

        // The float's margin box ends at 7.5 + 6 + 67.5 + 6, and the one that clears it has 15pt of margin above it.
        Assert.Equal(c.Location.X, b.Location.X, 1);
        Assert.Equal(c.Location.Y + 87 + 15, b.Location.Y, 1);
    }

    // The floats are in a block that spans both columns: it is resumed in the second one at the child its break names.
    [Fact]
    public async Task FloatInAnEarlierColumnOfTheSameBlock_IsNotCleared()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div><div style='height:7.5pt'></div>" +
            "<div style='float:left;width:75pt;height:67.5pt;margin:6pt 0'></div>" +
            "<div style='break-before:column;height:7.5pt'></div>" +
            "<div id='b' style='float:left;clear:left;width:75pt;height:22.5pt;margin:15pt 0 3.75pt'></div></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the float starts the second column");
        Assert.Equal(c.Location.Y + 7.5 + 15, b.Location.Y, 1);
    }

    // A float two levels down in the box's own column is found, and one in the column before is not.
    [Fact]
    public async Task NestedFloatInTheSecondColumn_IsClearedAndTheFirstColumnsIsNot()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div style='height:7.5pt'></div>" +
            "<div style='float:left;width:75pt;height:67.5pt'></div>" +
            "<div style='break-before:column;height:7.5pt'></div>" +
            "<div><div><div style='float:left;width:75pt;height:37.5pt'></div></div></div>" +
            "<div id='b' style='float:left;clear:left;width:75pt;height:22.5pt'></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the float is in the second column");
        Assert.Equal(c.Location.Y + 7.5 + 37.5, b.Location.Y, 1);
    }

    // The container's column is not remembered once it has been laid out: a clear after it is looked up as in any other block.
    [Fact]
    public async Task ClearAfterAMultiColumnContainer_ClearsFloatsBeforeIt()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Float}<div style='columns:2;column-gap:10pt'>x</div>{ClearingFloat()}</div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }
}
