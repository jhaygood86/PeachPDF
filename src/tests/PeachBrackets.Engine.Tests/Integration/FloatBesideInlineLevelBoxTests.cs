using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1 rules 1-3: a float's margin box may not overlap a line box's content. A right float that does not fit beside an
/// inline-block already on its line goes below the line. The blocker search used to find the inline-block only through floats inside it,
/// and once it stopped looking inside a box that is a formatting context of its own (a float there lies outside what the box takes up in
/// the flow) a right float went to the top of the line, over the inline-block.
/// </summary>
public class FloatBesideInlineLevelBoxTests
{
    private static async Task<(CssBox C, CssBox R, CssBox L)> LayoutAsync(string content)
    {
        var html = LayoutHarness.Wrap($"<div id='c' style='width:375pt'>{content}</div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "r")!, LayoutHarness.FindById(root, "l")!);
    }

    private const string Floats =
        "<div id='r' style='float:right;width:225pt;height:30pt'></div><div id='l' style='float:left;width:150pt;height:30pt'></div>";

    // However the inline-block holds its content, the 225pt right float does not fit beside it on a 375pt line: it goes below, and the left
    // float after it may not start above it (rule 5).
    [Theory]
    [InlineData("<div style='display:inline-block;vertical-align:top;width:225pt;height:60pt'></div>")]
    [InlineData("<div style='display:inline-block;vertical-align:top;width:225pt;height:60pt'>text</div>")]
    [InlineData("<div style='display:inline-block;vertical-align:top;width:225pt'><p style='margin:0;height:60pt'>text</p></div>")]
    [InlineData("<div style='display:inline-block;vertical-align:top;width:225pt'><div style='float:right;width:112.5pt;height:60pt'></div>" +
                "<div style='float:left;width:75pt;height:15pt'></div><div style='clear:both'></div></div>")]
    [InlineData("<div style='display:inline-flex;vertical-align:top;width:225pt;height:60pt'></div>")]
    [InlineData("<div style='display:inline-grid;vertical-align:top;width:225pt;height:60pt'></div>")]
    public async Task RightFloatThatDoesNotFitBesideAnInlineBlock_GoesBelowTheLine(string inlineBox)
    {
        var (c, r, l) = await LayoutAsync(inlineBox + Floats);

        Assert.Equal(c.Location.Y + 60, r.Location.Y, 1);
        Assert.Equal(c.Location.Y + 60, l.Location.Y, 1);
        Assert.Equal(c.Location.X, l.Location.X, 1);
    }

    [Fact]
    public async Task RightFloatThatFitsBesideAnInlineBlock_StaysOnTheLine()
    {
        var (c, r, _) = await LayoutAsync(
            "<div style='display:inline-block;vertical-align:top;width:225pt;height:60pt'></div>" +
            "<div id='r' style='float:right;width:150pt;height:30pt'></div>");

        Assert.Equal(c.Location.Y, r.Location.Y, 1);
        Assert.Equal(c.Location.X + 225, r.Location.X, 1);
    }

    // The inline-block's margin box counts, not only its border box: 225pt + 20pt of margin leaves no room for a 150pt float.
    [Theory]
    [InlineData("20pt", true, false)]
    [InlineData("0pt", false, false)]
    [InlineData("20pt", true, true)]
    [InlineData("0pt", false, true)]
    public async Task InlineBlocksMarginCountsTowardsFittingBesideARightFloat(string marginRight, bool below, bool blockContent)
    {
        // One that holds block-level content is placed with its own box (found by the blocker scan); an empty one is on the line as reserved width.
        var content = blockContent ? "<p style='margin:0;height:60pt'>text</p>" : "";
        var height = blockContent ? "" : "height:60pt;";

        var (c, r, _) = await LayoutAsync(
            $"<div style='display:inline-block;vertical-align:top;width:225pt;{height}margin-right:{marginRight}'>{content}</div>" +
            "<div id='r' style='float:right;width:150pt;height:30pt'></div><div id='l' style='float:left;width:10pt;height:30pt'></div>");

        Assert.Equal(below ? c.Location.Y + 60 : c.Location.Y, r.Location.Y, 1);
    }

    [Fact]
    public async Task RightFloatAfterTextAndAnInlineBlock_GoesBelowTheLine()
    {
        var (c, r, _) = await LayoutAsync(
            "text <span style='display:inline-block;width:225pt;height:60pt'></span>" +
            "<div id='r' style='float:right;width:225pt;height:30pt'></div><div id='l' style='float:left;width:10pt;height:30pt'></div>");

        Assert.True(r.Location.Y >= c.Location.Y + 60, $"the float is at {r.Location.Y - c.Location.Y}, over the line");
    }

    // An inline-block that sticks out of an earlier block is on that block's line, not on the float's: the float after the block does not
    // have to clear it.
    [Fact]
    public async Task InlineBlockOverflowingAnEarlierBlock_IsNotOnTheFloatsLine()
    {
        var (c, r, _) = await LayoutAsync(
            "<div style='height:15pt'><span style='display:inline-block;width:75pt;height:75pt'></span></div>" +
            "<div id='r' style='float:right;width:337.5pt;height:22.5pt'></div><div id='l' style='float:left;width:10pt;height:22.5pt'></div>");

        Assert.Equal(c.Location.Y + 15, r.Location.Y, 1);
    }

    // The float goes below the line, and what follows it in the source goes on the line after the inline-block, as in a browser: the
    // container holds one line, not two.
    [Fact]
    public async Task InlineContentAfterAFloatThatWentBelowTheLine_StaysOnTheLine()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:375pt'><span style='display:inline-block;width:225pt;height:22.5pt'></span>" +
            "<div id='r' style='float:right;width:187.5pt;height:75pt'></div>" +
            "<span style='display:inline-block;width:30pt;height:22.5pt'></span></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var c = LayoutHarness.FindById(root, "c")!;

        Assert.True(LayoutHarness.FindById(root, "r")!.Location.Y >= c.Location.Y + 22.5 - 0.1, "the float is below the line");
        Assert.True(c.ActualBottom - c.Location.Y < 40, $"the second inline-block is on a line of its own (the container is {c.ActualBottom - c.Location.Y}pt tall)");
    }
}
