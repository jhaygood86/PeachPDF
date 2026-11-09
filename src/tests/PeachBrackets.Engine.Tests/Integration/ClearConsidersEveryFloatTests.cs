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

    // CSS 2.1 §9.5.2: the floats of the formatting context that come before the box, whatever box they are in - including a float before an
    // ancestor of the box. Chrome: the cleared box is at the bottom of the float.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FloatBeforeAnAncestor_IsClearedByABlockWithClear(int depth)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Float}{Nested("<div id='b' style='clear:left;height:7.5pt'></div>", depth)}</div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    [Fact]
    public async Task FloatBeforeAnAncestor_IsClearedByAFloatWithClear()
    {
        var (c, b, _) = await LayoutAsync($"<div id='c' style='width:300pt'>{Float}<div>{ClearingFloat()}</div></div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
        Assert.Equal(c.Location.X, b.Location.X, 1);
    }

    [Theory]
    [InlineData("left", 0)]
    [InlineData("right", 45)]
    [InlineData("both", 45)]
    public async Task RightFloatBeforeAnAncestor_IsClearedOnlyByClearRightOrBoth(string clear, double below)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'><div style='float:right;width:75pt;height:45pt'></div><div><div id='b' style='clear:{clear};height:7.5pt'></div></div></div>");

        Assert.Equal(c.Location.Y + below, b.Location.Y, 1);
    }

    // A float outside a box that is a formatting context of its own is not a float of that context (CSS 2.1 §9.4.1).
    [Theory]
    [InlineData("overflow:hidden")]
    [InlineData("display:flow-root")]
    [InlineData("display:inline-block;width:200pt")]
    public async Task FloatBeforeAFormattingContextRoot_IsNotClearedFromInsideIt(string style)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Float}<div style='{style}'><div id='b' style='clear:left;height:7.5pt'></div></div></div>");

        Assert.Equal(c.Location.Y, b.Location.Y, 1);
    }

    // The floats before an ancestor are measured as they are laid out, not as they are drawn (CSS 2.1 §9.4.3).
    [Fact]
    public async Task RelativelyPositionedFloatBeforeAnAncestor_ClearsAtItsStaticBottom()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'><div style='float:left;position:relative;top:30pt;width:75pt;height:45pt'></div><div><div id='b' style='clear:left;height:7.5pt'></div></div></div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    [Fact]
    public async Task FloatBeforeAnAncestor_IsNotClearedWhenTheBoxIsAlreadyBelowIt()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{Float}<div><div style='height:60pt'></div><div id='b' style='clear:left;height:7.5pt'></div></div></div>");

        Assert.Equal(c.Location.Y + 60, b.Location.Y, 1);
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

    // What is not in the flow is not cleared: a box that is not displayed is never laid out (its floats keep the geometry of a layout that did
    // not happen), an absolutely positioned one computes to float: none (CSS 2.1 section 9.7), and a multi-column container contains its own floats.
    [Theory]
    [InlineData("<div style='display:none'><div><div style='float:left;width:75pt;height:60pt'></div></div></div>")]
    [InlineData("<div hidden><div><div style='float:left;width:75pt;height:60pt'></div></div></div>")]
    [InlineData("<div style='display:none'><div style='float:left;width:75pt;height:60pt'></div></div>")]
    [InlineData("<div style='position:relative'><div><div><div style='position:absolute;float:left;top:0;left:150pt;width:75pt;height:60pt'></div></div></div></div>")]
    [InlineData("<div style='columns:2;height:11.25pt;column-fill:auto'><div><div style='float:left;width:37.5pt;height:52.5pt'></div></div></div>")]
    public async Task FloatThatIsNotInTheFlow_IsNotCleared(string sibling)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{sibling}<div style='height:7.5pt'></div><div id='b' style='clear:both;height:15pt'></div></div>");

        // Only the 7.5pt block (and the multi-column container's own 11.25pt) is above the box.
        var above = sibling.Contains("columns:2") ? 11.25 + 7.5 : 7.5;

        Assert.Equal(c.Location.Y + above, b.Location.Y, 1);
    }

    // A table, a table cell or an inline-block that holds nothing but a float is as tall as the float (CSS 2.1 section 10.6.7), so it is not
    // empty and what follows it goes below, with `clear` or without.
    [Theory]
    [InlineData("<div style='display:table'><div style='display:table-cell;width:112.5pt'>{0}</div></div>", "")]
    [InlineData("<div style='display:table'><div style='display:table-cell;width:112.5pt'>{0}</div></div>", "clear:both;")]
    [InlineData("<table cellspacing=0 cellpadding=0><tr><td>{0}</td></tr></table>", "clear:both;")]
    [InlineData("<div style='display:inline-block;vertical-align:top;width:112.5pt'>{0}</div>", "clear:both;")]
    public async Task ContainerHoldingOnlyAFloat_IsNotEmpty(string container, string clear)
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{string.Format(container, Float)}<div id='b' style='{clear}height:15pt'></div></div>");

        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
    }

    // The `height` of a table cell is a minimum (CSS 2.1 section 17.5.3): a cell that holds a float taller than it is as tall as the float, and
    // what follows goes below it, `clear` or not. An ordinary box with a definite height keeps it and the float overhangs.
    [Theory]
    [InlineData("<div style='display:table-cell;width:150pt;height:15pt'>{0}</div>", "clear:left;")]
    [InlineData("<div style='display:table-cell;width:150pt;height:15pt'>{0}</div>", "")]
    [InlineData("<div style='display:table'><div style='display:table-cell;width:150pt;height:15pt'>{0}</div></div>", "clear:left;")]
    [InlineData("<div style='display:table-cell;width:150pt;height:15pt'><div><div>{0}</div></div></div>", "clear:left;")]
    [InlineData("<table cellspacing=0 cellpadding=0><tr><td style='width:150pt;height:15pt;vertical-align:top'>{0}</td></tr></table>", "")]
    [InlineData("<table cellspacing=0 cellpadding=0><tr><td style='width:150pt;height:15pt;vertical-align:top'>{0}</td></tr></table>", "clear:left;")]
    public async Task TableCellWithAFixedHeightShorterThanItsFloat_GrowsToContainIt(string cell, string clear)
    {
        var floatInCell = "<div style='float:left;width:75pt;height:60pt'></div>";
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='width:300pt'>{string.Format(cell, floatInCell)}<div id='b' style='{clear}height:15pt'>af</div></div>");

        Assert.Equal(c.Location.Y + 60, b.Location.Y, 1);
    }

    [Fact]
    public async Task TableCellWithAFixedHeightTallerThanItsFloat_KeepsItsHeight()
    {
        var (c, b, _) = await LayoutAsync(
            "<div id='c' style='width:300pt'><table cellspacing=0 cellpadding=0><tr><td style='width:150pt;height:75pt;vertical-align:top'>" +
            "<div style='float:left;width:75pt;height:22.5pt'></div></td></tr></table><div id='b' style='height:15pt'>af</div></div>");

        Assert.Equal(c.Location.Y + 75, b.Location.Y, 1);
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

    // A float that did not fit at the foot of the first column is laid out at the top of the second, from before the child the column starts
    // at, and is in the column: a box that clears goes below it. (The height is a column height only for a container that clips its content.)
    [Theory]
    [InlineData("")]
    [InlineData("<div style='height:45pt'></div>")]
    public async Task FloatDeferredIntoTheColumn_IsCleared(string inFlowBeforeTheClear)
    {
        var (c, b, _) = await LayoutAsync(
            "<div id='c' style='columns:2;column-gap:7.5pt;width:307.5pt;height:225pt;column-fill:auto;overflow:hidden'>" +
            "<div style='height:187.5pt'></div><div style='float:left;width:75pt;height:75pt;break-inside:avoid'></div>" +
            inFlowBeforeTheClear + "<div id='b' style='clear:left;height:22.5pt'></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the box is in the second column");
        Assert.Equal(c.Location.Y + 75, b.Location.Y, 1);
    }

    // The floats of the column before are not looked at inside the child the column resumes in either: the float is in the wrapper's first
    // column and the box that clears follows the wrapper in the second.
    [Fact]
    public async Task FloatInTheResumedChildInAnEarlierColumn_IsNotCleared()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div style='height:7.5pt'></div><div><div style='height:7.5pt'></div>" +
            "<div style='float:left;width:75pt;height:67.5pt;margin:6pt 0'></div><div style='break-before:column;height:7.5pt'></div></div>" +
            "<div id='b' style='float:left;clear:left;width:75pt;height:22.5pt;margin:15pt 0 3.75pt'></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the float starts the second column");
        Assert.Equal(c.Location.Y + 7.5 + 15, b.Location.Y, 1);
    }

    // ... and one in the resumed child that is in this column is: the block that clears goes below it.
    [Fact]
    public async Task FloatInTheResumedChildInThisColumn_IsCleared()
    {
        var (c, b, _) = await LayoutAsync(
            $"<div id='c' style='{Columns}'><div style='height:7.5pt'></div><div><div style='height:7.5pt'></div>" +
            "<div style='break-before:column;height:7.5pt'></div><div style='float:left;width:75pt;height:40pt'></div></div>" +
            "<div id='b' style='clear:left;height:22.5pt'></div></div>");

        Assert.True(b.Location.X > c.Location.X + 140, "the box is in the second column");
        Assert.Equal(c.Location.Y + 7.5 + 40, b.Location.Y, 1);
    }

    // A container that balances fills its columns more than once, and what one fill knew about where its column starts is gone for the next:
    // the float is in the first column of the last fill too.
    [Fact]
    public async Task FloatInTheFirstColumnOfABalancedContainer_IsCleared()
    {
        var (c, b, _) = await LayoutAsync(
            "<div id='c' style='columns:2;column-gap:10pt;width:300pt'>" +
            "<div style='float:left;width:75pt;height:45pt'></div><div id='b' style='clear:left;height:30pt'></div>" +
            "<div style='height:30pt'></div><div style='height:30pt'></div><div style='height:30pt'></div></div>");

        Assert.Equal(c.Location.X, b.Location.X, 1);
        Assert.Equal(c.Location.Y + 45, b.Location.Y, 1);
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
