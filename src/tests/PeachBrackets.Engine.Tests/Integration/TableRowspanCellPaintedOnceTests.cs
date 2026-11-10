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
    /// standing in for it in the rows it covers. Built once per visit, every word of a cell spanning N rows was drawn 2N−1
    /// times on top of itself, which reads as bold and extracts 2N−1 times; built once per page, a cell crossing a row taller
    /// than a page under a repeated group lost every part outside the one band that copy was confined to.
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
        private static string SpanningTableBesideTallRows(
            string group, string heights, string spacing, int cellHeight = 0, string align = "middle", int words = 1)
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
                    ? $"<td id='span' rowspan='{rowHeights.Length}' style='border:2pt solid #36c;border-bottom-color:#c36;background:#fdd;vertical-align:{align}'>{SpanningCellContent(cellHeight, words)}</td>"
                    : "";
                var id = i == 0 ? " id='first'" : "";

                return $"<tr>{spanning}<td{id} style='background:#eee'><div style='height:{height}pt'>w2_{i}</div></td></tr>";
            });

            return $"<table style='border-collapse:separate;border-spacing:{spacing}'>{groups}<tbody>{string.Concat(rows)}</tbody></table>";
        }

        /// <summary>
        /// The spanning cell's content: one word, alone or at the top of <paramref name="height"/>pt of its own, or
        /// <paramref name="words"/> words spread evenly down that height, so a part of the cell lost or drawn twice on some
        /// page shows in the word census.
        /// </summary>
        private static string SpanningCellContent(int height, int words) =>
            (height, words) switch
            {
                (<= 0, _) => "w1_1",
                (_, <= 1) => $"<div style='height:{height}pt'>w1_1</div>",
                _ => string.Concat(Enumerable.Range(1, words).Select(k => $"<div style='height:{height / words}pt'>w1_{k}</div>")),
            };

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
        [InlineData("none", "300,300", "0")]
        [InlineData("none", "300,15", "0")]
        [InlineData("none", "300,15,15", "0", 300, "middle", 10)]
        [InlineData("thead", "300,15", "0", 180, "middle", 6)]
        public async Task ARowspanCellContinuingOntoLaterPages_IsDecoratedToTheBottomOfItsLastRow(
            string group, string heights, string spacing, int cellHeight = 0, string align = "middle", int words = 1)
        {
            var html = Page(SpanningTableBesideTallRows(group, heights, spacing, cellHeight, align, words));
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

                // The fills of the cells beside it end where the rows do. The spanning cell's fill reaches at least that far
                // (on a page before its last it may run on to the foot of the page, past a row that moved to the next one),
                // and on its last page it ends there, as does its bottom border strip (2pt high, drawn as its own band as
                // its colour differs).
                var rowsBottom = beside.Max(r => r.Bottom);
                var spanningBottom = spanning.Max(r => r.Bottom);
                Assert.True(page == pages - 1 ? Math.Abs(spanningBottom - rowsBottom) < 0.5 : spanningBottom > rowsBottom - 0.5,
                    $"page {page + 1}: the spanning cell is drawn down to {spanningBottom}, the rows beside it to {rowsBottom}");
                if (page == pages - 1)
                    Assert.Contains(spanning, r => r.Height <= 2.5 && r.Width > 2.5 && Math.Abs(r.Bottom - rowsBottom) < 0.5);
                Assert.Equal(strips.Count, strips.Distinct().Count());
            }

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);
            Assert.True(lost.Count == 0 && doubled.Count == 0, $"lost [{string.Join(", ", lost)}], doubled [{string.Join(", ", doubled)}]");
        }

        /// <summary>
        /// A cell spanning five rows, whose own row is about a page and two thirds tall, under a repeated <c>&lt;thead&gt;</c> or
        /// <c>&lt;tfoot&gt;</c>. The page in the middle is walked while the cell's height is not settled yet, so the cell has no
        /// copy there until layout moves the spanned rows and re-opens that page; it re-opens it only for boxes that hold a
        /// draft there, which the rows and placeholders did only through the copies left out as repeats. Leaving them out
        /// without keeping those drafts lost the cell's fill and borders on the middle page.
        /// </summary>
        [Theory]
        [InlineData("<thead><tr><th>H1</th><th>H2</th></tr></thead>")]
        [InlineData("<tfoot><tr><td>F1</td><td>F2</td></tr></tfoot>")]
        public async Task ARowspanCellOverAPageTallOwnRow_IsDrawnOnTheMiddlePage(string group)
        {
            var rows = string.Concat(Enumerable.Range(1, 4).Select(i =>
                $"<tr><td>w3_{i}<div style='height:20px'>w4_{i}</div></td></tr>"));
            var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:360px 400px;margin:24px} " +
                "body{margin:0;font:11px/14px Arial} table{border-collapse:separate;border-spacing:0} td,th{padding:3px;border:1px solid #444}" +
                $"</style></head><body><table>{group}<tbody><tr><td id='span' rowspan='5' style='border:2px solid #36c;background:#fdd;" +
                "vertical-align:top'>w1_1</td><td><div style='height:580px'>w2_0</div></td></tr>" + rows + "</tbody></table></body></html>";
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
            var pages = container.FragmentTree!.Fragmentainers.Count;
            var cell = LayoutHarness.FindById(root, "span")!;

            Assert.True(pages >= 3, "the cell has to cross a whole page, or this asserts nothing");

            for (var page = 0; page < pages; page++)
            {
                using var graphics = new RecordingGraphics(new HtmlRenderContext());
                FragmentPaintHarness.PaintPage(container, graphics, page);

                var fill = VisibleDecoration(graphics.Log)
                    .Where(r => r.Left >= cell.Location.X - 0.5 && r.Right <= cell.ActualRight + 0.5).ToList();
                // The body's words beside the cell: the repeated group's own cells lie outside the rows the cell spans.
                var lowestText = graphics.Log
                    .Where(op => op.Kind == PaintOpKind.DrawString && op.Text!.StartsWith('w') && op.Bounds.Left >= cell.ActualRight - 0.5)
                    .Max(op => op.Bounds.Bottom);

                Assert.True(fill.Count > 0, $"page {page + 1}: the spanning cell is not drawn");
                Assert.True(fill.Max(r => r.Bottom) >= lowestText - 0.5,
                    $"page {page + 1}: the spanning cell is drawn down to {fill.Max(r => r.Bottom)}, the text beside it to {lowestText}");
            }
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

        private const string TranslucentHead =
            "<thead><tr><th style='background:#96aaff;padding:0;height:16px'>HEAD1</th><th style='background:#96aaff;padding:0'>HEAD2</th></tr></thead>";

        private const string TranslucentFoot =
            "<tfoot><tr><td style='background:#aaff96;padding:0;height:16px'>FOOT1</td><td style='background:#aaff96;padding:0'>FOOT2</td></tr></tfoot>";

        /// <summary>
        /// A cell spanning <paramref name="rows"/> (each row's height in px) on a 300×400px page, filled with a translucent
        /// colour so that a part drawn twice shows as a darker band, under the repeated <paramref name="groups"/>.
        /// </summary>
        private static string TranslucentSpanningTable(string model, string groups, int[] rows, string align, string content) =>
            "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300px 400px; margin: 20px } " +
            "body{margin:0;font:10px/12px Arial} td,th{vertical-align:top;font-weight:normal;text-align:left}</style></head><body>" +
            $"<table style='border-collapse:{model};border-spacing:0px;table-layout:fixed;width:260px'>" +
            $"<colgroup><col style='width:100px'><col style='width:160px'></colgroup>{groups}<tbody>" +
            string.Concat(rows.Select((height, i) => "<tr>" +
                (i == 0
                    ? $"<td rowspan='{rows.Length}' style='background:rgba(255,0,0,.5);border:3px solid #006;border-bottom:3px solid #060;vertical-align:{align};padding:2px'>{content}</td>"
                    : "") +
                $"<td style='background:#c8dcff;padding:0'><div style='height:{height}px'>R{i}</div></td></tr>")) +
            "</tbody></table></body></html>";

        public static TheoryData<string, string> SpanningCellsUnderARepeatedGroup() => new()
        {
            // The review's repros: the cell's words drawn again under the header on the next page; a cell taller than a
            // page overstruck; a translucent band drawn twice beside a sliced row; the cell drawn over the repeated header.
            { "bottom-aligned, tall row last", TranslucentSpanningTable("separate", TranslucentHead + TranslucentFoot, [40, 600], "bottom", "CW1 CW2 CW3") },
            { "content taller than a page", TranslucentSpanningTable("separate", TranslucentHead + TranslucentFoot, [40, 40], "top", "<div style='height:900px'>CW1 CW2 CW3 CW4 CW5 CW6</div>") },
            { "footer only, tall row in the middle", TranslucentSpanningTable("collapse", TranslucentFoot, [40, 600, 40], "top", "CW1") },
            { "header only, content taller than a page", TranslucentSpanningTable("separate", TranslucentHead, [40, 40], "top", "<div style='height:900px'>CW1</div>") },
            { "header only, tall row first", TranslucentSpanningTable("separate", TranslucentHead, [500, 20], "middle", "CW1") },
            { "header only, tall row first, bottom-aligned", TranslucentSpanningTable("separate", TranslucentHead, [500, 20], "bottom", "CW1 CW2") },
            { "header only, content taller than a page beside short rows", TranslucentSpanningTable("separate", TranslucentHead, [40, 40, 40], "top", "<div style='height:900px'>CW1</div> CW2") },

            // Collapsed borders put a row's top on a half pixel, where two parts of the fill meet.
            { "collapsed, header only, tall row second", TranslucentSpanningTable("collapse", TranslucentHead, [40, 600, 40], "top", "CW1") },
        };

        /// <summary>
        /// Under a repeated <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>, with a row taller than a page sliced across the bands,
        /// the cell is drawn in parts: no part of its translucent fill twice, none of it over the repeated group, and each of
        /// its words once in the whole document.
        /// </summary>
        [Theory]
        [MemberData(nameof(SpanningCellsUnderARepeatedGroup))]
        public async Task ARowspanCellUnderARepeatedGroup_IsDrawnOnceAndNeverOverTheGroup(string layout, string html)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
            var pages = container.FragmentTree!.Fragmentainers.Count;
            List<string> words = [];

            Assert.True(pages >= 2, $"{layout}: the cell has to continue onto a later page, or this asserts nothing");

            for (var page = 0; page < pages; page++)
            {
                using var graphics = new RecordingGraphics(new HtmlRenderContext());
                FragmentPaintHarness.PaintPage(container, graphics, page);

                words.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString && op.Text!.StartsWith("CW")).Select(op => op.Text!));

                // The translucent fills only: clips pass through, borders and opaque fills do not.
                var fills = VisibleDecoration(graphics.Log.Where(op =>
                        op.Kind is not (PaintOpKind.FillRect or PaintOpKind.Polygon) || op.Brush is SolidBrush { PaintColor.A: < 255 }))
                    .Where(r => r.Width > 0 && r.Height > 0).ToList();
                var labels = graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString && op.Text is "HEAD1" or "FOOT1").Select(op => op.Bounds).ToList();

                for (var i = 0; i < fills.Count; i++)
                {
                    for (var j = i + 1; j < fills.Count; j++)
                    {
                        Assert.True(Rect.Intersect(fills[i], fills[j]) is { Width: < 0.01 } or { Height: < 0.01 },
                            $"{layout}, page {page + 1}: the cell's fill is drawn twice over {Rect.Intersect(fills[i], fills[j])}");

                        // Where two parts meet, they meet on a pixel boundary, or each paints half of that pixel and a
                        // translucent fill shows a darker line there.
                        foreach (var (upper, lower) in new[] { (fills[i], fills[j]), (fills[j], fills[i]) })
                        {
                            if (Math.Abs(upper.Bottom - lower.Top) > 0.01) continue;

                            var pixels = upper.Bottom / (72d / 96d);
                            Assert.True(Math.Abs(pixels - Math.Round(pixels)) < 0.01,
                                $"{layout}, page {page + 1}: two parts of the cell's fill meet at {upper.Bottom}pt, inside a pixel");
                        }
                    }
                }

                Assert.DoesNotContain(fills, fill => labels.Exists(label => !Rect.Intersect(fill, label).IsEmpty));

                // Nor is its text drawn under the repeated group. Except a bottom-aligned cell's text under a repeated footer:
                // since #1950 it is aligned within the remainder of the band its row begins in, which runs under the footer,
                // on main as here (#2313).
                var cellText = graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString && op.Text!.StartsWith("CW")).Select(op => op.Bounds);
                var groupLabels = layout == "bottom-aligned, tall row last"
                    ? graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString && op.Text is "HEAD1").Select(op => op.Bounds).ToList()
                    : labels;
                Assert.DoesNotContain(cellText, text => groupLabels.Exists(label => Rect.Intersect(text, label) is { Width: > 0.01, Height: > 0.01 }));
            }

            Assert.Equal(PaintedWordsOf(html), words.Order());
        }

        private static IEnumerable<string> PaintedWordsOf(string html) =>
            System.Text.RegularExpressions.Regex.Matches(html, @"CW\d+").Select(m => m.Value).Order();

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
