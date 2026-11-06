using System.Globalization;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §9.5.1 float placement as it reaches the PDF: a float's rectangle is read back from the page's content stream by its fill
    /// colour. The same rules are asserted on the box tree in <c>PeachBrackets.Engine.Tests</c> (<c>LeftFloatBesideRightFloatTests</c>,
    /// <c>FloatTopNotAboveAnEarlierFloatTests</c>); these run through <see cref="PdfGenerator"/>, which is what the coverage gate measures
    /// the engine through. PDF space has its origin at the bottom, so "below" is a smaller <c>y</c> and a float's top edge is <c>y + h</c>.
    /// </summary>
    public class FloatPlacementPdfTests
    {
        private const string Red = "1 0 0";
        private const string Lime = "0 1 0";
        private const string Blue = "0 0 1";
        private const string Yellow = "1 1 0";

        private readonly record struct Rect(double X, double Y, double W, double H)
        {
            public double Top => Y + H;
        }

        private static async Task<Dictionary<string, Rect>> RenderAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0 } div { box-sizing: border-box }</style></head><body>" +
                       body + "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            return rects;
        }

        private static string Float(string side, string color, string size, string extra = "") =>
            $"<div style='float:{side};{size};background:{color};{extra}'></div>";

        [Fact]
        public async Task LeftFloatThatDoesNotFitBesideAnEarlierRightFloat_GoesBelowIt()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("right", "#f00", "width:225pt;height:40pt") + Float("left", "#00f", "width:150pt;height:40pt") + "</div>");

            Assert.Equal(r[Red].Y, r[Blue].Top, 1);
            Assert.True(r[Blue].X < r[Red].X);
        }

        [Fact]
        public async Task LeftFloatThatFitsBesideAnEarlierRightFloat_StaysOnItsRow()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("right", "#f00", "width:150pt;height:40pt") + Float("left", "#00f", "width:150pt;height:40pt") + "</div>");

            Assert.Equal(r[Red].Top, r[Blue].Top, 1);
        }

        // The left float's margin-right counts: 140pt + 20pt reaches into the 150pt right float, 140pt + 10pt only touches it.
        [Theory]
        [InlineData("20pt", true)]
        [InlineData("10pt", false)]
        public async Task LeftFloatsMarginRightCountsTowardsFittingBesideARightFloat(string marginRight, bool below)
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("right", "#f00", "width:150pt;height:40pt") +
                Float("left", "#00f", "width:140pt;height:40pt", $"margin-right:{marginRight}") + "</div>");

            if (below) Assert.Equal(r[Red].Y, r[Blue].Top, 1);
            else Assert.Equal(r[Red].Top, r[Blue].Top, 1);
        }

        [Fact]
        public async Task FloatsWhoseWidthsAddUpToTheContainerOnlyByRounding_StayOnOneRow()
        {
            var r = await RenderAsync(
                "<div style='width:333px'>" + Float("right", "#f00", "width:70%;height:40pt") + Float("left", "#00f", "width:30%;height:40pt") + "</div>");

            Assert.Equal(r[Red].Top, r[Blue].Top, 1);
        }

        // `clear: left` only has to clear the left float; once it has, the right float does not stop this one sitting beside it.
        [Fact]
        public async Task ClearLeftAfterARightFloat_ClearsTheLeftFloatsBottomOnly()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("left", "#0f0", "width:75pt;height:30pt") + Float("right", "#f00", "width:190pt;height:50pt") +
                Float("left", "#00f", "width:75pt;height:30pt", "clear:left") + "</div>");

            Assert.Equal(r[Lime].Y, r[Blue].Top, 1);
            Assert.Equal(r[Lime].X, r[Blue].X, 1);
        }

        [Fact]
        public async Task ClearRightAfterARightFloat_GoesBelowIt()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("right", "#f00", "width:100pt;height:30pt") + Float("right", "#00f", "width:100pt;height:20pt", "clear:right") + "</div>");

            Assert.Equal(r[Red].Y, r[Blue].Top, 1);
        }

        // Widths that are rounded percentages of the container add up to it only to within a rounding error, whichever sides the floats are on.
        [Theory]
        [InlineData("right", "left", "66.6667%", "33.3333%")]
        [InlineData("left", "left", "66.6667%", "33.3333%")]
        [InlineData("right", "right", "66.6667%", "33.3333%")]
        [InlineData("left", "right", "14.2857%", "85.7143%")]
        public async Task FloatsWhoseWidthsAreRoundedPercentages_StayOnOneRow(string firstSide, string secondSide, string firstWidth, string secondWidth)
        {
            var r = await RenderAsync(
                "<div style='width:525pt'>" + Float(firstSide, "#f00", $"width:{firstWidth};height:30pt") +
                Float(secondSide, "#00f", $"width:{secondWidth};height:30pt") + "</div>");

            Assert.Equal(r[Red].Top, r[Blue].Top, 1);
        }

        // An inline-block already on the line is content a right float may not overlap: one that does not fit beside it goes below the line.
        [Theory]
        [InlineData("")]
        [InlineData("text")]
        [InlineData("<p style='margin:0;height:60pt'>text</p>")]
        public async Task RightFloatThatDoesNotFitBesideAnInlineBlock_GoesBelowTheLine(string content)
        {
            var height = content.StartsWith("<p", StringComparison.Ordinal) ? "" : "height:60pt;";
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='display:inline-block;vertical-align:top;width:225pt;" + height + "background:#0f0'>" + content + "</div>" +
                Float("right", "#f00", "width:225pt;height:30pt") + Float("left", "#00f", "width:150pt;height:30pt") + "</div>");

            Assert.Equal(r[Lime].Y, r[Red].Top, 1);
            Assert.Equal(r[Lime].Y, r[Blue].Top, 1);
        }

        [Fact]
        public async Task RightFloatThatFitsBesideAnInlineBlock_StaysOnTheLine()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='display:inline-block;vertical-align:top;width:225pt;height:60pt;background:#0f0'></div>" +
                Float("right", "#f00", "width:150pt;height:30pt") + "</div>");

            Assert.Equal(r[Lime].Top, r[Red].Top, 1);
        }

        // Relative positioning is visual only: the float in the offset box is drawn 3.75pt lower, and the floats after it are placed against
        // where it is in the flow.
        [Fact]
        public async Task FloatsAfterARelativelyPositionedBoxOfFloats_AreAgainstTheirStaticPositions()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='width:225pt;position:relative;top:3.75pt;left:5pt'>" +
                Float("right", "#0f0", "width:112.5pt;height:60pt") + Float("left", "#ff0", "width:75pt;height:15pt") + "<div style='clear:both'></div></div>" +
                Float("left", "#f00", "width:150pt;height:30pt") + Float("left", "#00f", "width:187.5pt;height:15pt") + "</div>");

            Assert.Equal(r[Lime].Y + 3.75, r[Red].Top, 1);
            Assert.Equal(r[Red].Top, r[Blue].Top, 1);
            Assert.Equal(r[Red].X + r[Red].W, r[Blue].X, 1);
        }

        // The float is in an offset box itself, and its blocker in another: they are compared in one frame, so it sits beside the blocker.
        [Fact]
        public async Task FloatInAnOffsetBox_IsPlacedBesideABlockerInAnOffsetSibling()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='position:relative;top:15pt'>" + Float("left", "#0f0", "width:75pt;height:7.5pt") + "</div>" +
                "<div style='position:relative;top:15pt;height:150pt'>" + Float("left", "#f00", "width:75pt;height:22.5pt") + "</div></div>");

            Assert.Equal(r[Lime].X + r[Lime].W, r[Red].X, 1);
            Assert.Equal(r[Lime].Top, r[Red].Top, 1);
        }

        // An inline-block that sticks out of an earlier block is on that block's line, not on the float's.
        [Fact]
        public async Task InlineBlockOverflowingAnEarlierBlock_DoesNotPushALaterFloatDown()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='height:15pt'><span style='display:inline-block;width:75pt;height:75pt;background:#0f0'></span></div>" +
                Float("right", "#f00", "width:337.5pt;height:22.5pt") + "</div>");

            Assert.Equal(r[Lime].Top - 15, r[Red].Top, 1);
        }

        [Fact]
        public async Task ClearAfterAFloatInARelativelyPositionedBox_ClearsItsStaticBottom()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='width:225pt;position:relative;top:3.75pt'>" + Float("left", "#0f0", "width:75pt;height:45pt") + "</div>" +
                Float("left", "#f00", "width:150pt;height:30pt", "clear:left") + "</div>");

            Assert.Equal(r[Lime].Y + 3.75, r[Red].Top, 1);
        }

        [Fact]
        public async Task LaterFloat_IsNotHeldBelowAnEarlierFloatByAnAncestorsRelativeOffset()
        {
            var r = await RenderAsync(
                "<div style='width:375pt'><div style='width:150pt;position:relative;top:30pt'>" + Float("left", "#0f0", "width:75pt;height:10pt") + "</div>" +
                Float("left", "#f00", "width:75pt;height:10pt") + "</div>");

            Assert.Equal(r[Lime].Top + 30, r[Red].Top, 1);
        }

        // A float that clears an earlier one goes below its margin box by its own margin-top, so two floats with 6pt margins are 12pt apart.
        [Theory]
        [InlineData("left", "left", "right")]
        [InlineData("right", "right", "left")]
        public async Task ClearedFloatsWithMargins_AreAFullMarginBoxApart(string a, string b, string d)
        {
            const string style = "width:300pt;height:42pt;margin:6pt 0;clear:both";
            var r = await RenderAsync(
                "<div style='width:750pt'>" + Float(a, "#f00", style) + Float(b, "#00f", style) + Float(d, "#0f0", style) + "</div>");

            Assert.Equal(12, r[Red].Y - r[Blue].Top, 1);
            Assert.Equal(12, r[Blue].Y - r[Lime].Top, 1);
        }

        [Fact]
        public async Task ClearedFloatWithANegativeMarginTop_ReachesBackAboveTheFloatItClears()
        {
            var r = await RenderAsync(
                "<div style='width:750pt'>" + Float("left", "#f00", "width:300pt;height:30pt", "margin:6pt 0 15pt") +
                Float("left", "#00f", "width:300pt;height:30pt", "margin:-4.5pt 0 3pt;clear:left") + "</div>");

            // `#f00` ends with a 15pt margin below it; the margin box of the next starts there, and its border box 4.5pt above.
            Assert.Equal(15 - 4.5, r[Red].Y - r[Blue].Top, 1);
        }

        // Rule 5: the third float fits beside the first at the top, but may not start above the second, which the first pushed down.
        [Fact]
        public async Task ThirdLeftFloat_SitsBesideTheSecondNotBesideTheFirst()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" + Float("left", "#f00", "width:225pt;height:40pt") + Float("left", "#00f", "width:150pt;height:40pt") +
                Float("left", "#ff0", "width:38pt;height:40pt") + "</div>");

            Assert.Equal(r[Blue].Top, r[Yellow].Top, 1);
            Assert.Equal(r[Blue].X + r[Blue].W, r[Yellow].X, 1);
        }

        // A float inside a box that is a formatting context of its own is no blocker outside it: the box clips it to 15pt, and the next
        // float starts at the box's bottom.
        [Fact]
        public async Task FloatInsideAFixedHeightOverflowHiddenSibling_DoesNotBlockALaterLeftFloat()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'><div style='overflow:hidden;height:15pt'>" + Float("right", "#f00", "width:190pt;height:60pt") + "</div>" +
                Float("left", "#00f", "width:190pt;height:20pt") + "</div>");

            Assert.Equal(r[Red].Top - 15, r[Blue].Top, 1);
        }
    }
}
