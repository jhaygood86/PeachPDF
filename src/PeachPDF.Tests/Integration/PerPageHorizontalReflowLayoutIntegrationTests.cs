using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-level tests for per-page horizontal reflow (issue #143): when a per-page <c>@page</c> rule
    /// overrides a left/right margin, top-level (main-column) auto-width block content is re-wrapped to
    /// that page's own content-box width — CSS Paged Media 3's "the edges of the page area act as a
    /// containing block for layout that occurs between page breaks" — instead of being laid out once at
    /// the base measure and merely shifted/clipped at paint time. A paragraph that spans a page boundary
    /// keeps a single measure (its start page's) across its fragments, per CSS Fragmentation 3
    /// ("Fragmentation splits boxes in the block flow dimension"): a box's used inline size is shared by
    /// all its fragments. Follows the repo's layout-harness convention (build a container, PerformLayout,
    /// assert box positions/sizes), with a harness mirroring PdfGenerator.SetContent's geometry derivation.
    /// </summary>
    public class PerPageHorizontalReflowLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        // Base fixture margins: @page { margin: 60pt 50pt } -> content box 512 wide at left origin 50,
        // so the base right edge is 562. Band 672 tall.
        private const double BaseMargin = 50;
        private const double BaseContentWidth = SheetW - 2 * BaseMargin; // 512
        private const double BaseRightEdge = BaseMargin + BaseContentWidth; // 562

        [Fact]
        public async Task FirstPageMarginLeftZero_ReflowsToWiderMeasure()
        {
            // @page :first { margin-left: 0 } widens page 0 only: content box 562 wide (612 - 0 - 50),
            // right edge 612; pages 2+ keep the base 562 right edge.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>page zero paragraph whose auto width should reflow to the wider first-page measure</p>
                <p id='p1' style='page-break-before: always'>page one paragraph at the base measure</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            var p1 = FindById(container.Root!, "p1")!;

            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(p1.Location.Y));

            // p0 adopts page 0's own (wider) measure; p1 reverts to the base measure.
            // Page 0: left origin stays at the base 50, content width = 612 - 0 - 50 = 562, so the right
            // edge is 50 + 562 = 612. Page 1 keeps the base right edge (562).
            const double firstPageRightEdge = BaseMargin + (SheetW - 0 - BaseMargin); // 612
            Assert.Equal(firstPageRightEdge, p0.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge, p1.ActualRight, 0.5);

            Assert.Equal(container.PageContentRightOf(p0.Location.Y), p0.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p1.Location.Y), p1.ActualRight, 0.5);
            Assert.True(p0.ActualRight > BaseRightEdge, "first page should reflow wider than the base measure");
        }

        [Fact]
        public async Task MirrorMargins_DifferingWidths_EachPageOwnMeasure()
        {
            // Binding-style mirror margins: right (odd) pages inset 20 on the left, left (even) pages inset
            // 100 — same right margin — so each page has a genuinely different measure.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :right { margin-left: 20pt; }
                @page :left  { margin-left: 100pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='pRight'>right page content</p>
                <p id='pLeft' style='page-break-before: always'>left page content</p>
                </body></html>
                """);

            var pRight = FindById(container.Root!, "pRight")!; // page 0 -> pageNumber 1 -> :right
            var pLeft = FindById(container.Root!, "pLeft")!;   // page 1 -> pageNumber 2 -> :left

            Assert.Equal(0, container.PageIndexOf(pRight.Location.Y));
            Assert.Equal(1, container.PageIndexOf(pLeft.Location.Y));

            // Right page: right edge = 50 + (612 - 20 - 50) = 592. Left page: 50 + (612 - 100 - 50) = 512.
            Assert.Equal(BaseMargin + (SheetW - 20 - BaseMargin), pRight.ActualRight, 0.5); // 592
            Assert.Equal(BaseMargin + (SheetW - 100 - BaseMargin), pLeft.ActualRight, 0.5); // 512

            Assert.True(pRight.ActualRight > pLeft.ActualRight,
                "the wider-measure right page should extend further than the narrower left page");
            Assert.Equal(container.PageContentRightOf(pRight.Location.Y), pRight.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(pLeft.Location.Y), pLeft.ActualRight, 0.5);
        }

        [Fact]
        public async Task StraddlingParagraph_KeepsStartPageMeasure()
        {
            // A single long paragraph starts on the wide first page and flows onto the base-margin page 2.
            // Per CSS Fragmentation 3 its fragments share ONE inline size (the start page's), so its
            // continuation lines on page 2 keep the wider first-page measure rather than re-wrapping to
            // the base measure. This is a characterization of the spec-correct behavior.
            var words = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='flow'>{{words}}</p>
                </body></html>
                """);

            var flow = FindById(container.Root!, "flow")!;
            Assert.Equal(0, container.PageIndexOf(flow.Location.Y)); // starts on page 0

            var flowWords = new List<CssRect>();
            CollectWords(flow, flowWords);

            var pageOneWords = flowWords
                .Where(w => w.Width > 0 && container.PageIndexOf(w.Top) >= 1)
                .ToList();

            Assert.NotEmpty(pageOneWords); // the paragraph really does span onto page 2
            // Continuation lines keep the wide start-page measure: some word extends past the base right
            // edge, which it could not do had the paragraph re-wrapped to the base measure on page 2.
            Assert.True(pageOneWords.Max(w => w.Right) > BaseRightEdge,
                "a spanning paragraph keeps its start-page (wider) measure across fragments");
        }

        [Fact]
        public async Task BodyMargin_RightInsetRespected_ContentDoesNotOverrunBodyMargin()
        {
            // The containing block (body) carries a non-zero margin, so a reflowed main-column paragraph
            // must stay inside body's margin box: its right edge lands one body-right-margin short of the
            // page-area edge, not flush against it. Regression guard for the containing-block right inset
            // (the horizontal mirror of ClientLeft) - without it the block overruns body's right margin.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 20pt; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>first page paragraph inside a margined body</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));

            // Page 0's area right edge is 612 (margin-left:0, right stays base 50). body's 20pt margins
            // inset the content box on both sides: left = base 50 + 20 = 70, right = 612 - 20 = 592.
            const double pageAreaRight = BaseMargin + (SheetW - 0 - BaseMargin); // 612
            Assert.Equal(70, p0.Location.X, 0.5);
            Assert.Equal(pageAreaRight - 20, p0.ActualRight, 0.5);
            Assert.True(p0.ActualRight < container.PageContentRightOf(p0.Location.Y),
                "content must stay inside body's right margin, not reach the page-area edge");
        }

        [Fact]
        public async Task ManyParagraphsAcrossPages_ReflowConverges_EachBlockOwnPageMeasure()
        {
            // Many separate main-column paragraphs flow across several pages with a wide first page.
            // The initial pass lays every box out at page 0's (wide) measure; the reflow loop then
            // re-wraps the later-page paragraphs to the base measure, making them taller and shifting
            // the page boundaries - so the box->page assignment changes between the first and second
            // reflow iterations and the loop runs more than once before converging. Asserts the loop
            // reaches a stable state where every paragraph carries exactly its own page's measure.
            // Very narrow base pages (200pt L/R margins -> ~212pt measure) vs a full-bleed first page
            // (612pt): a paragraph is a couple of lines wide on page 0 but several lines tall at the base
            // measure, so re-wrapping the later pages materially shifts every page boundary - guaranteeing
            // the assignment changes between iterations and the loop runs more than once.
            var paragraphs = string.Concat(Enumerable.Range(1, 90).Select(i =>
                $"<p class='b'>Block {i}: lorem ipsum dolor sit amet consectetur adipiscing elit sed " +
                "do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim.</p>"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 200pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>{{paragraphs}}</body></html>
                """);

            var blocks = new List<CssBox>();
            CollectByClass(container.Root!, "b", blocks);
            Assert.True(blocks.Count > 3, "fixture should span several pages worth of blocks");

            // Every block ended up at exactly its own page's measure (the reflow converged, so no block
            // is left carrying a neighbouring page's width).
            foreach (var block in blocks)
                Assert.Equal(container.PageContentRightOf(block.Location.Y), block.ActualRight, 0.5);

            // Page 0 blocks are genuinely wider than later-page blocks.
            var pageZero = blocks.Where(b => container.PageIndexOf(b.Location.Y) == 0).ToList();
            var laterPages = blocks.Where(b => container.PageIndexOf(b.Location.Y) >= 1).ToList();
            Assert.NotEmpty(pageZero);
            Assert.NotEmpty(laterPages);
            Assert.True(pageZero.Max(b => b.ActualRight) > laterPages.Min(b => b.ActualRight),
                "the full-bleed first page should reflow wider than the base-margin later pages");
        }

        [Fact]
        public async Task ConstrainedBody_ExplicitWidth_DoesNotReflow()
        {
            // body has an explicit width, so the main column no longer spans the page area: per-page
            // reflow is not applied and a child resolves against body's constrained width instead of the
            // wide page-0 measure (accepted gap - see issues #199/#201).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; width: 300pt; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>constrained-body paragraph</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));
            // body is 300pt wide at the base left origin (50); p0 fills that, NOT the wide page-0 area.
            Assert.Equal(BaseMargin + 300, p0.ActualRight, 0.5);
            Assert.True(p0.ActualRight < container.PageContentRightOf(p0.Location.Y),
                "a constrained containing block must not be widened to the page area");
        }

        [Fact]
        public async Task DegenerateOverride_MarginsConsumeSheet_FallsBackToBaseMeasure()
        {
            // Left+right margins wider than the sheet would collapse the content box; PageContentRightOf
            // falls back to the base measure (mirror of the vertical band-height clamp) so content never
            // gets a zero/negative width.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 700pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>degenerate-margin paragraph</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(BaseRightEdge, container.PageContentRightOf(p0.Location.Y), 0.5);
            Assert.Equal(BaseRightEdge, p0.ActualRight, 0.5);
        }

        [Fact]
        public async Task UniformMargins_NoHorizontalOverride_IdenticalToBase()
        {
            // Only a top-margin per-page override — no left/right override — so the horizontal reflow path
            // stays dormant and content lays out at the historical single base measure.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 80pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p'>ordinary paragraph at the base measure</p>
                </body></html>
                """);

            Assert.False(container.UseVariablePageWidth);
            var p = FindById(container.Root!, "p")!;
            Assert.Equal(BaseRightEdge, p.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge, container.PageContentRightOf(p.Location.Y), 0.5);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public async Task ReflowWidth_ScalesWithPixelsPerPoint(double ppp)
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>first page paragraph</p>
                <p id='p1' style='page-break-before: always'>second page paragraph</p>
                </body></html>
                """, ppp);

            var p0 = FindById(container.Root!, "p0")!;
            var p1 = FindById(container.Root!, "p1")!;

            // The wide first-page measure and the base measure both scale linearly with PixelsPerPoint —
            // no double-scaling (issue #113 discipline): the layout-space right edges are the point values
            // times ppp.
            const double firstPageRightEdge = BaseMargin + (SheetW - 0 - BaseMargin); // 612 at ppp 1
            Assert.Equal(firstPageRightEdge * ppp, p0.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge * ppp, p1.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p0.Location.Y), p0.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p1.Location.Y), p1.ActualRight, 0.5);
        }

        // --- Harness (mirrors PdfGenerator.SetContent's geometry derivation; see
        //     PerPageGeometryLayoutIntegrationTests) ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            // SetHtml runs CascadeApplyPageStyles: base @page margins land on the container (already
            // PixelsPerPoint-scaled) and PageRules are captured for per-page selection.
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                SheetW * ppp - container.MarginLeft - container.MarginRight,
                SheetH * ppp - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, ppp);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static void CollectWords(CssBox box, List<CssRect> words)
        {
            foreach (var word in box.Words)
                words.Add(word);

            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        private static void CollectByClass(CssBox box, string className, List<CssBox> result)
        {
            var classAttr = box.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr) &&
                classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                result.Add(box);
            }

            foreach (var child in box.Boxes)
                CollectByClass(child, className, result);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
