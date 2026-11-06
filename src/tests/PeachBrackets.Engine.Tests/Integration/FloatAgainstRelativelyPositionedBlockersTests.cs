using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.4.3: relative positioning is visual only, so a float is placed against where its blockers are in the flow. The offset of a box
/// moves everything inside it, so a float in a relatively positioned box has to have the box's offset taken back off, as well as its own.
/// </summary>
public class FloatAgainstRelativelyPositionedBlockersTests
{
    private static async Task<CssBox> LayoutAsync(string content)
    {
        var html = LayoutHarness.Wrap($"<div id='c' style='width:375pt'>{content}</div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return root;
    }

    private static CssBox Box(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

    // The right float inside the relatively positioned box ends at 60pt in the flow, not at 63.75pt where it is drawn; the next float that
    // does not fit beside it goes to 60pt.
    [Theory]
    [InlineData("top:3.75pt")]
    [InlineData("top:3.75pt;left:5pt")]
    [InlineData("bottom:3.75pt")]
    public async Task FloatBelowABlockerInARelativelyPositionedBox_IsPlacedAgainstTheStaticPosition(string offset)
    {
        var root = await LayoutAsync(
            $"<div style='width:225pt;position:relative;{offset}'><div style='float:right;width:112.5pt;height:60pt'></div>" +
            "<div style='float:left;width:75pt;height:15pt'></div><div style='clear:both'></div></div>" +
            "<div id='a' style='float:left;width:150pt;height:30pt'></div><div id='b' style='float:left;width:187.5pt;height:15pt'></div>");

        var c = Box(root, "c");

        Assert.Equal(c.Location.Y + 60, Box(root, "a").Location.Y, 1);
        Assert.Equal(c.Location.Y + 60, Box(root, "b").Location.Y, 1);
        Assert.Equal(c.Location.X + 150, Box(root, "b").Location.X, 1);
    }

    // The same through a box that is itself the relatively positioned blocker, and through one nested in a relatively positioned box.
    [Theory]
    [InlineData("<div id='s' style='float:right;width:112.5pt;height:60pt;position:relative;top:3.75pt;left:-3pt'></div>")]
    [InlineData("<div style='position:relative;top:3.75pt'><div style='position:relative;left:6pt;top:3.75pt'><div id='s' style='float:right;width:112.5pt;height:60pt'></div></div></div>")]
    public async Task FloatBesideARelativelyPositionedBlocker_KeepsItsStaticEdges(string blocker)
    {
        var root = await LayoutAsync(blocker +
            "<div id='a' style='float:left;width:300pt;height:30pt'></div><div id='b' style='float:left;width:75pt;height:15pt'></div>");

        var c = Box(root, "c");

        // 300pt does not fit beside the 112.5pt float in 375pt, wherever the float is drawn; the 75pt one then fits beside it.
        Assert.Equal(c.Location.Y + 60, Box(root, "a").Location.Y, 1);
        Assert.Equal(c.Location.X + 300, Box(root, "b").Location.X, 1);
        Assert.Equal(c.Location.Y + 60, Box(root, "b").Location.Y, 1);
    }

    // The horizontal edges are the static ones too: a blocker drawn 10pt to the side is still where the flow put it.
    [Fact]
    public async Task RightFloatBesideALeftBlockerInAnOffsetBox_FitsAgainstTheStaticEdge()
    {
        var root = await LayoutAsync(
            "<div style='width:150pt;position:relative;left:10pt'><div style='float:left;width:75pt;height:30pt'></div></div>" +
            "<div id='a' style='float:right;width:300pt;height:15pt'></div>");

        // 75pt + 300pt is the 375pt container exactly; drawn 10pt over it would not fit.
        Assert.Equal(Box(root, "c").Location.Y, Box(root, "a").Location.Y, 1);
    }

    [Fact]
    public async Task LeftFloatBesideARightBlockerInAnOffsetBox_FitsAgainstTheStaticEdge()
    {
        var root = await LayoutAsync(
            "<div style='width:375pt;position:relative;left:-10pt'><div style='float:right;width:150pt;height:30pt'></div></div>" +
            "<div id='a' style='float:left;width:225pt;height:15pt'></div>");

        Assert.Equal(Box(root, "c").Location.Y, Box(root, "a").Location.Y, 1);
    }

    [Fact]
    public async Task LeftFloatAfterALeftBlockerInAnOffsetBox_StartsAtTheStaticEdge()
    {
        var root = await LayoutAsync(
            "<div style='width:150pt;position:relative;left:10pt'><div style='float:left;width:75pt;height:30pt'></div></div>" +
            "<div id='a' style='float:left;width:100pt;height:15pt'></div>");

        // Beside the blocker, not under it: the blocker starts at the left edge and is 75pt wide, drawn or not.
        Assert.Equal(Box(root, "c").Location.X + 75, Box(root, "a").Location.X, 1);
        Assert.Equal(Box(root, "c").Location.Y, Box(root, "a").Location.Y, 1);
    }

    [Fact]
    public async Task RightFloatAfterARightBlockerInAnOffsetBox_EndsAtTheStaticEdge()
    {
        var root = await LayoutAsync(
            "<div style='width:375pt;position:relative;left:-10pt'><div style='float:right;width:75pt;height:30pt'></div></div>" +
            "<div id='a' style='float:right;width:50pt;height:15pt'></div>");

        Assert.Equal(Box(root, "c").Location.X + 375 - 75 - 50, Box(root, "a").Location.X, 1);
    }

    [Fact]
    public async Task RightFloatThatDoesNotFitBesideARightBlockerInAnOffsetBox_GoesBelowItsStaticBottom()
    {
        var root = await LayoutAsync(
            "<div style='width:375pt;position:relative;top:3.75pt'><div style='float:right;width:225pt;height:60pt'></div></div>" +
            "<div id='a' style='float:right;width:225pt;height:15pt'></div>");

        Assert.Equal(Box(root, "c").Location.Y + 60, Box(root, "a").Location.Y, 1);
    }

    // The float being placed can be in an offset box itself: a box's offset is applied before its content is laid out, so the float's own
    // coordinates already include the offsets of the boxes it lies in, and its blockers have to be compared in that frame, not the static one.
    [Theory]
    [InlineData("position:relative;top:15pt", "position:relative;top:15pt", 0, 15, false)]
    [InlineData("position:relative;top:15pt", "position:relative;top:15pt", 0, 15, true)]
    [InlineData("position:relative;left:22.5pt", "position:relative;left:22.5pt", 22.5, 0, false)]
    [InlineData("position:relative;top:15pt;left:7.5pt", "position:relative;top:-6pt;left:-3pt", -3, -6, false)]
    [InlineData("", "position:relative;top:15pt;left:7.5pt", 7.5, 15, false)]
    public async Task FloatInAnOffsetBox_IsPlacedBesideABlockerInAnOffsetSibling(string blockerBox, string floatBox, double drawnX, double drawnY, bool wrapped)
    {
        var inner = "<div id='a' style='float:left;width:75pt;height:22.5pt'></div>";
        if (wrapped) inner = "<div>" + inner + "</div>";

        var root = await LayoutAsync(
            $"<div style='{blockerBox}'><div style='float:left;width:75pt;height:7.5pt'></div></div>" +
            $"<div style='{floatBox};height:150pt'>{inner}</div>");

        var c = Box(root, "c");
        var a = Box(root, "a");

        // Beside the blocker in the flow (75pt from the container's left edge), then drawn where its own box's offset puts it.
        Assert.Equal(c.Location.X + 75 + drawnX, a.Location.X, 1);
        Assert.Equal(c.Location.Y + drawnY, a.Location.Y, 1);
    }

    // Rule 5 in the same frame: the earlier float's top is 37.5pt down in the flow, the later one is in a box drawn 15pt up, so it is at
    // 22.5pt, not held to 37.5pt of its own drawn position.
    [Fact]
    public async Task LaterFloatInAnOffsetBox_IsNotHeldBelowAnEarlierFloatByItsOwnBoxsOffset()
    {
        var root = await LayoutAsync(
            "<div style='height:37.5pt'><div style='height:37.5pt'></div></div><div style='float:left;width:75pt;height:15pt'></div>" +
            "<div style='position:relative;top:-15pt;height:75pt'><div id='a' style='float:right;width:22.5pt;height:22.5pt'></div></div>");

        Assert.Equal(Box(root, "c").Location.Y + 37.5 - 15, Box(root, "a").Location.Y, 1);
    }

    // The line flow compares in the same frame: the inline-block starts beside the float, wherever the box holding both is drawn.
    [Fact]
    public async Task InlineContentInAnOffsetBox_StartsBesideAFloatInAnOffsetSibling()
    {
        var root = await LayoutAsync(
            "<div style='position:relative;top:15pt;left:7.5pt'><div style='float:left;width:75pt;height:7.5pt'></div></div>" +
            "<div style='position:relative;top:15pt;left:7.5pt'><div id='i' style='display:inline-block;width:30pt'><p style='margin:0;height:15pt'>x</p></div></div>");

        Assert.Equal(Box(root, "c").Location.X + 7.5 + 75, Box(root, "i").Location.X, 1);
    }

    // `clear` measures the bottom of the floats it clears the same way.
    [Fact]
    public async Task ClearAfterAFloatInARelativelyPositionedBox_ClearsItsStaticBottom()
    {
        var root = await LayoutAsync(
            "<div style='width:225pt;position:relative;top:3.75pt'><div style='float:left;width:75pt;height:45pt'></div></div>" +
            "<div id='a' style='float:left;clear:left;width:150pt;height:30pt'></div>");

        Assert.Equal(Box(root, "c").Location.Y + 45, Box(root, "a").Location.Y, 1);
    }

    // Rule 5 compares the tops of floats, which a relative offset must not move either: the float in the box starts at the container's top,
    // so a later float that fits beside it may too.
    [Fact]
    public async Task LaterFloat_IsNotHeldBelowAnEarlierFloatByAnAncestorsRelativeOffset()
    {
        var root = await LayoutAsync(
            "<div style='width:150pt;position:relative;top:30pt'><div style='float:left;width:75pt;height:10pt'></div></div>" +
            "<div id='a' style='float:left;width:75pt;height:10pt'></div>");

        Assert.Equal(Box(root, "c").Location.Y, Box(root, "a").Location.Y, 1);

        // Beside it, not under it, which is where the float is found as a blocker at all.
        Assert.Equal(Box(root, "c").Location.X + 75, Box(root, "a").Location.X, 1);
    }

    // An offset box drawn above the flow (the leaked bottom is above the float's top, the static one is not).
    [Fact]
    public async Task FloatBelowABlockerInAnOffsetBoxDrawnUp_IsPlacedAgainstTheStaticBottom()
    {
        var root = await LayoutAsync(
            "<div style='position:relative;top:-12pt'><div style='float:right;width:112.5pt;height:60pt'></div></div>" +
            "<div style='height:52.5pt'></div><div id='a' style='float:left;width:300pt;height:15pt'></div>");

        Assert.Equal(Box(root, "c").Location.Y + 60, Box(root, "a").Location.Y, 1);
    }

    // Nested offset boxes add up on both axes.
    [Fact]
    public async Task NestedOffsetBoxes_AddUpWhenTakenOff()
    {
        var root = await LayoutAsync(
            "<div style='position:relative;left:7.5pt;top:3.75pt'><div style='position:relative;left:7.5pt;top:3.75pt'>" +
            "<div style='float:left;width:75pt;height:30pt'></div></div></div>" +
            "<div id='a' style='float:left;width:100pt;height:15pt'></div>");

        Assert.Equal(Box(root, "c").Location.X + 75, Box(root, "a").Location.X, 1);
        Assert.Equal(Box(root, "c").Location.Y, Box(root, "a").Location.Y, 1);
    }

    // A relatively positioned inline-block is a blocker at its static position too.
    [Fact]
    public async Task RightFloatBesideARelativelyPositionedInlineBlock_GoesBelowItsStaticLine()
    {
        var root = await LayoutAsync(
            "<div style='display:inline-block;vertical-align:top;position:relative;top:22.5pt;width:225pt'><p style='margin:0;height:45pt'>x</p></div>" +
            "<div id='a' style='float:right;width:187.5pt;height:15pt'></div>");

        // The line is 45pt tall (the box is drawn 22.5pt lower, which is not where the line is).
        Assert.Equal(Box(root, "c").Location.Y + 45, Box(root, "a").Location.Y, 1);
    }

    // The line flow reads the same blockers: text after an offset box that holds a float lays out as it does without the offset, whatever the
    // offset (a box hidden by a large negative `left` is a common idiom, and its float must not push the text off the page).
    [Theory]
    [InlineData("left:-9999px")]
    [InlineData("left:-45pt")]
    [InlineData("left:30pt")]
    [InlineData("top:22.5pt")]
    public async Task TextAfterAnOffsetBoxHoldingAFloat_LaysOutAsWithoutTheOffset(string offset)
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 80));

        async Task<(double Height, double Words)> Measure(string style)
        {
            var root = await LayoutAsync(
                $"<div style='position:relative;height:7.5pt;{style}'><div style='float:left;width:75pt;height:45pt'></div></div><p id='p' style='margin:0'>{words}</p>");
            var p = Box(root, "p");
            return (p.ActualBottom - p.Location.Y, p.Words.Count + p.Boxes.Sum(b => b.Words.Count));
        }

        var without = await Measure("");
        var with = await Measure(offset);

        Assert.Equal(without.Height, with.Height, 1);
    }
}
