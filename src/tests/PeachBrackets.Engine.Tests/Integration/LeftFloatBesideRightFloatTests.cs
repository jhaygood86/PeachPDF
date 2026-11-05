using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1 rules 1-3: the outer edges of floats never overlap, so a left float that comes after a right one and does not fit
/// beside it goes below it. The lookup that finds what a left float overlaps only asked whether the float's left edge lay inside a
/// blocker, which a right float further along never does, so the left float stayed at the top of the container, over the right one.
/// </summary>
public class LeftFloatBesideRightFloatTests
{
    private const string Container = "<div id='c' style='width:300pt'>{0}</div>";

    private static async Task<(CssBox C, CssBox R, CssBox L)> LayoutAsync(string content)
    {
        var html = LayoutHarness.Wrap(string.Format(Container, content));
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "r")!, LayoutHarness.FindById(root, "l")!);
    }

    [Fact]
    public async Task LeftFloatThatDoesNotFitBesideAnEarlierRightFloat_GoesBelowIt()
    {
        var (c, r, l) = await LayoutAsync(
            "<div id='r' style='float:right;width:225pt;height:40pt'></div><div id='l' style='float:left;width:150pt;height:40pt'></div>");

        Assert.Equal(c.Location.Y, r.Location.Y, 1);
        Assert.Equal(r.ActualBottom, l.Location.Y, 1);
        Assert.Equal(c.Location.X, l.Location.X, 1);
        Assert.Equal(c.Location.X + 75, r.Location.X, 1);
    }

    [Fact]
    public async Task LeftFloatThatFitsBesideAnEarlierRightFloat_StaysOnItsRow()
    {
        var (c, r, l) = await LayoutAsync(
            "<div id='r' style='float:right;width:150pt;height:40pt'></div><div id='l' style='float:left;width:150pt;height:40pt'></div>");

        Assert.Equal(c.Location.Y, l.Location.Y, 1);
        Assert.Equal(c.Location.X, l.Location.X, 1);
        Assert.Equal(c.Location.X + 150, r.Location.X, 1);
    }

    // The outer edges are the margin edges: a margin-right that pushes the left float's margin box into the right float's sends it below
    // even though the border boxes do not touch.
    [Theory]
    [InlineData("20pt", true)]
    [InlineData("10pt", false)]
    public async Task LeftFloatsMarginRightCountsTowardsFittingBesideARightFloat(string marginRight, bool below)
    {
        var (c, r, l) = await LayoutAsync(
            "<div id='r' style='float:right;width:150pt;height:40pt'></div>" +
            $"<div id='l' style='float:left;width:140pt;margin-right:{marginRight};height:40pt'></div>");

        Assert.Equal(below ? r.ActualBottom : c.Location.Y, l.Location.Y, 1);
        Assert.Equal(c.Location.X, l.Location.X, 1);
    }

    // A float inside a box that is a formatting context of its own is no blocker outside it: here the box clips it to 15pt, and the next
    // float starts at the box's bottom, whichever side the inner float is on and however wide the two are together.
    [Theory]
    [InlineData("overflow:hidden;height:15pt", "right")]
    [InlineData("overflow:hidden;height:15pt", "left")]
    [InlineData("display:flex;height:15pt", "right")]
    public async Task FloatInsideAFormattingContextSibling_DoesNotBlockALaterLeftFloat(string box, string side)
    {
        var (c, _, l) = await LayoutAsync(
            $"<div style='{box}'><div id='r' style='float:{side};width:190pt;height:60pt'></div></div>" +
            "<div id='l' style='float:left;width:190pt;height:20pt'></div>");

        Assert.Equal(c.Location.Y + 15, l.Location.Y, 1);
    }

    [Fact]
    public async Task FloatInsideAnAbsolutelyPositionedSibling_DoesNotBlockALaterLeftFloat()
    {
        var (c, _, l) = await LayoutAsync(
            "<div style='position:absolute;width:225pt'><div id='r' style='float:right;width:190pt;height:60pt'></div></div>" +
            "<div id='l' style='float:left;width:190pt;height:20pt'></div>");

        Assert.Equal(c.Location.Y, l.Location.Y, 1);
    }

    // A regression guard: this holds without the fix too.
    [Fact]
    public async Task LeftFloatBeforeARightFloat_IsUnchanged()
    {
        var (c, r, l) = await LayoutAsync(
            "<div id='l' style='float:left;width:150pt;height:40pt'></div><div id='r' style='float:right;width:225pt;height:40pt'></div>");

        Assert.Equal(c.Location.Y, l.Location.Y, 1);
        Assert.Equal(l.ActualBottom, r.Location.Y, 1);
    }

    [Fact]
    public async Task LeftFloatAfterAShortRightFloat_GoesBelowThatFloatOnly()
    {
        var (c, r, l) = await LayoutAsync(
            "<div id='r' style='float:right;width:225pt;height:20pt'></div><div id='l' style='float:left;width:150pt;height:40pt'></div>");

        Assert.Equal(c.Location.Y + 20, l.Location.Y, 1);
        Assert.Equal(r.ActualBottom, l.Location.Y, 1);
    }

    // 70% and 30% of the same width only add up to it to within rounding, which must not push the second float down.
    [Theory]
    [InlineData("333px")]
    [InlineData("347px")]
    [InlineData("523px")]
    public async Task FloatsWhoseWidthsAddUpToTheContainerOnlyByRounding_StayOnOneRow(string width)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='c' style='width:{width}'><div id='r' style='float:right;width:70%;height:40pt'></div>" +
            "<div id='l' style='float:left;width:30%;height:40pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "r")!.Location.Y, LayoutHarness.FindById(root, "l")!.Location.Y, 1);
    }

    // `clear: left` only has to clear the left float; once it has, the right float does not stop this one sitting beside it.
    [Fact]
    public async Task ClearLeftAfterARightFloat_ClearsTheLeftFloatsBottomOnly()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:75pt;height:30pt'></div>" +
            "<div id='r' style='float:right;width:190pt;height:50pt'></div>" +
            "<div id='l' style='float:left;clear:left;width:75pt;height:30pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var a = LayoutHarness.FindById(root, "a")!;
        var l = LayoutHarness.FindById(root, "l")!;

        Assert.Equal(a.ActualBottom, l.Location.Y, 1);
        Assert.Equal(a.Location.X, l.Location.X, 1);
    }

    [Fact]
    public async Task TableAlignedLeftAfterATableAlignedRight_GoesBelowItWhenTheyDoNotFitTogether()
    {
        var (c, r, l) = await LayoutAsync(
            "<table id='r' align='right' style='width:225pt;height:40pt'><tr><td>r</td></tr></table>" +
            "<table id='l' align='left' style='width:150pt;height:40pt'><tr><td>l</td></tr></table>");

        Assert.Equal(c.Location.Y, r.Location.Y, 1);
        Assert.Equal(225, r.ActualRight - r.Location.X, 1);
        Assert.Equal(c.Location.X + 75, r.Location.X, 1);
        Assert.Equal(r.ActualBottom, l.Location.Y, 1);
        Assert.Equal(c.Location.X, l.Location.X, 1);
    }
}
