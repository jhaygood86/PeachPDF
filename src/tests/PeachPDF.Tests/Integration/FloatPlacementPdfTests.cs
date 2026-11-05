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
