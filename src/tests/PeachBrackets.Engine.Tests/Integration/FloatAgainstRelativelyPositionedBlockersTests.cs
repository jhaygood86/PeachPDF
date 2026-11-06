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
    [InlineData("top:-12pt")]
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
    }
}
