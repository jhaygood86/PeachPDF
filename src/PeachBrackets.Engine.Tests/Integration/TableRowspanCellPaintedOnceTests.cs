using PeachDrawing.Core;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
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

        private const string SpanningTableUnderARepeatedHeader =
            "<table style='border-collapse:separate'><thead><tr><th>w0_1</th><th>w0_2</th></tr></thead><tbody>" +
            "<tr><td rowspan='2' style='{0}'>w1_1</td><td><div style='height:300pt'>w2_0</div></td></tr>" +
            "<tr><td>w2_1</td></tr></tbody></table>";

        /// <summary>
        /// Under a repeated header the cell's own row reaches the next page with nothing inside the band it is clipped to, so the
        /// placeholder in the next row is what draws the continuation; keeping the own row's empty fragment instead lost it.
        /// </summary>
        [Theory]
        [InlineData("background:#fdd")]
        [InlineData("border:2px solid #36c")]
        public async Task ARowspanCellContinuingUnderARepeatedHeader_IsDecoratedOnceOnTheNextPage(string decoration)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Page(string.Format(SpanningTableUnderARepeatedHeader, decoration)), new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

            Assert.True(container.FragmentTree!.Fragmentainers.Count == 2, "the cell has to continue onto a second page, or this asserts nothing");

            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 1);
            var strips = VisibleDecoration(graphics.Log);

            Assert.NotEmpty(strips);
            Assert.Equal(strips.Count, strips.Distinct().Count());
        }

        /// <summary>The reference walk the pruning check builds starts from no spanning cell drawn, as the pruned walk did.</summary>
        [Fact]
        public async Task ARowspanCellContinuingUnderARepeatedHeader_PrunesWithoutChangingTheTree()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                Page(string.Format(SpanningTableUnderARepeatedHeader, "background:#fdd")), pageHeight: 250, margin: 20,
                prepare: root => root.HtmlContainer!.VerifyFragmentPruningOverride = true);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 2, "the cell has to continue onto a second page, or this asserts nothing");
        }

        /// <summary>The fills and border strips of a paint log that some of each lies inside every clip in force.</summary>
        private static List<Rect> VisibleDecoration(IEnumerable<PaintOp> log)
        {
            List<Rect> visible = [];
            Stack<Rect?> clips = [];

            foreach (var op in log)
            {
                switch (op.Kind)
                {
                    case PaintOpKind.PushClip: clips.Push(op.Bounds); break;
                    case PaintOpKind.PushClipPath: clips.Push(null); break;
                    case PaintOpKind.PopClip: clips.Pop(); break;
                    case PaintOpKind.FillRect or PaintOpKind.Polygon:
                        var shown = clips.Aggregate(op.Bounds, (r, clip) => clip is { } c ? Rect.Intersect(r, c) : r);
                        if (!shown.IsEmpty) visible.Add(op.Bounds);
                        break;
                }
            }

            return visible;
        }
    }
}
