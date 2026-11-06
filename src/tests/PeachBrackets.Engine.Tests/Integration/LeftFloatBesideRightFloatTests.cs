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

    // The words on the line stay where they are: the left float went below the right one, so it is not beside the line, and moving the words
    // beside it would put them 150pt to the right of where a browser has them.
    [Fact]
    public async Task LeftFloatBelowAnEarlierRightFloat_LeavesTheWordsOnTheLineInPlace()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:375pt'><div id='t' style='display:inline-block;vertical-align:top;width:225pt;height:60pt'>text</div>" +
            "<div id='r' style='float:right;width:225pt;height:30pt'></div><div id='l' style='float:left;width:150pt;height:30pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var c = LayoutHarness.FindById(root, "c")!;
        var l = LayoutHarness.FindById(root, "l")!;
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.True(l.Location.Y >= c.Location.Y + 59, "the left float goes below the line");
        static IEnumerable<CssRect> WordsOf(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(WordsOf));

        Assert.NotEmpty(WordsOf(t));
        Assert.Equal(c.Location.X, WordsOf(t).Min(w => w.Left), 1);
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

    // Percentages that are not exact in binary (a third and two thirds, rounded to four decimals) add up to the container to within a
    // rounding error that can go either way, whichever sides the two floats are on. The pairs on one side are the ones that reach the fit
    // tests in FloatBoxLeft and FloatBoxRight (the first float is a blocker there, the second then only just fits); the pairs on opposite
    // sides reach the blocker test.
    [Theory]
    [InlineData("right", "left", "66.6667%", "33.3333%", "700px")]
    [InlineData("left", "right", "33.3333%", "66.6667%", "700px")]
    [InlineData("right", "left", "33.3333%", "66.6667%", "700px")]
    [InlineData("right", "left", "14.2857%", "85.7143%", "1000px")]
    [InlineData("left", "right", "14.2857%", "85.7143%", "1000px")]
    [InlineData("left", "left", "66.6667%", "33.3333%", "700px")]
    [InlineData("left", "left", "33.3333%", "66.6667%", "700px")]
    [InlineData("left", "left", "14.2857%", "85.7143%", "1000px")]
    [InlineData("left", "left", "85.7143%", "14.2857%", "1000px")]
    [InlineData("left", "left", "66.6667%", "33.3333%", "333px")]
    [InlineData("right", "right", "66.6667%", "33.3333%", "700px")]
    [InlineData("right", "right", "33.3333%", "66.6667%", "700px")]
    [InlineData("right", "right", "14.2857%", "85.7143%", "1000px")]
    [InlineData("right", "right", "85.7143%", "14.2857%", "1000px")]
    [InlineData("right", "right", "66.6667%", "33.3333%", "333px")]
    public async Task FloatsWhoseWidthsAreRoundedPercentagesOfTheContainer_StayOnOneRow(string firstSide, string secondSide, string firstWidth, string secondWidth, string container)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='c' style='width:{container}'><div id='r' style='float:{firstSide};width:{firstWidth};height:30px'></div>" +
            $"<div id='l' style='float:{secondSide};width:{secondWidth};height:30px'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var first = LayoutHarness.FindById(root, "r")!;
        var second = LayoutHarness.FindById(root, "l")!;

        Assert.Equal(first.Location.Y, second.Location.Y, 1);

        // Side by side, not over each other: whichever is further left ends where the other starts.
        var (left, right) = first.Location.X < second.Location.X ? (first, second) : (second, first);
        Assert.Equal(left.ActualRight, right.Location.X, 0.5);
    }

    // Three floats of 233.34px in 700px are 0.02px over: a browser keeps them on one row, as the single-precision tolerance alone would not.
    [Theory]
    [InlineData("left", "left", "left")]
    [InlineData("right", "right", "right")]
    [InlineData("left", "right", "left")]
    public async Task ThreeFloatsAFewHundredthsOfAPixelOver_StayOnOneRow(string a, string b, string d)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='c' style='width:700px'><div id='r' style='float:{a};width:233.34px;height:30px'></div>" +
            $"<div id='m' style='float:{b};width:233.34px;height:30px'></div><div id='l' style='float:{d};width:233.34px;height:30px'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var top = LayoutHarness.FindById(root, "r")!.Location.Y;

        Assert.Equal(top, LayoutHarness.FindById(root, "m")!.Location.Y, 1);
        Assert.Equal(top, LayoutHarness.FindById(root, "l")!.Location.Y, 1);
    }

    // The tolerance is a few hundredths of a point: floats that really overlap, by a tenth of a pixel or more, still go to two rows.
    [Theory]
    [InlineData("right", "left")]
    [InlineData("left", "right")]
    [InlineData("left", "left")]
    [InlineData("right", "right")]
    public async Task FloatsThatReallyOverlap_GoOnTwoRows(string firstSide, string secondSide)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='c' style='width:700px'><div id='r' style='float:{firstSide};width:66.6667%;height:30px'></div>" +
            $"<div id='l' style='float:{secondSide};width:33.4%;height:30px'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var first = LayoutHarness.FindById(root, "r")!;

        Assert.Equal(first.ActualBottom, LayoutHarness.FindById(root, "l")!.Location.Y, 1);
    }

    // A float goes below its blocker's margin box (rule 8): a float that does not fit beside a blocker with a bottom margin starts below
    // the margin.
    [Theory]
    [InlineData("right", "left")]
    [InlineData("left", "left")]
    public async Task FloatThatDoesNotFitBesideABlockerWithABottomMargin_GoesBelowItsMarginBox(string blockerSide, string floatSide)
    {
        var (c, _, l) = await LayoutAsync(
            $"<div id='r' style='float:{blockerSide};width:187.5pt;height:15pt;margin-bottom:7.5pt'></div>" +
            $"<div id='l' style='float:{floatSide};width:188.25pt;height:15pt'></div>");

        Assert.Equal(c.Location.Y + 22.5, l.Location.Y, 1);
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
