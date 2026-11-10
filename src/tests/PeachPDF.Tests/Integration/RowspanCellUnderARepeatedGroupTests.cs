using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>rowspan</c> cell spanning a row taller than a page, under a repeated <c>&lt;thead&gt;</c> or <c>&lt;tfoot&gt;</c>
    /// (#1834), rendered end to end. Layout slices the tall row across the bands below the repeated group, so a single copy of
    /// the cell has no one place; it is built in parts, and these check what a reader of the result sees: the cell's text
    /// once in the whole document, its translucent fill never drawn twice over any part of a page, and never over the
    /// repeated group.
    /// </summary>
    public class RowspanCellUnderARepeatedGroupTests
    {
        private const string Head = "<thead><tr><th style='background:#96aaff;padding:0;height:16px'></th><th style='background:#96aaff;padding:0'></th></tr></thead>";
        private const string Foot = "<tfoot><tr><td style='background:#96aaff;padding:0;height:16px'></td><td style='background:#96aaff;padding:0'></td></tr></tfoot>";

        /// <summary>
        /// A table on a 300×400px page whose only text is the spanning cell's one word, beside rows of
        /// <paramref name="rows"/> px, so every text-showing operator in the document is a copy of that word.
        /// </summary>
        private static string Table(string model, string groups, int[] rows, string align, string content) =>
            "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300px 400px; margin: 20px } " +
            "body{margin:0;font:10px/12px Arial}</style></head><body>" +
            $"<table style='border-collapse:{model};border-spacing:0px;table-layout:fixed;width:260px'>" +
            $"<colgroup><col style='width:100px'><col style='width:160px'></colgroup>{groups}<tbody>" +
            string.Concat(rows.Select((height, i) => "<tr>" +
                (i == 0
                    ? $"<td rowspan='{rows.Length}' style='background:rgba(255,0,0,.5);border:3px solid #006;vertical-align:{align};padding:2px'>{content}</td>"
                    : "") +
                $"<td style='background:#c8dcff;padding:0;height:{height}px'></td></tr>")) +
            "</tbody></table></body></html>";

        public static TheoryData<string, string> Layouts() => new()
        {
            { "bottom-aligned, tall row last", Table("separate", Head + Foot, [40, 600], "bottom", "W") },
            { "content taller than a page", Table("separate", Head + Foot, [40, 40], "top", "<div style='height:900px'>W</div>") },
            { "footer only, tall row in the middle", Table("collapse", Foot, [40, 600, 40], "top", "W") },
            { "header only, tall row first", Table("separate", Head, [500, 20], "middle", "W") },
            { "header only, two page-tall rows", Table("separate", Head, [330, 330], "middle", "W") },
        };

        [Theory]
        [MemberData(nameof(Layouts))]
        public async Task TheSpanningCellsWord_IsShownOnceInTheSavedPdf(string layout, string html)
        {
            var document = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig());
            var pdf = document.Inspect();

            Assert.True(pdf.Pages.Count >= 2, $"{layout}: the cell has to continue onto a later page, or this asserts nothing");
            Assert.Equal(1, pdf.Pages.Sum(page => Regex.Matches(pdf.ContentText(page), @"\bTj\b").Count));
        }

        /// <summary>
        /// The cell's text is its content, which paints over its own background: with the cell built in parts, every part of
        /// its fill is drawn before the text on each page. Opaque and in a colour nothing else uses, so the content stream
        /// says which comes first; a translucent fill would only tint text drawn beneath it, and a check that only counts
        /// fills or words cannot tell.
        /// </summary>
        [Theory]
        [InlineData("<thead><tr><th style='height:16px'></th><th></th></tr></thead>", 600)]
        [InlineData("<tfoot><tr><td style='height:16px'></td><td></td></tr></tfoot>", 600)]
        [InlineData("<thead><tr><th style='height:16px'></th><th></th></tr></thead>", 330)]
        public async Task TheSpanningCellsText_IsDrawnOverItsOwnFill(string group, int tallRow)
        {
            var html = "<!DOCTYPE html><html><head><style>@page{size:300px 400px;margin:20px} body{margin:0;font:10px/12px Arial}</style></head><body>" +
                $"<table style='width:260px;border-spacing:0'>{group}<tbody>" +
                "<tr><td rowspan='2' style='background:#00ff00;vertical-align:top'>CW1 CW2</td>" +
                $"<td style='background:#cccccc'><div style='height:{tallRow}px'></div></td></tr><tr><td style='background:#cccccc'><div style='height:20px'></div></td></tr></tbody></table></body></html>";

            var pdf = (await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig())).Inspect();

            Assert.True(pdf.Pages.Count >= 2, "the cell has to continue onto a later page, or this asserts nothing");

            var pagesWithText = 0;
            foreach (var page in pdf.Pages)
            {
                var content = pdf.ContentText(page);
                var fills = Regex.Matches(content, @"\b0 1 0 rg\b").Select(m => m.Index).ToList();
                var text = Regex.Matches(content, @"\bT[jJ]\b").Select(m => m.Index).ToList();

                if (text.Count == 0) continue;

                pagesWithText++;
                Assert.True(fills.Count > 0 && fills.Max() < text.Min(), "the cell's fill is drawn over its text");
            }

            Assert.Equal(1, pagesWithText);
        }

        /// <summary>
        /// The parts of the cell beside later rows reach it through the placeholders those rows hold for it, which are no
        /// element of the document: tagged, they open no structure element of their own, and the cell's own element stays
        /// in a row, though its content now paints after the rows. Empty divisions are compared against the same table with
        /// no repeated group, where the cell is not built in parts, so only what the parts would add is counted.
        /// </summary>
        [Theory]
        [MemberData(nameof(Layouts))]
        public async Task TaggedTheParts_OpenNoEmptyStructureElement_AndLeaveEveryCellInARow(string layout, string html)
        {
            var withoutGroups = html.Replace(Head, "").Replace(Foot, "");
            var (emptyWithoutGroups, _) = await StructureOf(withoutGroups);
            var (empty, cellParents) = await StructureOf(html);

            Assert.NotEqual(withoutGroups, html);
            Assert.True(emptyWithoutGroups == empty, $"{layout}: {empty} empty /Div elements, {emptyWithoutGroups} without the repeated group");
            Assert.True(cellParents.All(parent => parent == "/TR"), $"{layout}: a table cell's element is in {string.Join(", ", cellParents.Distinct())}");
        }

        /// <summary>How many empty <c>/Div</c> elements the tagged document has, and the type of every table cell element's parent.</summary>
        private static async Task<(int EmptyDivisions, List<string?> CellParents)> StructureOf(string html)
        {
            var pdf = (await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { EnableTaggedPdf = true })).Inspect();
            var empty = 0;
            List<string?> cellParents = [];

            foreach (var element in pdf.StructureRoot) Walk(element, parent: null);
            return (empty, cellParents);

            void Walk(StructElem element, string? parent)
            {
                if (element.StructureType == "/Div" && element.AllKids.Count == 0) empty++;
                if (element.StructureType is "/TD" or "/TH") cellParents.Add(parent);
                foreach (var kid in element.Kids) Walk(kid, element.StructureType);
            }
        }

        [Theory]
        [MemberData(nameof(Layouts))]
        public async Task TheSpanningCellsFill_IsPaintedOnceAndNeverOverTheRepeatedGroup(string layout, string html)
        {
            var renderContext = new HtmlRenderContext();
            var options = new HtmlDocumentOptions { PageWidthPoints = 225, PageHeightPoints = 300 };
            options.SetMargins(15);
            using var document = await HtmlDocument.LoadAsync(html, renderContext, options);
            using (var measure = PdfCanvas.CreateMeasuring(renderContext, new Size(225, 300)))
                await document.LayoutAsync(measure);

            Assert.True(document.Pages.Count >= 2, $"{layout}: the cell has to continue onto a later page, or this asserts nothing");

            for (var page = 0; page < document.Pages.Count; page++)
            {
                using var graphics = new RecordingGraphics(renderContext);
                await document.PaintPageAsync(page, graphics);

                var (cell, group, beside) = VisibleFills(graphics.Log);

                // The cell runs beside every row of its span on this page, from the top of the first to the foot of the last.
                if (beside.Count > 0)
                {
                    Assert.True(cell.Count > 0, $"{layout}, page {page + 1}: the cell is not drawn beside its rows");
                    Assert.True(cell.Min(r => r.Top) <= beside.Min(r => r.Top) + 0.5 && cell.Max(r => r.Bottom) >= beside.Max(r => r.Bottom) - 0.5,
                        $"{layout}, page {page + 1}: the cell is drawn from {cell.Min(r => r.Top)} to {cell.Max(r => r.Bottom)}, " +
                        $"the rows beside it from {beside.Min(r => r.Top)} to {beside.Max(r => r.Bottom)}");
                }

                for (var i = 0; i < cell.Count; i++)
                {
                    for (var j = i + 1; j < cell.Count; j++)
                    {
                        Assert.True(Rect.Intersect(cell[i], cell[j]) is { Width: < 0.01 } or { Height: < 0.01 },
                            $"{layout}, page {page + 1}: the cell's fill is drawn twice over {Rect.Intersect(cell[i], cell[j])}");
                    }
                }

                Assert.DoesNotContain(cell, fill => group.Exists(g => Rect.Intersect(fill, g) is { Width: > 0.01, Height: > 0.01 }));
            }
        }

        /// <summary>
        /// The visible part of each translucent fill (the spanning cell's), of each fill in the repeated group's colour, and of
        /// each fill of the cells beside the spanning one, on one page's paint log.
        /// </summary>
        private static (List<Rect> Cell, List<Rect> Group, List<Rect> Beside) VisibleFills(IEnumerable<PaintOp> log)
        {
            List<Rect> cell = [];
            List<Rect> group = [];
            List<Rect> beside = [];
            Stack<Rect?> clips = [];

            foreach (var op in log)
            {
                switch (op.Kind)
                {
                    case PaintOpKind.PushClip: clips.Push(op.Bounds); break;
                    case PaintOpKind.PushClipPath: clips.Push(null); break;
                    case PaintOpKind.PopClip: clips.Pop(); break;
                    case PaintOpKind.FillRect when op.Brush is SolidBrush { PaintColor: var color }:
                        var shown = clips.Aggregate(op.Bounds, (r, clip) => clip is { } c ? Rect.Intersect(r, c) : r);
                        if (shown is not { Width: > 0, Height: > 0 }) break;
                        if (color.A < 255) cell.Add(shown);
                        else if (color is { R: 0x96, G: 0xaa, B: 0xff }) group.Add(shown);
                        else if (color is { R: 0xc8, G: 0xdc, B: 0xff }) beside.Add(shown);
                        break;
                }
            }

            return (cell, group, beside);
        }
    }
}
