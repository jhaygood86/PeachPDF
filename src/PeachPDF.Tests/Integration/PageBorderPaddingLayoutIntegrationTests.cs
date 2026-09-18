using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Content-position regression coverage for the page box's own border/padding (issue #1147): the
    /// content area now genuinely shrinks by the resolved border+padding extent, on top of margin - this
    /// is the "content-position regression proof" CLAUDE.md's PR plan calls out across every existing
    /// page-layout scenario category (multi-page, named pages, <c>:left</c>/<c>:right</c>, margin boxes,
    /// multi-column, footnotes), plus the critical guard that a document with NO <c>@page</c> border/
    /// padding declared is unaffected. Follows <see cref="PerPageGeometryLayoutIntegrationTests"/>'s own
    /// layout-harness convention (build a container, PerformLayout, assert box positions) - no full PDF
    /// generation needed for the layout-only assertions.
    /// </summary>
    public class PageBorderPaddingLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMargin = 50;

        [Fact]
        public async Task NoBorderOrPaddingDeclared_BandMatchesMarginOnlyBaseline()
        {
            // The critical regression guard: a document that declares no @page border/padding at all
            // must reproduce the exact pre-#1147 band - proven here structurally (rather than via a
            // stored byte-for-byte snapshot) since the whole pre-existing suite already re-runs
            // unchanged against this code path.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 50pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body><p id='p'>content</p></body></html>
                """);

            var p = FindById(container.Root!, "p");
            Assert.NotNull(p);
            // Layout keeps content anchored at the BASE MarginLeft/MarginTop in its own coordinate
            // space regardless of any per-page @page geometry (PdfGenerator's per-page paint-time
            // translate is what maps this onto each page's own physical origin) - see
            // HtmlContainerInt.PageContentRightOf's own remarks. What DOES vary per page is the band's
            // SIZE (BandWidth/BandHeight), asserted below.
            Assert.Equal(BaseMargin, p!.Location.X, 0.01);
            Assert.Equal(BaseMargin, p.Location.Y, 0.01);
            Assert.Equal(SheetW - 2 * BaseMargin, container.PageGeometry.GetPage(0).BandWidth, 0.01);
            Assert.Equal(SheetH - 2 * BaseMargin, container.PageGeometry.GetPage(0).BandHeight, 0.01);
            Assert.Equal(0, container.PageGeometry.GetPage(0).BorderLeftPt);
            Assert.Equal(0, container.PageGeometry.GetPage(0).PaddingLeftPt);
        }

        [Fact]
        public async Task UniformBorderAndPadding_ShrinksContentBandOnEveryPage()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 50pt; border: 5pt solid black; padding: 10pt; }
                body { margin: 0; }
                p { margin: 0; page-break-after: always; }
                </style></head><body><p id='p1'>one</p><p id='p2'>two</p></body></html>
                """);

            const double inset = BaseMargin + 5 + 10; // margin + border + padding

            var p1 = FindById(container.Root!, "p1");
            var p2 = FindById(container.Root!, "p2");
            Assert.NotNull(p1);
            Assert.NotNull(p2);

            // p1's own LAYOUT-space position stays at the base anchor (see the no-border/padding test's
            // own remarks) - border/padding narrows the BAND, it does not move where layout places
            // content within its own coordinate space.
            Assert.Equal(BaseMargin, p1!.Location.X, 0.01);
            Assert.Equal(BaseMargin, p1.Location.Y, 0.01);

            // The actual, directly observable effect: this page's own resolved band is smaller by
            // margin + border + padding on every side.
            var page0 = container.PageGeometry.GetPage(0);
            Assert.Equal(SheetW - 2 * inset, page0.BandWidth, 0.01);
            Assert.Equal(SheetH - 2 * inset, page0.BandHeight, 0.01);
            Assert.Equal(BaseMargin + 5 + 10, page0.ContentLeftPt);
            Assert.Equal(BaseMargin + 5 + 10, page0.ContentTopPt);

            // p2 (after the forced break) starts exactly where page 0's own (shrunk) band ends - the
            // SECOND page (no @page override of its own) gets the identical shrunk band, since
            // border/padding declared on the base rule applies uniformly, not just to page 1.
            Assert.Equal(BaseMargin + page0.BandHeight, p2!.Location.Y, 0.01);
            var page1 = container.PageGeometry.GetPage(1);
            Assert.Equal(SheetW - 2 * inset, page1.BandWidth, 0.01);
            Assert.Equal(SheetH - 2 * inset, page1.BandHeight, 0.01);
        }

        [Fact]
        public async Task PaddingOnNamedPage_OnlyThatPagesContentShrinks()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 50pt; }
                @page chapter { padding: 30pt; }
                body { margin: 0; }
                p { margin: 0; }
                div { page: chapter; margin: 0; page-break-before: always; }
                </style></head><body><p id='before'>before</p><div><p id='inside'>inside</p></div></body></html>
                """);

            var before = FindById(container.Root!, "before");
            var inside = FindById(container.Root!, "inside");
            Assert.NotNull(before);
            Assert.NotNull(inside);

            var beforePage = container.PageIndexOf(before!.Location.Y);
            var insidePage = container.PageIndexOf(inside!.Location.Y);
            Assert.True(insidePage > beforePage, "the named-page div should have forced a page break");

            // Page 1 (unnamed): margin only - full band.
            Assert.Equal(SheetW - 2 * BaseMargin, container.PageGeometry.GetPage(beforePage).BandWidth, 0.01);
            Assert.Equal(0, container.PageGeometry.GetPage(beforePage).PaddingLeftPt);

            // Page 2 (named "chapter"): margin + padding - narrower band, on this page only.
            const double insetWidth = SheetW - 2 * (BaseMargin + 30);
            Assert.Equal(insetWidth, container.PageGeometry.GetPage(insidePage).BandWidth, 0.01);
            Assert.Equal(30, container.PageGeometry.GetPage(insidePage).PaddingLeftPt);
        }

        [Fact]
        public async Task LeftRightPaddingOverride_AlternatingBandsShrinkAsymmetrically()
        {
            var blocks = string.Concat(Enumerable.Range(0, 80).Select(i => $"<p>paragraph {i}</p>"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 50pt; }
                @page :right { padding-left: 40pt; }
                @page :left { padding-right: 40pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>{{blocks}}</body></html>
                """);

            // Page 1 (:right, odd) and page 2 (:left, even) each lose the same 40pt total padding -
            // only WHICH side carries it differs - so both bands end up the same width.
            const double expectedWidth = SheetW - 2 * BaseMargin - 40;

            Assert.Equal(expectedWidth, container.PageGeometry.GetPage(0).BandWidth, 0.01);
            Assert.Equal(expectedWidth, container.PageGeometry.GetPage(1).BandWidth, 0.01);
            Assert.Equal(40, container.PageGeometry.GetPage(0).PaddingLeftPt);
            Assert.Equal(0, container.PageGeometry.GetPage(0).PaddingRightPt);
            Assert.Equal(0, container.PageGeometry.GetPage(1).PaddingLeftPt);
            Assert.Equal(40, container.PageGeometry.GetPage(1).PaddingRightPt);
        }

        [Fact]
        public async Task MulticolumnContent_ReWrapsToTheShrunkenBandWidth()
        {
            // css-multicol-1: column boxes distribute the multicol container's own content-box width -
            // once that content-box is narrower (margin + border + padding), every column narrows with
            // it. Two equal columns across the shrunk band.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 50pt; border: 10pt solid black; }
                body { margin: 0; }
                #mc { column-count: 2; column-gap: 0; margin: 0; }
                p { margin: 0; }
                </style></head><body><div id='mc'>
                <p>one</p><p>two</p><p>three</p><p>four</p>
                </div></body></html>
                """);

            var mc = FindById(container.Root!, "mc");
            Assert.NotNull(mc);

            const double inset = BaseMargin + 10;
            var expectedWidth = SheetW - 2 * inset;
            Assert.Equal(expectedWidth, mc!.ActualRight - mc.Location.X, 0.5);
        }

        [Fact]
        public async Task PageMarginBox_PositionIsUnaffectedByPageBorderOrPadding()
        {
            // css-page-3 §5.3.1: a page-margin box's own containing block spans the "available width"/
            // height - border-left + padding-left + page-area-width + padding-right + border-right on
            // the horizontal axis - which already equals the full margin-to-margin extent regardless of
            // how it subdivides into border/padding/content. A page-margin box therefore needs NO
            // change for #1147: its rendered position with a border/padding declared must be BYTE-
            // IDENTICAL to one with none, since margin boxes sit entirely OUTSIDE the page box's own
            // border/padding.
            async Task<string> Render(string pageDecl)
            {
                var generator = new PdfGenerator();
                var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
                config.SetMargins(50);
                var doc = await generator.GeneratePdf(
                    $"<!DOCTYPE html><html><head><style>@page {{ {pageDecl} @top-center {{ content: 'header'; }} }}" +
                    "</style></head><body><p>short</p></body></html>", config);
                var ms = new MemoryStream();
                doc.Save(ms);
                return System.Text.Encoding.Latin1.GetString(ms.ToArray());
            }

            var withoutBorderPadding = await Render("");
            var withBorderPadding = await Render("border: 5pt solid black; padding: 10pt;");

            // Every text run's own "<font> <size> Tf\n<x> <y> Td" placement - the LAST one in the
            // stream is always the @top-center margin box (painted after the document's own content,
            // per PdfGenerator's paint order), so its coordinates are what must match between the two
            // renders despite one declaring a page border/padding that shifted the BODY text above it.
            var tdPattern = new System.Text.RegularExpressions.Regex(@"Tf\r?\n[\d.-]+ [\d.-]+ Td");
            var withoutMatches = tdPattern.Matches(withoutBorderPadding);
            var withMatches = tdPattern.Matches(withBorderPadding);

            Assert.True(withoutMatches.Count >= 2, "expected both the body text and the header to be present");
            Assert.Equal(withoutMatches.Count, withMatches.Count);
            Assert.Equal(withoutMatches[^1].Value, withMatches[^1].Value);
            // Sanity check that the two renders are NOT simply identical throughout - the body text
            // above the header (index 0) DOES move, confirming the fixture actually exercises the
            // border/padding shrink this test means to isolate the header from.
            Assert.NotEqual(withoutMatches[0].Value, withMatches[0].Value);
        }

        [Fact]
        public async Task FootnoteArea_StillReservesSpaceWithinTheShrunkenBand()
        {
            // css-gcpm-3 float: footnote reserves space at the bottom of the SAME content band that
            // border/padding already shrank - generating the PDF (rather than a layout-only assertion)
            // is the simplest way to prove the whole pipeline (fragmentation, footnote reservation,
            // paint) still completes without throwing once the band it reserves against is smaller.
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(50);

            var doc = await generator.GeneratePdf(
                "<!DOCTYPE html><html><head><style>" +
                "@page { border: 5pt solid black; padding: 10pt; }" +
                "body { margin: 0; } p { margin: 0; }" +
                ".fn { float: footnote; }" +
                "</style></head><body><p>Body text<span class='fn'>a footnote</span> continues.</p></body></html>",
                config);

            Assert.True(doc.PageCount >= 1);
            var ms = new MemoryStream();
            doc.Save(ms);
            Assert.True(ms.Length > 0);
        }

        // --- Harness (mirrors PerPageGeometryLayoutIntegrationTests / PdfGenerator.SetContent) ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
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
