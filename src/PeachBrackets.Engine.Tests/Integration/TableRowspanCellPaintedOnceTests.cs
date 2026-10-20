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
    /// standing in for it in the rows it covers. Without the walk dropping the copies that draw what an earlier one already
    /// shows, every word of a cell spanning N rows was drawn 2N−1 times on top of itself, which reads as bold and extracts
    /// 2N−1 times.
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

        /// <summary>
        /// A table whose <c>rowspan</c> cell (<c>#span</c>, bordered and filled) spans one row per entry of
        /// <paramref name="heights"/> (each row's height in pt, beside the cell) and continues onto later pages, under a
        /// repeated group the rows taller than a page are displaced below (<c>thead</c>, a two-row <c>thead2</c> or
        /// <c>tfoot</c>) or under none. The group's cells carry no decoration and no <c>w#_#</c> words, so every decoration in
        /// the first column is the spanning cell's and every word in the census is a body cell's.
        /// </summary>
        private static string SpanningTableBesideTallRows(string group, string heights, string spacing, int cellHeight = 0)
        {
            var groups = group switch
            {
                "thead" => "<thead><tr><th>H1</th><th>H2</th></tr></thead>",
                "thead2" => "<thead><tr><th>H1</th><th>H2</th></tr><tr><th>H3</th><th>H4</th></tr></thead>",
                "tfoot" => "<tfoot><tr><td>F1</td><td>F2</td></tr></tfoot>",
                _ => "",
            };

            var rowHeights = heights.Split(',');
            var rows = rowHeights.Select((height, i) =>
            {
                var spanning = i == 0
                    ? $"<td id='span' rowspan='{rowHeights.Length}' style='border:2pt solid #36c;border-bottom-color:#c36;background:#fdd'>{(cellHeight > 0 ? $"<div style='height:{cellHeight}pt'>w1_1</div>" : "w1_1")}</td>"
                    : "";
                var id = i == 0 ? " id='first'" : "";

                return $"<tr>{spanning}<td{id} style='background:#eee'><div style='height:{height}pt'>w2_{i}</div></td></tr>";
            });

            return $"<table style='border-collapse:separate;border-spacing:{spacing}'>{groups}<tbody>{string.Concat(rows)}</tbody></table>";
        }

        /// <summary>
        /// On every page the spanning cell continues onto, its fill reaches as far down as the rows beside it, its bottom border
        /// strip is drawn at the bottom of its last row, and no part of it is drawn twice. Under a repeated group a row taller
        /// than a page is displaced and each copy of the cell reached through it is clipped to its band: the copies are
        /// different parts of the cell, so keeping only one of them lost the rest (with the tall row second, the part below the
        /// cell's own row; with two tall rows, the part in the second one's band).
        /// </summary>
        [Theory]
        [InlineData("thead", "15,300", "0")]
        [InlineData("thead", "15,300", "4pt")]
        [InlineData("thead2", "15,300", "0")]
        [InlineData("tfoot", "15,300", "2pt")]
        [InlineData("none", "15,300", "0")]
        [InlineData("thead", "300,15", "0")]
        [InlineData("thead2", "300,15", "4pt")]
        [InlineData("tfoot", "300,15", "0")]
        [InlineData("thead", "15,300,15", "0")]
        [InlineData("thead", "300,15,15", "0")]
        [InlineData("thead", "250,250", "0")]
        [InlineData("thead", "220,220", "0")]
        [InlineData("thead", "15,15", "0", 300)]
        [InlineData("thead", "15,15,15", "0", 300)]
        [InlineData("none", "15,15", "0", 300)]
        [InlineData("none", "300,300", "0")]
        public async Task ARowspanCellContinuingOntoLaterPages_IsDecoratedToTheBottomOfItsLastRow(string group, string heights, string spacing, int cellHeight = 0)
        {
            var html = Page(SpanningTableBesideTallRows(group, heights, spacing, cellHeight));
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
            var pages = container.FragmentTree!.Fragmentainers.Count;

            Assert.True(pages >= 2, "the cell has to continue onto a later page, or this asserts nothing");

            // The second column starts where the cell beside the spanning one does; fragments keep their boxes' x.
            var secondColumn = LayoutHarness.FindById(root, "first")!.Location.X;

            for (var page = 1; page < pages; page++)
            {
                using var graphics = new RecordingGraphics(new HtmlRenderContext());
                FragmentPaintHarness.PaintPage(container, graphics, page);
                var strips = VisibleDecoration(graphics.Log);

                var spanning = strips.Where(r => r.Right <= secondColumn + 0.5).ToList();
                var beside = strips.Where(r => r.Left >= secondColumn - 0.5).ToList();

                Assert.NotEmpty(spanning);
                Assert.NotEmpty(beside);

                // The fills of the cells beside it end where the rows do; the spanning cell's fill ends there too, and on the
                // last page so does its bottom border strip (2pt high, drawn as its own band as its colour differs).
                var rowsBottom = beside.Max(r => r.Bottom);
                Assert.True(Math.Abs(spanning.Max(r => r.Bottom) - rowsBottom) < 0.5,
                    $"page {page + 1}: the spanning cell is drawn down to {spanning.Max(r => r.Bottom)}, the rows beside it to {rowsBottom}");
                if (page == pages - 1)
                    Assert.Contains(spanning, r => r.Height <= 2.5 && r.Width > 2.5 && Math.Abs(r.Bottom - rowsBottom) < 0.5);
                Assert.Equal(strips.Count, strips.Distinct().Count());
            }

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);
            Assert.True(lost.Count == 0 && doubled.Count == 0, $"lost [{string.Join(", ", lost)}], doubled [{string.Join(", ", doubled)}]");
        }

        /// <summary>
        /// Two rows each taller than a page, under a repeated header: on the page where the first row's last band meets the
        /// second row's first, the spanning cell has a copy confined to the second row's band and one that is not confined, at
        /// the same place. The second shows the part above that band, so it is not a repeat of the first, and the cell is drawn
        /// from the top of the rows to the foot of the page.
        /// </summary>
        [Fact]
        public async Task ARowspanCellOverTwoRowsTallerThanAPage_IsDrawnAcrossBothRowsWhereTheirBandsMeet()
        {
            var html = Page(SpanningTableBesideTallRows("thead", "300,300", "0"));
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 1);
            var strips = VisibleDecoration(graphics.Log);

            var secondColumn = LayoutHarness.FindById(root, "first")!.Location.X;
            var spanning = strips.Where(r => r.Right <= secondColumn + 0.5).ToList();
            var beside = strips.Where(r => r.Left >= secondColumn - 0.5).ToList();

            Assert.True(beside.Count >= 2, "both rows have to reach this page, or this asserts nothing");
            Assert.NotEmpty(spanning);
            Assert.True(Math.Abs(spanning.Min(r => r.Top) - beside.Min(r => r.Top)) < 0.5,
                $"the spanning cell is drawn from {spanning.Min(r => r.Top)}, the rows beside it from {beside.Min(r => r.Top)}");
            Assert.True(Math.Abs(spanning.Max(r => r.Bottom) - beside.Max(r => r.Bottom)) < 0.5,
                $"the spanning cell is drawn down to {spanning.Max(r => r.Bottom)}, the rows beside it to {beside.Max(r => r.Bottom)}");
        }

        /// <summary>The reference walk the pruning check builds starts from no spanning cell drawn, as the pruned walk did.</summary>
        [Theory]
        [InlineData("300,15")]
        [InlineData("15,300")]
        public async Task ARowspanCellContinuingUnderARepeatedHeader_PrunesWithoutChangingTheTree(string heights)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                Page(SpanningTableBesideTallRows("thead", heights, "0")), pageHeight: 250, margin: 20,
                prepare: root => root.HtmlContainer!.VerifyFragmentPruningOverride = true);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 2, "the cell has to continue onto a second page, or this asserts nothing");
        }

        /// <summary>The part of each fill and border strip of a paint log that lies inside every clip in force, where some does.</summary>
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
                        if (!shown.IsEmpty) visible.Add(shown);
                        break;
                }
            }

            return visible;
        }
    }
}
