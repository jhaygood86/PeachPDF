using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The percentage-height fixes from <c>CssLayoutEngine.GetBoxHeight</c>/<c>ResolveDefiniteHeightValue</c>
    /// as they reach the PDF: a box's rectangle is read back from the page's content stream by its fill
    /// colour. The box tree is asserted directly in <c>PeachBrackets.Engine.Tests</c>
    /// (<c>HeightDefinitenessTests</c>, <c>TableCellPercentageHeightIntegrationTests</c>); these run through
    /// <see cref="PdfGenerator"/>, which is what the coverage gate measures the engine through.
    /// </summary>
    public class TableCellPercentageHeightPdfTests
    {
        private const string Red = "1 0 0";
        private const string Blue = "0 0 1";

        private readonly record struct Rect(double X, double Y, double W, double H);

        private static async Task<Dictionary<string, Rect>> RenderAsync(string body)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}' }} div {{ box-sizing: border-box }}</style></head><body>" +
                       body + "</body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
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

        // An indefinite percentage height (the body's own height is auto, so `height: 100%` on a block
        // with no table-cell/table-height involvement cannot resolve) used to skip `min-height` entirely
        // and return the box's unclamped content height. It must still grow to the resolvable `min-height`.
        [Fact]
        public async Task AnIndefinitePercentageHeight_StillGrowsToAResolvableMinHeight()
        {
            var r = await RenderAsync(
                "<div style='width:60pt;height:100%;min-height:40pt;background:#f00'></div>");

            Assert.Equal(40, r[Red].H, 1);
        }

        // With no min-height (and no aspect-ratio) to clamp it, an indefinite percentage height still
        // behaves as auto end to end: the box keeps its own, unrelated content height - a plain fixed-height
        // child - rather than collapsing or growing from the now-reachable fall-through code path.
        [Fact]
        public async Task AnIndefinitePercentageHeightWithNoMinHeight_KeepsItsNaturalContentHeight()
        {
            var r = await RenderAsync(
                "<div style='width:60pt;height:100%;background:#f00'>" +
                "<div style='height:30pt'></div></div>");

            Assert.Equal(30, r[Red].H, 1);
        }

        // CSS Box Sizing 4 §5: an indefinite percentage height with a preferred aspect-ratio sizes from the
        // box's own (definite) width instead of staying auto.
        [Fact]
        public async Task AnIndefinitePercentageHeightWithAnAspectRatio_SizesFromTheDefiniteWidth()
        {
            var r = await RenderAsync(
                "<div style='width:60pt;aspect-ratio:2;height:100%;background:#f00'></div>");

            Assert.Equal(30, r[Red].H, 1);
        }

        // A table cell's own percentage height acts as auto for row sizing, instead of resolving against
        // the table directly: two 50pt rows sum to the table's own explicit 100pt, so there is no surplus
        // left to distribute - the second row's cell must stay at 50pt rather than inflating to 100pt (the
        // whole table height) on top of the first row's own natural 50pt.
        [Fact]
        public async Task ACellsOwnPercentageHeight_ActsAsAutoInsteadOfInflatingItsRow()
        {
            var r = await RenderAsync(
                "<table style='border-spacing:0;width:100pt;height:100pt'>" +
                "<tr style='height:50pt'><td style='padding:0'></td></tr>" +
                "<tr style='height:50pt'><td style='padding:0;height:100%;background:#f00'></td></tr>" +
                "</table>");

            Assert.Equal(50, r[Red].H, 1);
        }

        // A descendant of a cell with its own definite height resolves a percentage height against that
        // declared height - not against a taller, row-equalized height a sibling cell forces the row to.
        // The first cell declares 50pt; the second cell's own content (100pt) stretches the row to 100pt,
        // but the first cell's child `height: 200%` must still resolve against the first cell's own 50pt
        // (100pt), not the row's eventual 100pt (200pt).
        [Fact]
        public async Task ACellChildsPercentageHeight_ResolvesAgainstTheCellsOwnDeclaredHeight()
        {
            var r = await RenderAsync(
                "<table style='border-spacing:0'>" +
                "<tr>" +
                "<td style='padding:0;width:60pt;height:50pt'><div style='height:200%;background:#f00'></div></td>" +
                "<td style='padding:0;width:60pt'><div style='height:100pt'></div></td>" +
                "</tr></table>");

            Assert.Equal(100, r[Red].H, 1);
        }
    }
}
