using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box taller than a page fragments like other content: it breaks between its
    /// lines, moves an unbreakable block or a break-inside:avoid block whole, and repeats a table's header on
    /// each page (css-position-3 and css-break-3). It used to be laid out unbroken and sliced at each page edge.
    /// </summary>
    public class TallAbsoluteBoxFragmentationTests
    {
        private const string Page = "@page { size: 300pt 200pt; margin: 20pt } body { margin: 0; font: 10pt/12pt Arial } p { margin: 0 }";

        private static string Document(string box) =>
            $"<!DOCTYPE html><html><head><style>{Page} th, td {{ padding: 0 }}</style></head><body>"
            + "<p>P1</p><p>P2</p><p>P3</p>" + box
            + string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>Q{i}</p>")) + "</body></html>";

        private static async Task<(PeachPDF.Html.Core.Dom.CssBox Root, HtmlContainerInt Container, List<string> Painted)> RenderAsync(string html)
        {
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig());

            List<string> painted = [];
            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                using var graphics = new RecordingGraphics(new PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, graphics, page);
                painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
            }

            return (root, container, painted);
        }

        [Fact]
        public async Task ATallAbsoluteBox_BreaksBetweenItsLines_AndKeepsWhatFollowsIt()
        {
            var lines = string.Join("<br>", Enumerable.Range(1, 37).Select(i => $"W{i}"));
            var (root, container, painted) = await RenderAsync(
                Document($"<div id='box' style='position:absolute;top:0;right:0;width:60pt'>{lines}</div>"));

            // No line straddles a page edge: each is drawn whole on the page its top is on.
            var box = LayoutHarness.FindById(root, "box")!;
            foreach (var word in box.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak && !w.IsSpaces))
            {
                Assert.Equal(container.PageIndexOf(word.Top + HtmlContainerInt.PageBoundaryEpsilon),
                    container.PageIndexOf(word.Bottom - HtmlContainerInt.PageBoundaryEpsilon));
            }

            // Every word of the box and every paragraph after it is drawn exactly once.
            Assert.All(Enumerable.Range(1, 37), i => Assert.Single(painted, $"W{i}"));
            Assert.All(Enumerable.Range(1, 20), i => Assert.Single(painted, $"Q{i}"));
        }

        [Fact]
        public async Task ABreakInsideAvoidBlockInATallAbsoluteBox_IsNotSlicedAtAPageEdge()
        {
            var blocks = string.Concat(Enumerable.Range(1, 12).Select(i =>
                $"<div id='b{i}' style='break-inside:avoid;border:1px solid #888;margin:0 0 3pt'>B{i}a<br>B{i}b<br>B{i}c</div>"));
            var (root, container, painted) = await RenderAsync(
                Document($"<div style='position:absolute;top:0;right:0;width:200pt'>{blocks}</div>"));

            for (var i = 1; i <= 12; i++)
            {
                var block = LayoutHarness.FindById(root, $"b{i}")!;

                Assert.Equal(container.PageIndexOf(block.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                    container.PageIndexOf(block.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon));
            }

            Assert.All(Enumerable.Range(1, 12), i => Assert.Single(painted, $"B{i}a"));
        }

        [Fact]
        public async Task ATableInATallAbsoluteBox_BreaksBetweenRowsAndRepeatsItsHeader()
        {
            var rows = string.Concat(Enumerable.Range(1, 29).Select(i => $"<tr><td>R{i}</td></tr>"));
            var (_, container, painted) = await RenderAsync(
                Document($"<div style='position:absolute;top:0;right:0;width:100pt'><table border='1'><thead><tr><th>HEAD</th></tr></thead>{rows}</table></div>"));

            Assert.All(Enumerable.Range(1, 29), i => Assert.Single(painted, $"R{i}"));

            // One header per page the table spans: it is repeated, not drawn once.
            var pagesWithRows = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(painted.Count(t => t == "HEAD") >= 3, "the header repeats on the pages the rows continue onto");
            Assert.True(painted.Count(t => t == "HEAD") <= pagesWithRows);
        }
    }
}
