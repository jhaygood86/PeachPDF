using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Flexible rows are expanded against a definite container height (CSS Grid section 12.7) from their base sizes, and a track whose minimum
    /// sizing function is <c>auto</c> counts its items' automatic minimum with its exemptions (section 6.6), read back from the fills a saved PDF
    /// paints, in points. The box-by-box rules are asserted in <c>PeachBrackets.Engine.Tests</c>; these drive the same engine through a document.
    /// </summary>
    public class GridFlexibleTracksPdfTests
    {
        private record struct Fill(double X, double Y, double Width, double Height);

        private static readonly Regex FillPattern = new(
            @"\S+ \S+ \S+ rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private static async Task<List<Fill>> FillsAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0; font: 12pt Arial }</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            Assert.Single(file.Pages);

            return [.. FillPattern.Matches(file.ContentText(file.Pages[0])).Select(m => new Fill(
                double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture)))];
        }

        private static bool Tall(IEnumerable<Fill> fills, double height) => fills.Any(f => Math.Abs(f.Height - height) < 0.7);

        private static string Describe(IEnumerable<Fill> fills) => string.Join("; ", fills.Select(f => $"x={f.X:0.#} w={f.Width:0.#} h={f.Height:0.#}"));

        private const string TallContent = "<div style='height:150pt'></div>";

        private static string Rows(string template, string firstStyle = "", string firstContent = "x", string height = "height:100pt") =>
            $"<div style='display:grid;width:100pt;grid-template-rows:{template};{height}'>" +
            $"<div style='background:#fcc;{firstStyle}'>{firstContent}</div><div style='background:#cfc'>y</div></div>";

        [Fact]
        public async Task TwoFrRowsShareTheDefiniteHeight()
        {
            var fills = await FillsAsync(Rows("1fr 1fr"));

            Assert.True(fills.Count(f => Math.Abs(f.Height - 50) < 0.7) >= 2, Describe(fills));
        }

        [Fact]
        public async Task FrRowsShareWhatAnAutoRowLeaves()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;height:100pt;grid-template-rows:1fr auto 2fr'>" +
                "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div><div style='background:#ccf'>c</div></div>");

            // The auto row is one 14pt line, so the other 86pt is shared 1:2.
            Assert.True(fills.Any(f => f.Height > 28 && f.Height < 29) && fills.Any(f => f.Height > 57 && f.Height < 58), Describe(fills));
        }

        [Fact]
        public async Task ABareFrRowKeepsItsItemsAutomaticMinimumHeight()
        {
            var fills = await FillsAsync(Rows("1fr 1fr", firstContent: TallContent));

            Assert.True(Tall(fills, 150), Describe(fills));
        }

        [Theory]
        [InlineData("1fr 1fr", "overflow:hidden")]
        [InlineData("1fr 1fr", "min-height:0")]
        [InlineData("minmax(0,1fr) 1fr", "")]
        public async Task AnExemptRowShrinksToItsShare(string template, string firstStyle)
        {
            var fills = await FillsAsync(Rows(template, firstStyle, TallContent));

            Assert.True(Tall(fills, 50), Describe(fills));
        }

        [Fact]
        public async Task AMinimumLengthIsTheFloorOfAMinmaxRow()
        {
            var fills = await FillsAsync(Rows("minmax(80pt, 1fr) 1fr"));

            Assert.True(Tall(fills, 80), Describe(fills));
        }

        [Theory]
        [InlineData("minmax(min-content, 1fr) 1fr")]
        [InlineData("minmax(max-content, 1fr) 1fr")]
        public async Task AContentMinimumTakesTheItemsContent(string template)
        {
            var fills = await FillsAsync(Rows(template, firstContent: TallContent));

            Assert.True(Tall(fills, 150), Describe(fills));
        }

        [Fact]
        public async Task AnAutoHeightGridKeepsContentSizedFrRows()
        {
            var fills = await FillsAsync(Rows("1fr 1fr", height: ""));

            Assert.True(fills.All(f => f.Height < 30), Describe(fills));
        }

        [Fact]
        public async Task AFlexFactorSumBelowOneTakesThatFractionOfTheSpace()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-columns:0.5fr'><div style='background:#fcc'>x</div></div>");

            Assert.True(fills.Any(f => Math.Abs(f.Width - 50) < 0.7), Describe(fills));
        }

        [Theory]
        [InlineData("overflow:hidden")]
        [InlineData("min-width:0")]
        public async Task AnAutoColumnIsNotFlooredByAnExemptItem(string firstStyle)
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-columns:auto auto'>" +
                $"<div style='background:#fcc;{firstStyle}'>wwwwwwwwwwwwwwwwwwwwwwww</div><div style='background:#cfc'>x</div></div>");

            Assert.True(fills.Any(f => f.Width < 100), Describe(fills));
        }

        [Fact]
        public async Task AMinmaxAutoColumnIsNotFlooredByAScrollContainer()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-columns:minmax(auto,300pt) auto'>" +
                "<div style='background:#fcc;overflow:hidden'>wwwwwwwwwwwwwwwwwwwwwwww</div><div style='background:#cfc'>x</div></div>");

            Assert.True(fills.Any(f => f.Width < 100), Describe(fills));
        }
    }
}
