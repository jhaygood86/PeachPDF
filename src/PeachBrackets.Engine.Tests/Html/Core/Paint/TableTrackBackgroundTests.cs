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
                $"<tr style='background:#f88'><td id='a' rowspan='2'>a</td><td id='b'>b</td></tr>" +
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
        public async Task TrackBackgrounds_ArePaintedColumnGroupFirst_ThenColumnRowGroupAndRow_BeneathTheCellsOwn()
        {
            var (root, log) = await PaintAsync(
                "<table><colgroup style='background:#eeffee'><col style='background:#ccccff'></colgroup>" +
                "<tbody style='background:#ffffdd'><tr style='background:#ffcccc'><td id='a' style='background:#ffffff'>a</td></tr></tbody></table>");

            var a = RectOf(root, "a");
            var fills = log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();

            // Five layers, all over the cell and nowhere else, in CSS 2.1 §17.5.1's order, then the cell's text on top of them.
            Assert.All(fills, f => Assert.True(Same(f.Bounds, a), $"fill {f.Bounds} is not cell a {a}"));
            Assert.Equal(["#eeffee", "#ccccff", "#ffffdd", "#ffcccc", "#ffffff"], fills.Select(ColorOf));
            Assert.True(log.FindLastIndex(op => op.Kind == PaintOpKind.FillRect) < log.FindIndex(op => op.Kind == PaintOpKind.DrawString));
        }

        private static string? ColorOf(PaintOp op) =>
            op.Brush is SolidBrush { PaintColor: var c } ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : null;

        /// <summary>
        /// §17.5.1: the difference in area "does not affect background image positioning" - a row's gradient runs across the
        /// row, so each cell shows its own part of it rather than the whole gradient again.
        /// </summary>
        [Fact]
        public async Task ARowsGradient_RunsAcrossTheWholeRow_NotAcrossEachCell()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:0'>" +
                "<tr id='r' style='background:linear-gradient(to right, #f00, #00f)'>" +
                "<td id='a' style='width:40pt'>a</td><td id='b' style='width:40pt'>b</td></tr></table>");

            var row = RectOf(root, "r");
            var gradients = log.Where(op => op.Brush is LinearGradientBrush).ToList();

            Assert.Equal(2, gradients.Count);
            Assert.Contains(gradients, op => Same(op.Bounds, RectOf(root, "a")));
            Assert.Contains(gradients, op => Same(op.Bounds, RectOf(root, "b")));
            Assert.All(gradients, op =>
            {
                var brush = (LinearGradientBrush)op.Brush!;
                Assert.Equal(row.Left, brush.Start.X, 0.01);
                Assert.Equal(row.Right, brush.End.X, 0.01);
            });
        }

        [Fact]
        public async Task ARowsBackgroundImage_IsPositionedAgainstTheRow_AndPaintedOnlyOverItsCells()
        {
            // A 10pt image at the row's top right corner: it is drawn there, at the row's edge, and only within the cells -
            // positioned against each cell instead, it would be drawn at each cell's own right edge.
            const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='border-collapse:separate;border-spacing:0'>" +
                $"<tr id='r' style=\"background:url('data:image/png;base64,{Png}') right top / 10pt 10pt no-repeat\">" +
                "<td id='a' style='width:40pt'>a</td><td id='b' style='width:40pt'>b</td></tr></table>"));
            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 0);

            var row = RectOf(root, "r");

            Assert.NotEmpty(graphics.DrawnImageRects);
            Assert.All(graphics.DrawnImageRects, image =>
            {
                Assert.Equal(row.Right, image.Right, 0.01);
                Assert.Equal(row.Top, image.Top, 0.01);
            });

            // Clipped to the cell it is painted over each time.
            var clips = graphics.Log.Where(op => op.Kind == PaintOpKind.PushClip).Select(op => op.Bounds).ToList();
            Assert.Contains(clips, clip => Same(clip, RectOf(root, "b")));
        }

        [Fact]
        public async Task ACellsOpacity_DoesNotFadeItsRowsBackground()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><tr style='background:#ffcccc'><td style='opacity:.5;background:#ffffff'>a</td></tr></table>"));
            using var graphics = new RecordingGraphics(new HtmlRenderContext()) { RecordsLayers = true };
            FragmentPaintHarness.PaintPage(container, graphics, 0);

            var log = graphics.Log;
            var layer = log.FindIndex(op => op.Kind == PaintOpKind.BeginLayer && op.Opacity is 0.5);
            var row = log.FindIndex(op => ColorOf(op) == "#ffcccc");
            var cell = log.FindIndex(op => ColorOf(op) == "#ffffff");

            Assert.True(layer >= 0, "the cell's opacity has to make a layer, or this asserts nothing");
            Assert.InRange(row, 0, layer - 1);
            Assert.True(cell > layer, "the cell's own background is in its layer");
        }

        [Fact]
        public async Task ARowsOpacity_FadesItsOwnBackground_ButNotItsColumnsOrRowGroups()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><col style='background:#ccccff'><tbody style='background:#ffffdd'>" +
                "<tr style='opacity:.5;background:#ffcccc'><td>a</td></tr></tbody></table>"));
            using var graphics = new RecordingGraphics(new HtmlRenderContext()) { RecordsLayers = true };
            FragmentPaintHarness.PaintPage(container, graphics, 0);

            var log = graphics.Log;
            var begin = log.FindIndex(op => op.Kind == PaintOpKind.BeginLayer && op.Opacity is 0.5);
            var end = log.FindIndex(begin + 1, op => op.Kind == PaintOpKind.EndLayer);

            Assert.True(begin >= 0, "the row's opacity has to make a layer, or this asserts nothing");
            Assert.InRange(log.FindIndex(op => ColorOf(op) == "#ccccff"), 0, begin - 1);
            Assert.InRange(log.FindIndex(op => ColorOf(op) == "#ffffdd"), 0, begin - 1);
            Assert.InRange(log.FindIndex(op => ColorOf(op) == "#ffcccc"), begin + 1, end - 1);
        }

        /// <summary>
        /// CSS 2.1 §17.3 gives a column's <c>visibility</c> a meaning for <c>collapse</c> only, so a hidden column's background
        /// is still painted under its cells - while a hidden row paints nothing.
        /// </summary>
        /// <remarks>
        /// The hidden column group (and the column inheriting its visibility) paint nothing themselves, so the cell paints their
        /// backgrounds - and, to keep the layer order, the row group's and row's above them.
        /// </remarks>
        [Fact]
        public async Task AHiddenColumnsBackground_IsStillPainted_UnderItsCells()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:3pt'>" +
                "<colgroup style='visibility:hidden;background:#eeffee'><col style='background:#ccccff'></colgroup><col>" +
                "<tbody style='background:#ffffdd'><tr style='background:#ffcccc'><td id='a'>a</td><td id='b'>b</td></tr>" +
                "<tr style='visibility:hidden;background:#ddddff'><td>c</td><td>d</td></tr></tbody></table>");

            var a = RectOf(root, "a");
            var fills = log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();

            // The column group's and column's colours over a alone, beneath the row group's and row's - never the hidden row's.
            Assert.Single(fills, op => ColorOf(op) == "#ccccff");
            Assert.DoesNotContain(fills, op => ColorOf(op) == "#ddddff");
            Assert.Equal(["#eeffee", "#ccccff", "#ffffdd", "#ffcccc"], fills.Where(op => Same(op.Bounds, a)).Select(ColorOf));
        }

        /// <summary>
        /// A table split across the columns of a multi-column container has one fragment of each track per column: a cell's
        /// track background is positioned against the fragment over it, which shares the cell's column, not only its rows.
        /// </summary>
        [Fact]
        public async Task AColumnSplitAcrossMulticolColumns_PositionsItsBackgroundInTheColumnOfEachCell()
        {
            var rows = string.Concat(Enumerable.Range(1, 12).Select(i => $"<tr><td>r{i}</td></tr>"));
            var (_, log) = await PaintAsync(
                "<div style='columns:2;column-gap:40pt'>" +
                "<table style='border-collapse:separate;border-spacing:0'>" +
                $"<col style='background:linear-gradient(to right, #f00, #00f)'>{rows}</table></div>");

            var words = log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Bounds.Left).ToList();
            Assert.True(words.Max() > words.Min() + 40, "the table has to continue in the second column, or this asserts nothing");

            var gradients = log.Where(op => op.Brush is LinearGradientBrush).ToList();
            Assert.NotEmpty(gradients);
            Assert.All(gradients, op =>
            {
                var brush = (LinearGradientBrush)op.Brush!;
                Assert.InRange(brush.Start.X, op.Bounds.Left - 0.01, op.Bounds.Right + 0.01);
            });
        }

        [Fact]
        public async Task ATranslucentRowsBackground_IsPaintedOnceUnderARowspanCell()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:0'>" +
                "<tr style='background:rgba(255,0,0,.5)'><td id='a' rowspan='3'>a</td><td>r0</td></tr>" +
                "<tr><td>r1</td></tr><tr><td>r2</td></tr></table>");

            var a = RectOf(root, "a");

            Assert.Single(log, op => op.Kind == PaintOpKind.FillRect && Same(op.Bounds, a));
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

        [Fact]
        public async Task SnappedToCssPixels_ARowsBackground_CoversItsCellExactlyWhereTheCellsOwnDoes()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='margin-left:10.3pt;margin-top:5.2pt;border-spacing:0'><tr style='background:#ffcccc'>" +
                "<td id='a' style='background:#ffffff;width:40.2pt'>a</td></tr></table>"));
            container.SnapBoxDecorationsToCssPixels = true;
            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 0);

            var row = graphics.Log.Single(op => ColorOf(op) == "#ffcccc").Bounds;
            var cell = graphics.Log.Single(op => ColorOf(op) == "#ffffff").Bounds;

            Assert.False(Same(cell, RectOf(root, "a")), "snapping has to move the cell's edges, or this asserts nothing");
            Assert.True(Same(row, cell), $"the row's fill {row} is not the cell's {cell}");
        }

        [Fact]
        public async Task AHiddenCell_ShowsNoneOfItsTracksBackgrounds()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:0'><col style='background:#ccccff'><col style='background:#ccccff'>" +
                "<tr style='background:#ffcccc'><td id='a'>a</td><td style='visibility:hidden'>h</td></tr></table>");

            var a = RectOf(root, "a");
            Assert.All(Fills(log), f => Assert.True(Same(f, a), $"fill {f} is not cell a {a}"));
        }

        /// <summary>A hidden column's cells paint its background, still positioned against the column, not each cell.</summary>
        [Fact]
        public async Task AHiddenColumnsGradient_IsPositionedAgainstTheColumn()
        {
            var (root, log) = await PaintAsync(
                "<table style='border-collapse:separate;border-spacing:0'>" +
                "<col style='visibility:hidden;background:linear-gradient(#f00, #00f)'>" +
                "<tr><td id='a'>a</td></tr><tr><td id='b'>b</td></tr></table>");

            var gradients = log.Where(op => op.Brush is LinearGradientBrush).Select(op => (LinearGradientBrush)op.Brush!).ToList();

            Assert.Equal(2, gradients.Count);
            Assert.All(gradients, brush => Assert.Equal(RectOf(root, "a").Top, brush.Start.Y, 0.01));
            Assert.All(gradients, brush => Assert.Equal(RectOf(root, "b").Bottom, brush.End.Y, 0.01));
        }
    }
}
