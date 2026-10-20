using PeachBrackets.Html.Dom;
using PeachPDF;
using PeachDrawing.Core;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Paint
{
    /// <summary>
    /// Table track backgrounds, <see href="https://www.w3.org/TR/CSS21/tables.html#table-layers">CSS 2.1 §17.5.1</see>: a
    /// column group's, column's, row group's or row's background "covers exactly the full area of all cells that originate
    /// in" it, beneath every cell, and the <c>border-spacing</c> between cells is the table's.
    /// </summary>
    /// <remarks>
    /// Every fill is asserted against the cells' own rectangles, so each test says which cell a background lands on rather
    /// than only that something was filled. Before this, a track filled its whole rectangle in tree order: a later row's
    /// background covered a cell spanning into it from an earlier row, and every track filled the gaps between its cells.
    /// </remarks>
    public class TableTrackBackgroundTests
    {
        private static async Task<(CssBox Root, List<PaintOp> Log)> PaintAsync(string body, int page = 0)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, page);

            return (root, graphics.Log);
        }

        private static Rect RectOf(CssBox root, string id)
        {
            var box = LayoutHarness.FindById(root, id)!;
            return new Rect(box.Location.X, box.Location.Y, box.ActualRight - box.Location.X, box.ActualBottom - box.Location.Y);
        }

        private static List<Rect> Fills(IEnumerable<PaintOp> log) =>
            log.Where(op => op.Kind == PaintOpKind.FillRect).Select(op => op.Bounds).ToList();

        private static bool Same(Rect a, Rect b) =>
            Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01
            && Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01;

        [Fact]
        public async Task ARowsBackground_CoversItsCellsOnly_IncludingARowspanCellOverItsWholeHeight()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:4pt'>" +
                "<tr style='background:#f88'><td id='a' rowspan='2'>a</td><td id='b'>b</td></tr>" +
                "<tr style='background:#ccc'><td id='c'>c</td></tr></table>");

            var (a, b, c) = (RectOf(root, "a"), RectOf(root, "b"), RectOf(root, "c"));
            var fills = Distinct(Fills(log));

            // Row 1's colour over a's whole height and over b; row 2's over c alone. Nothing else: no fill across a row's
            // width, which would cover the gaps between cells and, for row 2, the lower half of a.
            Assert.True(a.Height > c.Height * 1.5, "a has to span both rows, or this asserts nothing");
            Assert.Equal(3, fills.Count);
            Assert.Contains(fills, f => Same(f, a));
            Assert.Contains(fills, f => Same(f, b));
            Assert.Contains(fills, f => Same(f, c));
        }

        /// <summary>
        /// The fills at different rectangles: a <c>rowspan</c> cell's fills can repeat at the same one, which is how many times
        /// it is drawn, not where - the subject of <c>TableRowspanCellPaintedOnceTests</c>, not of these.
        /// </summary>
        private static List<Rect> Distinct(List<Rect> fills)
        {
            List<Rect> distinct = [];
            foreach (var fill in fills)
            {
                if (!distinct.Exists(d => Same(d, fill))) distinct.Add(fill);
            }

            return distinct;
        }

        [Fact]
        public async Task ALaterRowsBackground_NeverPaintsOverACellSpanningIntoIt()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:collapse'>" +
                "<tr><td id='a' rowspan='3' style='background:#fdd'>a</td><td>r0</td></tr>" +
                "<tr style='background:#cde'><td>r1</td></tr><tr><td>r2</td></tr></table>");

            var a = RectOf(root, "a");
            var overA = Distinct(Fills(log).Where(f => Rect.Intersect(f, a) is { Width: > 0.01, Height: > 0.01 }).ToList());

            // Only a's own fill lands on a: the stripe on the second row stays behind r1.
            Assert.Single(overA);
            Assert.True(Same(overA[0], a), $"the fill over a is {overA[0]}, a is {a}");
        }

        [Fact]
        public async Task AColumnsBackground_CoversTheCellsStartingInIt_AndAColspanCellTakesItsFirstColumns()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:3pt'><col style='background:#ccf'><col>" +
                "<tr><td id='a'>a</td><td id='b'>b</td></tr><tr><td id='ab' colspan='2'>ab</td></tr></table>");

            var fills = Fills(log);

            Assert.Equal(2, fills.Count);
            Assert.Contains(fills, f => Same(f, RectOf(root, "a")));
            Assert.Contains(fills, f => Same(f, RectOf(root, "ab")));
        }

        [Fact]
        public async Task ACellPaintsItsTracksBackgrounds_BeneathItsOwn_ColumnGroupFirstAndRowLast()
        {
            var (root, log) = await PaintAsync(
                "<table><colgroup style='background:#efe'><col style='background:#ccf'></colgroup>" +
                "<tbody style='background:#ffd'><tr style='background:#fcc'><td id='a' style='background:#fff'>a</td></tr></tbody></table>");

            var a = RectOf(root, "a");
            var fills = Fills(log);

            // Five layers, all over the cell and nowhere else, then the cell's text on top of them.
            Assert.Equal(5, fills.Count);
            Assert.All(fills, f => Assert.True(Same(f, a), $"fill {f} is not cell a {a}"));
            Assert.True(log.FindLastIndex(op => op.Kind == PaintOpKind.FillRect) < log.FindIndex(op => op.Kind == PaintOpKind.DrawString));
        }

        [Fact]
        public async Task ARowWithAMissingCell_LeavesTheMissingCellUnpainted()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:0'><tbody style='background:#ffd'>" +
                "<tr><td id='a'>a</td><td id='b'>b</td></tr><tr><td id='c'>c</td></tr></tbody></table>");

            // The group's colour over its three cells, and nowhere else - not over the slot beside c that no cell fills.
            var fills = Fills(log);
            Assert.Equal(3, fills.Count);
            Assert.All(new[] { "a", "b", "c" }, id => Assert.Contains(fills, f => Same(f, RectOf(root, id))));
        }

        [Fact]
        public async Task ARepeatedHeaderGroupsBackground_IsPaintedUnderItsCellsOnEveryPage()
        {
            var rows = string.Concat(Enumerable.Range(1, 30).Select(i => $"<tr><td>r{i}</td><td>r{i}</td></tr>"));
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<table><thead style='background:#9cf'><tr><th>H1</th><th>H2</th></tr></thead><tbody>{rows}</tbody></table>"),
                pageHeight: 200, margin: 20);

            var pages = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(pages >= 2, "the table has to repeat its header on a second page, or this asserts nothing");

            for (var page = 0; page < pages; page++)
            {
                using var graphics = new RecordingGraphics(new HtmlRenderContext());
                FragmentPaintHarness.PaintPage(container, graphics, page);

                var fills = Fills(graphics.Log);
                foreach (var header in graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString && op.Text is "H1" or "H2"))
                {
                    Assert.Contains(fills, f => f.Contains(header.Bounds.X + header.Bounds.Width / 2, header.Bounds.Y + header.Bounds.Height / 2));
                }
            }
        }

        /// <summary>
        /// A repeated <c>&lt;thead&gt;</c> is detached from the table's children for layout and keeps the table only as its DOM
        /// parent, so a header cell finds its column through that.
        /// </summary>
        [Fact]
        public async Task AHeaderCell_ShowsItsColumnsBackground()
        {
            var (_, log) = await PaintAsync(
                "<table><col style='background:#ccf'><col><thead><tr><th>H1</th><th>H2</th></tr></thead>" +
                "<tbody><tr><td>b1</td><td>b2</td></tr></tbody></table>");

            var fills = Fills(log);
            var h1 = log.First(op => op.Kind == PaintOpKind.DrawString && op.Text == "H1").Bounds;
            var h2 = log.First(op => op.Kind == PaintOpKind.DrawString && op.Text == "H2").Bounds;

            Assert.Contains(fills, f => f.Contains(h1.X + h1.Width / 2, h1.Y + h1.Height / 2));
            Assert.DoesNotContain(fills, f => f.Contains(h2.X + h2.Width / 2, h2.Y + h2.Height / 2));
        }

        /// <summary>
        /// <c>empty-cells</c> applies to the separated borders model only (CSS 2.1 §17.6.1.1): a collapsed table's empty cell is
        /// painted, with the backgrounds of the tracks beneath it.
        /// </summary>
        [Fact]
        public async Task AnEmptyCellOfACollapsedTable_ShowsItsTracksBackgrounds_WhateverEmptyCellsSays()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:collapse;empty-cells:hide'><tr style='background:#fcc'>" +
                "<td id='a'>a</td><td id='empty' style='width:20pt'></td></tr></table>");

            var fills = Distinct(Fills(log));

            Assert.Contains(fills, f => Same(f, RectOf(root, "a")));
            Assert.Contains(fills, f => Same(f, RectOf(root, "empty")));
        }

        [Fact]
        public async Task AHiddenEmptyCell_ShowsNoneOfItsTracksBackgrounds()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;empty-cells:hide'><tr style='background:#fcc'>" +
                "<td id='a'>a</td><td id='empty' style='width:20pt'></td></tr></table>");

            var fills = Fills(log);

            Assert.Single(fills);
            Assert.True(Same(fills[0], RectOf(root, "a")));
        }

        [Fact]
        public async Task ARowspanCellContinuingOntoTheNextPage_CarriesItsRowsBackgroundThere()
        {
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" +
                "<table style='border-collapse:separate;border-spacing:0'>" +
                "<tr style='background:#f88'><td id='a' rowspan='2'>a</td><td><div style='height:15pt'>b</div></td></tr>" +
                "<tr><td id='tall'><div style='height:300pt'>c</div>d</td></tr></table></body></html>",
                new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 2, "the cell has to continue onto a second page, or this asserts nothing");

            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 1);

            var a = LayoutHarness.FindById(root, "a")!;
            var inA = Fills(graphics.Log).Where(f => f.Left >= a.Location.X - 0.01 && f.Right <= a.ActualRight + 0.01).ToList();

            Assert.NotEmpty(inA);
        }
    }
}
