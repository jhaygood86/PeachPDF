using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// css-break-3 §4.4: an <c>avoid</c> on a break point between table rows (break-after on the earlier
    /// row, break-before on the later one, break-inside on a row group holding both) moves the earlier
    /// rows to the next page with the later one, unless that cannot be satisfied.
    /// </summary>
    public class TableRowBreakAvoidIntegrationTests
    {
        private const double PageHeight = 842.0;

        // 41 plain 20pt rows end at 820pt; the 20pt heading row ends at 840pt (fits), the first data
        // row would end at 860pt and does not.
        private static string Page(string headAttr, string firstAttr, string tbodyAttr = "", int spacers = 41,
            string rowsAfter = "")
        {
            var sb = new StringBuilder("""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; } table { border-collapse: collapse; width: 100%; }
                td { padding: 0; height: 20pt; font-size: 8px; line-height: 10pt; }
                </style></head><body><table><tbody>
                """);
            for (var r = 0; r < spacers; r++) sb.Append($"<tr><td>s{r}</td></tr>");
            sb.Append("</tbody>");
            sb.Append($"<tbody {tbodyAttr}><tr id='head' {headAttr}><td>HEAD</td></tr><tr id='first' {firstAttr}><td>FIRST</td></tr>{rowsAfter}</tbody>");
            sb.Append("</table></body></html>");
            return sb.ToString();
        }

        private static async Task<CssBox> Layout(string html)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, PageHeight);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static double Y(CssBox root, string id) => DomUtils.GetBoxById(root, id)!.Location.Y;

        [Fact]
        public async Task WithoutAvoid_HeadingStaysOnTheFirstPageAndIsStranded()
        {
            var root = await Layout(Page("", ""));

            Assert.True(Y(root, "head") < PageHeight);
            Assert.True(Y(root, "first") >= PageHeight);
        }

        [Theory]
        [InlineData("style='break-after: avoid'", "")]
        [InlineData("style='page-break-after: avoid'", "")]
        [InlineData("", "style='break-before: avoid'")]
        public async Task AvoidBetweenTheRows_MovesTheHeadingWithTheRowAfterIt(string head, string first)
        {
            var root = await Layout(Page(head, first));

            Assert.True(Y(root, "head") >= PageHeight, $"head at {Y(root, "head")}");
            Assert.True(Y(root, "first") > Y(root, "head"));
            Assert.True(Y(root, "first") < 2 * PageHeight);
        }

        [Fact]
        public async Task BreakInsideAvoidOnTheRowGroup_KeepsTheGroupWhole()
        {
            var root = await Layout(Page("", "", "style='break-inside: avoid'"));

            Assert.True(Y(root, "head") >= PageHeight);
            Assert.True(Y(root, "first") > Y(root, "head"));
        }

        [Fact]
        public async Task ARunOfAvoidRows_TravelsTogether()
        {
            var root = await Layout(Page("style='break-after: avoid'", "style='break-after: avoid'",
                rowsAfter: "<tr id='third'><td>THIRD</td></tr>", spacers: 40));

            // 40 spacers end at 800; head 800-820, first 820-840 fit, third would end at 860.
            Assert.True(Y(root, "head") >= PageHeight);
            Assert.True(Y(root, "third") > Y(root, "first"));
        }

        [Fact]
        public async Task ForcedBreakWinsOverAvoid()
        {
            var root = await Layout(Page("style='break-after: avoid'", "style='break-before: page'"));

            // The forced break is honored where it is: the heading stays put.
            Assert.True(Y(root, "head") < PageHeight);
            Assert.True(Y(root, "first") >= PageHeight);
        }

        [Fact]
        public async Task RunHeadAtThePageTop_IsNotMovedAndLayoutTerminates()
        {
            // Every row avoids a break after it, so the whole table is one run taller than a page: the
            // avoid is unsatisfiable and is relaxed instead of looping or leaving an empty page.
            var sb = new StringBuilder("""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; } table { border-collapse: collapse; width: 100%; }
                td { padding: 0; height: 20pt; font-size: 8px; }
                tr { break-after: avoid; }
                </style></head><body><table>
                """);
            for (var r = 0; r < 90; r++) sb.Append($"<tr id='r{r}'><td>r{r}</td></tr>");
            sb.Append("</table></body></html>");

            var root = await Layout(sb.ToString());

            Assert.True(Y(root, "r89") >= PageHeight);
            Assert.True(Y(root, "r0") < 1);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(11)]
        public async Task GroupedReport_NoGroupHeadingIsLeftAloneAtThePageFoot(int seed)
        {
            // The report from the issue: a heading row per group, a variable number of data rows after.
            var rnd = new Random(seed);
            var sb = new StringBuilder("""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; font-family: Arial; font-size: 10pt }
                table { width: 100%; border-collapse: collapse } td { padding: 2px }
                .gh { break-after: avoid; page-break-after: avoid }
                </style></head><body><table><thead><tr><th>A</th><th>B</th></tr></thead>
                """);
            for (var g = 0; g < 40; g++)
            {
                sb.Append($"<tr class='gh' id='h{g}'><td colspan='2'>HEAD{g}</td></tr><tr id='f{g}'><td>FIRST{g}</td><td>1</td></tr>");
                for (var r = rnd.Next(0, 9); r > 0; r--) sb.Append("<tr><td>row</td><td>2</td></tr>");
            }
            sb.Append("</table></body></html>");

            var root = await Layout(sb.ToString());

            for (var g = 0; g < 40; g++)
            {
                var head = DomUtils.GetBoxById(root, $"h{g}")!;
                var first = DomUtils.GetBoxById(root, $"f{g}")!;
                Assert.True(first.Location.Y > head.Location.Y);
                Assert.Equal(Math.Floor(head.ActualBottom / PageHeight - 0.001), Math.Floor(first.Location.Y / PageHeight));
            }
        }
    }
}
