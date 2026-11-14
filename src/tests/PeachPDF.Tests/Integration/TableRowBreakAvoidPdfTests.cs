using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Drawing;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-break-3 §4.4 between table body rows, read back from the written PDF: the heading row is the
    /// only red fill, so it is on the first page exactly when the break was allowed to strand it.
    /// </summary>
    public class TableRowBreakAvoidPdfTests
    {
        private static string Table(string headCell, string headRow, string firstCell, string firstRow,
            string tbody = "", int spacers = 41, string firstContent = "FIRST", string headFirst = "HEAD", string after = "")
        {
            var sb = new StringBuilder("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 0 } body { margin: 0 }
                table { border-collapse: collapse; width: 100% }
                td { padding: 0; height: 20pt; font-size: 8px; line-height: 10pt }
                .h { background: #ff0000 }
                </style></head><body><table><tbody>
                """);
            for (var r = 0; r < spacers; r++) sb.Append($"<tr><td colspan='2'>s{r}</td></tr>");
            sb.Append("</tbody>");
            sb.Append($"<tbody {tbody}><tr {headRow}><td class='h' {headCell}>{headFirst}</td></tr>");
            sb.Append($"<tr {firstRow}><td {firstCell}>{firstContent}</td></tr>{after}</tbody></table></body></html>");
            return sb.ToString();
        }

        private static async Task<(bool onFirst, bool onSecond)> HeadingPages(string html)
        {
            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var inspect = doc.Inspect();
            Assert.True(inspect.PageCount >= 2);
            return (doc.Page(0).ContentText.Contains("1 0 0 rg"), doc.Page(1).ContentText.Contains("1 0 0 rg"));
        }

        [Fact]
        public async Task WithoutAvoid_TheHeadingIsStrandedOnTheFirstPage()
        {
            var (first, _) = await HeadingPages(Table("", "", "", ""));
            Assert.True(first);
        }

        [Theory]
        [InlineData("style='break-after: avoid'", "", "", "")]
        [InlineData("style='page-break-after: avoid'", "", "", "")]
        [InlineData("", "style='break-before: avoid'", "", "")]
        [InlineData("", "", "style='break-after: avoid'", "")]
        [InlineData("", "", "", "style='break-before: avoid'")]
        public async Task AvoidBetweenTheRows_MovesTheHeadingToTheNextPage(string headRow, string firstRow, string headCell, string firstCell)
        {
            var (first, second) = await HeadingPages(Table(headCell, headRow, firstCell, firstRow));
            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public async Task BreakInsideAvoidOnTheRowGroup_KeepsItWhole()
        {
            var (first, second) = await HeadingPages(Table("", "", "", "", tbody: "style='break-inside: avoid'"));
            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public async Task TallFirstRow_StraddlesAndThePullStillMovesTheHeading()
        {
            // The first row holds a block taller than the estimate assumes, so the break is found after
            // the row is placed rather than predicted.
            var (first, second) = await HeadingPages(Table("", "style='break-after: avoid'", "", "",
                spacers: 40, firstContent: "<div style='height: 60pt'>FIRST</div>"));
            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public async Task ForcedBreakBeatsAvoid()
        {
            var (first, second) = await HeadingPages(Table("", "style='break-after: avoid'", "", "style='break-before: page'"));
            Assert.True(first);
            Assert.False(second);
        }

        [Fact]
        public async Task RowspanHeading_IsPulledBackWithItsSpan()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 0 } body { margin: 0 }
                table { border-collapse: collapse; width: 100% }
                td { padding: 0; height: 20pt; font-size: 8px; line-height: 10pt }
                .h { background: #ff0000 }
                </style></head><body><table>
                """ + string.Concat(Enumerable.Range(0, 41).Select(r => $"<tr><td colspan='2'>s{r}</td></tr>"))
                + "<tr style='break-after: avoid'><td class='h' rowspan='2'>SPAN</td><td>HEAD</td></tr>"
                + "<tr><td>FIRST</td></tr><tr><td colspan='2'>AFTER</td></tr></table></body></html>";

            var (first, second) = await HeadingPages(html);
            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public async Task AChainTallerThanAPage_IsRelaxedAndTerminates()
        {
            var sb = new StringBuilder("<style>@page{margin:0} body{margin:0} table{border-collapse:collapse} td{height:20pt;padding:0;font-size:8px} tr{break-after:avoid}</style><table>");
            for (var r = 0; r < 90; r++) sb.Append($"<tr><td>r{r}</td></tr>");
            sb.Append("</table>");

            var doc = await new PdfGenerator().GeneratePdf(sb.ToString(), PageSize.A4);
            Assert.True(doc.Inspect().PageCount >= 2);
        }
    }
}
