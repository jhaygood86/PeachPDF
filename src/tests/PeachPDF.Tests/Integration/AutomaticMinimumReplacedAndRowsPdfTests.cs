using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A replaced item of an overflowing column flex container floors at its natural height (CSS Flexbox 1 section 4.5), and the flexible rows of
    /// a grid with no definite height share by their flex fraction (CSS Grid 1 section 12.7.1), read back from the fills a saved PDF paints, in
    /// points. The box-by-box rules are asserted in <c>PeachBrackets.Engine.Tests</c>; these drive the same engine through a document.
    /// </summary>
    public class AutomaticMinimumReplacedAndRowsPdfTests
    {
        private record struct Fill(double X, double Y, double Width, double Height);

        private static readonly Regex FillPattern = new(
            @"\S+ \S+ \S+ rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        // 96 x 48 CSS px = 72 x 36 pt, a 2:1 picture.
        private const string Svg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

        private static async Task<List<Fill>> FillsAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0; font: 12pt Arial }</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            Assert.Single(file.Pages);

            return [.. FillPattern.Matches(file.ContentText(file.Pages[0])).Select(m => new Fill(
                double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture)))];
        }

        private static int CountWithHeight(IEnumerable<Fill> fills, double height) => fills.Count(f => Math.Abs(f.Height - height) < 0.7);

        private static string Describe(IEnumerable<Fill> fills) => string.Join("; ", fills.Select(f => $"x={f.X:0.#} w={f.Width:0.#} h={f.Height:0.#}"));

        private static readonly string[] ItemColors = ["#fcc", "#cfc", "#ccf"];

        private static string Column(string itemHtml, string itemStyle, string containerHeight, int items = 3) =>
            $"<div style='display:flex;flex-direction:column;width:200pt;height:{containerHeight}'>"
            // A different fill color per item, because a saved PDF sets a color once and repeats only the rectangle for the next same-colored fill.
            + string.Concat(Enumerable.Range(0, items).Select(n => itemHtml.Replace("STYLE", $"background:{ItemColors[n]};{itemStyle}")))
            + "</div>";

        // ─── a replaced column item ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task AStretchedPictureStopsShrinkingAtItsNaturalHeight()
        {
            var fills = await FillsAsync(Column($"<img src=\"{Svg}\" style='STYLE'>", "", "60pt"));

            Assert.True(CountWithHeight(fills, 36) == 3, Describe(fills));
            Assert.Equal(0, CountWithHeight(fills, 20));
        }

        [Fact]
        public async Task AnAuthoredZeroMinHeightLetsThePictureShrink()
        {
            var fills = await FillsAsync(Column($"<img src=\"{Svg}\" style='STYLE'>", "min-height:0", "60pt"));

            Assert.True(CountWithHeight(fills, 20) == 3, Describe(fills));
        }

        [Fact]
        public async Task ADefiniteWidthFloorsAtTheHeightItsRatioGivesIt()
        {
            // 36pt wide is 18pt tall by the 2:1 ratio, under the natural 36pt, and the column would shrink the pictures to 10pt.
            var fills = await FillsAsync(Column($"<img src=\"{Svg}\" style='STYLE'>", "width:36pt", "30pt"));

            Assert.True(CountWithHeight(fills, 18) == 3, Describe(fills));
        }

        [Fact]
        public async Task APaddedPictureFloorsAtItsOuterNaturalHeight()
        {
            var fills = await FillsAsync(Column($"<img src=\"{Svg}\" style='STYLE'>", "padding:5pt", "60pt"));

            Assert.True(CountWithHeight(fills, 46) == 3, Describe(fills));
        }

        [Fact]
        public async Task AFrameFloorsAtTheDefaultObjectHeight()
        {
            const string item = "<iframe style='border:0'></iframe>";

            // 150 CSS px is 112.5pt, the height a browser gives an iframe. A marker after the two items shows where
            // they end: 225pt down the 842pt page when they keep that height, about 95pt when the 100pt column squeezes them.
            var fills = await FillsAsync("<div style='display:flex;flex-direction:column;width:200pt;height:100pt'>"
                + item + item + "<div style='background:#ccf;height:5pt;flex-shrink:0'></div></div>");

            var marker = fills.Where(f => Math.Abs(f.Height - 5) < 0.1).ToList();
            Assert.True(marker.Count == 1, Describe(fills));
            Assert.Equal(842 - 225 - 5, marker[0].Y, 1);
        }

        // ─── flexible rows against an indefinite height ─────────────────────────────────────────

        private static string Rows(string template, string aHeight, string bHeight) =>
            $"<div style='display:grid;width:100pt;grid-template-rows:{template}'>"
            + $"<div style='background:#fcc'><div style='height:{aHeight}'></div></div><div style='background:#cfc'><div style='height:{bHeight}'></div></div></div>";

        [Fact]
        public async Task OneFrAndTwoFrOverEqualContentAreInRatio()
        {
            var fills = await FillsAsync(Rows("1fr 2fr", "30pt", "30pt"));

            Assert.True(CountWithHeight(fills, 30) >= 1 && CountWithHeight(fills, 60) >= 1, Describe(fills));
        }

        [Fact]
        public async Task TheRowAskingForTheLargestRatioSetsTheFraction()
        {
            var fills = await FillsAsync(Rows("1fr 2fr", "60pt", "30pt"));

            Assert.True(CountWithHeight(fills, 60) >= 1 && CountWithHeight(fills, 120) >= 1, Describe(fills));
        }

        [Fact]
        public async Task AnItemSpanningFlexibleRowsSetsTheFractionForThem()
        {
            // The spanning item needs 90pt across 1fr + 2fr: a fraction of 30, so rows of 30pt and 60pt.
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-rows:1fr 2fr;grid-template-columns:50pt 50pt'>"
                + "<div style='background:#fcc;grid-row:1;grid-column:1'><div style='height:10pt'></div></div>"
                + "<div style='background:#cfc;grid-row:2;grid-column:1'><div style='height:10pt'></div></div>"
                + "<div style='background:#ccf;grid-row:1 / 3;grid-column:2'><div style='height:90pt'></div></div></div>");

            Assert.True(CountWithHeight(fills, 30) >= 1 && CountWithHeight(fills, 60) >= 1 && CountWithHeight(fills, 90) >= 1, Describe(fills));
        }

        [Fact]
        public async Task AnItemSpanningOnlyAutoRowsStillGrowsTheLastOne()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-rows:1fr auto;grid-template-columns:50pt 50pt'>"
                + "<div style='background:#fcc;grid-row:1;grid-column:1'><div style='height:10pt'></div></div>"
                + "<div style='background:#ccf;grid-row:1 / 3;grid-column:2'><div style='height:40pt'></div></div></div>");

            Assert.True(CountWithHeight(fills, 40) >= 1, Describe(fills));
        }

        [Fact]
        public async Task ATallerMinHeightIsSharedByTheFlexFactors()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;min-height:120pt;grid-template-rows:1fr 2fr'>"
                + "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div>");

            Assert.True(CountWithHeight(fills, 40) >= 1 && CountWithHeight(fills, 80) >= 1, Describe(fills));
        }
    }
}
