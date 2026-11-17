using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Table sizing rules as they reach the PDF: an empty table's own size, a header/footer-only table taking its height surplus,
    /// <c>rowspan="0"</c> and the <c>rules</c> attribute. Each cell's or table's rectangle is read back from the page's content stream
    /// by its fill colour. The box tree is asserted in <c>PeachBrackets.Engine.Tests</c> (<c>EmptyTableSizingIntegrationTests</c>,
    /// <c>HeaderOnlyTableHeightIntegrationTests</c>, <c>ZeroRowspanIntegrationTests</c>, <c>TableRulesAttributeIntegrationTests</c>);
    /// these run through <see cref="PdfGenerator"/>, which is what the coverage gate measures the engine through.
    /// </summary>
    public class TableLayoutPdfTests
    {
        private const string Red = "1 0 0";
        private const string Blue = "0 0 1";

        private readonly record struct Rect(double X, double Y, double W, double H);

        private static async Task<string> ContentAsync(string body)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}' }}</style></head><body>" +
                       body + "</body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();
            return file.ContentText(file.Pages[0]);
        }

        private static Dictionary<string, Rect> FilledRects(string content)
        {
            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(content))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            return rects;
        }

        [Fact]
        public async Task AnEmptyTableTakesItsSpecifiedWidthAndHeight()
        {
            var r = FilledRects(await ContentAsync(
                "<table style='width:200pt;height:50pt;border-spacing:0;background:#00f'></table>"));

            Assert.Equal(200, r[Blue].W, 1);
            Assert.Equal(50, r[Blue].H, 1);
        }

        // Both rows are empty, so neither has a natural height to be proportional to: the 100pt surplus is split evenly.
        [Fact]
        public async Task AHeaderAndFooterOnlyTableSplitsItsHeightAcrossItsEmptyRows()
        {
            var r = FilledRects(await ContentAsync(
                "<table style='height:100pt;border-spacing:0'>" +
                "<thead><tr><td style='width:50pt;padding:0;background:#f00'></td></tr></thead>" +
                "<tfoot><tr><td style='width:50pt;padding:0;background:#00f'></td></tr></tfoot>" +
                "</table>"));

            Assert.Equal(50, r[Red].H, 1);
            Assert.Equal(50, r[Blue].H, 1);
        }

        [Fact]
        public async Task ARowspanOfZeroSpansToTheLastRowOfItsRowGroup()
        {
            const string row = "<td style='height:20pt;padding:0'></td>";
            var r = FilledRects(await ContentAsync(
                "<table style='border-spacing:0'><tbody>" +
                "<tr><td rowspan='0' style='width:50pt;padding:0;background:#f00'></td>" + row + "</tr>" +
                "<tr>" + row + "</tr>" +
                "<tr>" + row + "</tr>" +
                "</tbody></table>"));

            Assert.Equal(60, r[Red].H, 1);
        }

        // rules="groups" collapses the borders and draws a line after the thead, which the same table without the attribute does not have.
        [Fact]
        public async Task TheRulesAttributeDrawsALineBetweenRowGroups()
        {
            const string groups = "<thead><tr><td>A</td></tr></thead><tbody><tr><td>B</td></tr></tbody></table>";

            var withRules = await ContentAsync("<table rules='groups'>" + groups);
            var withoutRules = await ContentAsync("<table>" + groups);

            Assert.NotEqual(withoutRules, withRules);
        }
    }
}
