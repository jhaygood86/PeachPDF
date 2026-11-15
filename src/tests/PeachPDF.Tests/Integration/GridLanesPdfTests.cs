using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A grid-lanes container written to a paged PDF and read back from the fills it paints: an item that may not be cut moves to the next page with the
    /// items below it in its lane, and the other lane carries on where it was.
    /// </summary>
    public class GridLanesPdfTests
    {
        private record struct Fill(double X, double Y, double Width, double Height);

        private static readonly Regex FillPattern = new(
            @"\S+ \S+ \S+ rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private static List<Fill> FillsOf(PdfFile file, int page) =>
            [.. FillPattern.Matches(file.ContentText(file.Pages[page])).Select(m => new Fill(
                double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture)))];

        [Fact]
        public async Task AnItemThatMayNotBeCut_MovesToTheNextPage_WhileTheOtherLaneCarriesOn()
        {
            // Page height is about 420pt. Lane one holds a (300pt) then d; lane two holds b (250pt), and c (300pt, never cut) would end at 550pt, past
            // the page, so it starts the next page - and d, which fits below a, stays on the first.
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0 }" +
                ".l { display: grid-lanes; width: 200pt; grid-template-columns: 100pt 100pt; fit-tolerance: 0 }" +
                "</style></head><body><div class='l'>" +
                "<div style='height:300pt; background:#ff0000'></div>" +
                "<div style='height:250pt; background:#00ff00'></div>" +
                "<div style='height:300pt; background:#0000ff; break-inside:avoid'></div>" +
                "<div style='height:50pt; background:#ffff00'></div>" +
                "</div></body></html>";

            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A6)).Inspect();
            Assert.True(file.Pages.Count >= 2, $"pages: {file.Pages.Count}");

            var first = FillsOf(file, 0);
            var second = FillsOf(file, 1);
            var pageHeight = 419.53;

            // The 300pt blue item is on the second page, flush with its top; the red and green ones are on the first.
            Assert.Contains(second, f => Math.Abs(f.Height - 300) < 1 && Math.Abs(f.Y + f.Height - pageHeight) < 1.5);
            Assert.DoesNotContain(first, f => Math.Abs(f.Height - 300) < 1 && Math.Abs(f.Y + f.Height - pageHeight) > 1.5 && f.X > 50);
            Assert.Contains(first, f => Math.Abs(f.Height - 250) < 1);
        }
    }
}
