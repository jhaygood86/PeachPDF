using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A multi-column container's shrink-to-fit width is its column repeated side by side plus the gaps (the widths are those headless Chrome
    /// gives). The layout rules are asserted box by box in <c>PeachBrackets.Engine.Tests</c> (<c>MulticolIntrinsicWidthTests</c>); these read
    /// the same widths back from the fills a saved PDF paints, in CSS pixels as an author writes them.
    /// </summary>
    public class MulticolIntrinsicWidthPdfTests
    {
        private readonly record struct Rect(double X, double Y, double W, double H);

        private static async Task<Dictionary<string, Rect>> FillsOf(string body)
        {
            var generator = new PdfGenerator();
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }}</style></head><body>{body}</body></html>";
            var file = (await generator.GeneratePdf(html, PageSize.A4)).Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }
            return rects;
        }

        private const string Blue = "0.8 0.8 1";

        private static string Box(string color, string style = "") => $"<div style='width:100px;height:10px;background:{color};{style}'></div>";

        private static void AssertPx(double expectedPx, double actualPt, string what) =>
            Assert.True(Math.Abs(expectedPx * 0.75 - actualPt) < 0.5, $"{what}: expected {expectedPx}px, was {actualPt / 0.75}px");

        [Fact]
        public async Task TwoColumnsInAFloat_AreTwiceTheColumnPlusTheGap()
        {
            var rects = await FillsOf("<div style='float:left;columns:2;column-gap:10px;background:#ccf'>" + Box("#fcc") + Box("#cfc") + "</div>");

            AssertPx(210, rects[Blue].W, "the float");
        }

        [Fact]
        public async Task AChildsMaxWidthCapsTheColumn_NotTheContainer()
        {
            var rects = await FillsOf("<div style='float:left;columns:2;column-gap:10px;background:#ccf'>" +
                "<div style='width:230px;max-width:100px;height:10px;background:#fcc'></div><div style='width:230px;max-width:100px;height:10px;background:#cfc'></div></div>");

            AssertPx(210, rects[Blue].W, "the float");
        }

        [Fact]
        public async Task ThreeColumnsOfOneBlock_ReserveAllThreeColumnsAndBothGaps()
        {
            var rects = await FillsOf("<div style='float:left;columns:3;column-gap:10px;background:#ccf'>" + Box("#fcc") + "</div>");

            AssertPx(320, rects[Blue].W, "the float");
        }

        [Fact]
        public async Task BorderAndPaddingAreAddedOnce_AndANormalGapIsOneEm()
        {
            var rects = await FillsOf("<div style='float:left;columns:2;padding:0 7px;border:3px solid #000;background:#ccf;font-size:16px'>" + Box("#fcc") + "</div>");

            // 2 * 100 + 16 (normal gap: 1em) + 2 * 7 + 2 * 3.
            AssertPx(236, rects[Blue].W, "the float");
        }

        [Fact]
        public async Task APercentageGapCountsAsNoneAndOneColumnIsUnchanged()
        {
            var percent = await FillsOf("<div style='float:left;columns:2;column-gap:20%;background:#ccf'>" + Box("#fcc") + "</div>");
            var single = await FillsOf("<div style='float:left;columns:1;column-gap:10px;background:#ccf'>" + Box("#fcc") + "</div>");

            AssertPx(200, percent[Blue].W, "two columns with a percentage gap");
            AssertPx(100, single[Blue].W, "one column");
        }

        [Fact]
        public async Task AColumnWidth_FloorsTheColumn_AndWithACountIsRepeated()
        {
            var width = await FillsOf("<div style='float:left;columns:80px;column-gap:10px;background:#ccf'><div style='width:20px;height:10px'></div></div>");
            var both = await FillsOf("<div style='float:left;columns:3 80px;column-gap:10px;background:#ccf'><div style='width:20px;height:10px'></div></div>");
            var wider = await FillsOf("<div style='float:left;columns:2 80px;column-gap:10px;background:#ccf'>" + Box("#fcc") + "</div>");

            AssertPx(80, width[Blue].W, "column-width only");
            AssertPx(260, both[Blue].W, "three columns of 80px");
            AssertPx(210, wider[Blue].W, "content wider than the column-width");
        }

        [Fact]
        public async Task ShrunkToItsMinimum_AColumnCountMultipliesIt_AndAColumnWidthCapsIt()
        {
            var count = await FillsOf("<div style='width:1px'><div style='float:left;columns:2;column-gap:10px;background:#ccf'>" + Box("#fcc") + Box("#cfc") + "</div></div>");
            var width = await FillsOf("<div style='width:1px'><div style='float:left;columns:80px;column-gap:10px;background:#ccf'>" + Box("#fcc") + "</div></div>");

            AssertPx(210, count[Blue].W, "the count's minimum");
            AssertPx(80, width[Blue].W, "the column-width's minimum");
        }

        [Fact]
        public async Task AMulticolContainerInsideABlockInsideAFloat_IsMeasuredToo()
        {
            var rects = await FillsOf("<div style='float:left;background:#ccf'><div><div style='columns:2;column-gap:10px'>" + Box("#fcc") + Box("#cfc") + "</div></div></div>");

            AssertPx(210, rects[Blue].W, "the float");
        }

        [Fact]
        public async Task AVerticalWritingModeContainerIsLeftAlone()
        {
            var rects = await FillsOf("<div style='float:left;writing-mode:vertical-rl;columns:2;column-gap:10px;background:#ccf'>text text</div>");

            // One line wide, not two columns of the horizontal text plus a gap.
            Assert.InRange(rects[Blue].W, 1, 30);
        }

        [Fact]
        public async Task AColumnSpanner_IsNotPartOfTheColumn_ButSetsAFloorOnTheWidth()
        {
            var narrow = await FillsOf("<div style='float:left;columns:2;column-gap:10px;background:#ccf'>" + Box("#fcc") +
                "<div style='column-span:all;width:150px;height:10px'></div>" + Box("#cfc") + "</div>");
            var wide = await FillsOf("<div style='float:left;columns:2;column-gap:10px;background:#ccf'>" +
                "<div style='width:50px;height:10px'></div><div style='column-span:all;width:300px;height:10px'></div><div style='width:50px;height:10px'></div></div>");

            AssertPx(210, narrow[Blue].W, "a spanner narrower than the columns");
            AssertPx(300, wide[Blue].W, "a spanner wider than the columns");
        }
    }
}
