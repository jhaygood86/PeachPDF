using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>rowspan</c> cell's content is painted once, however many rows it spans
    /// (<see href="https://www.w3.org/TR/css-tables-3/#terminology">css-tables-3 §2.1.1</see>: "table-cells which cover
    /// more than one slot do so densely" — one cell over several slots, not one cell per slot).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fragment emitter reaches a spanning cell through its own row and again through every <c>CssSpacingBox</c>
    /// standing in for it in the rows it covers. Without the walk keeping only the cell's first fragment, every word of a
    /// cell spanning N rows was drawn 2N−1 times on top of itself, which reads as bold and extracts 2N−1 times.
    /// </para>
    /// <para>
    /// These are word censuses of the paint log, not of the fragment tree, because the paint log is what a reader
    /// sees; <see cref="ATableWithoutRowspan_PaintsEveryWordOnce"/> is the control that stops "no word doubled" passing
    /// against a census that never sees a table cell at all.
    /// </para>
    /// </remarks>
    public class TableRowspanCellPaintedOnceTests
    {
        private static string Page(string body) =>
            "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
            "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

        private static string SpanningTable(int span, string borderModel) =>
            $"<table style='border-collapse:{borderModel}'>" +
            $"<tr><td rowspan='{span}'>w1_1 w1_2</td><td>w2_0</td></tr>" +
            string.Concat(Enumerable.Range(1, span - 1).Select(i => $"<tr><td>w2_{i}</td></tr>")) +
            "</table>";

        [Theory]
        [InlineData(2, "collapse")]
        [InlineData(3, "collapse")]
        [InlineData(5, "collapse")]
        [InlineData(2, "separate")]
        public async Task ARowspanCellOnOnePage_IsPaintedOnce(int span, string borderModel)
        {
            var body = SpanningTable(span, borderModel);

            var (painted, pages) = await PaintedWords.LayOutAndPaintAsync(Page(body));
            var (lost, doubled) = PaintedWords.Diff(body, painted);

            Assert.Equal(1, pages);
            Assert.Empty(lost);
            Assert.Empty(doubled);
        }

        /// <summary>The control: the same slots with no span paint every word once.</summary>
        [Fact]
        public async Task ATableWithoutRowspan_PaintsEveryWordOnce()
        {
            const string body = "<table style='border-collapse:collapse'><tr><td>w1_1 w1_2</td><td>w2_0</td></tr><tr><td></td><td>w2_1</td></tr></table>";

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(Page(body));
            var (lost, doubled) = PaintedWords.Diff(body, painted);

            Assert.True(painted.Count == 4, $"the census has to see the cells, or this asserts nothing; it saw {painted.Count} words");
            Assert.Empty(lost);
            Assert.Empty(doubled);
        }
    }
}
