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
    /// Regression coverage for named-page geometry attribution (post-change review findings):
    /// (1) a named element's registration is attributed to the TOP of the pagination slot its page
    /// starts on (<c>CssBox.NamedPageRegistrationY</c>) - the element itself sits its preserved
    /// post-forced-break margin (css-break-3 §5.2) below that top, and the document's first element
    /// sits below the content origin by its own margins, but per css-page-3 the page it starts on
    /// carries its name, so <c>PageRuleResolver.ActiveNameAtSlotStart</c> must see it at the slot
    /// top; and (2) the registration happens BEFORE the named element's own children lay out, so a
    /// multi-page named element's content paginates against the named page's bands, not the
    /// previous name's. Same harness convention as <see cref="PerPageGeometryLayoutIntegrationTests"/>.
    /// </summary>
    public class NamedPageGeometryAttributionTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMt = 60;
        private const double BaseMb = 60;
        private const double BaseBand = SheetH - BaseMt - BaseMb;

        [Fact]
        public async Task NamedElementWithMarginTop_SlotBandUsesNamedRule()
        {
            // Same as NamedPage_FullBleedBand_StartsAtItsForcedBreakSlot_AndPropagates but the named
            // element carries its own margin-top (preserved after a forced break per css-break-3).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page wide { margin: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <p>normal page content</p>
                <div id='wide' style='page: wide; margin-top: 20pt'>wide content</div>
                </body></html>
                """);

            var wide = FindById(container.Root!, "wide");
            Assert.NotNull(wide);

            Assert.Equal(BaseBand, container.PageBandHeightOf(0));
            Assert.Equal(BaseMt + BaseBand, container.PageTopOf(1));
            // Element itself sits margin below the slot top (margin preserved after forced break).
            Assert.Equal(container.PageTopOf(1) + 20, wide!.Location.Y, 0.1);
            // The named page's band must still be the wide (full-sheet) band.
            Assert.Equal(SheetH, container.PageBandHeightOf(1));
        }

        [Fact]
        public async Task MultiPageNamedContent_ChildrenFragmentAtNamedBands()
        {
            // Named element whose own children span multiple pages: the children's page-break
            // decisions must use the named page's band geometry, not the base band.
            var blocks = string.Concat(Enumerable.Range(0, 120).Select(i =>
                $"<p>flowing paragraph number {i}</p>"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page wide { margin-top: 10pt; margin-bottom: 10pt; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <p>normal page content</p>
                <div id='wide' style='page: wide'>{{blocks}}</div>
                </body></html>
                """);

            const double wideBand = SheetH - 10 - 10;

            Assert.Equal(BaseBand, container.PageBandHeightOf(0));
            Assert.Equal(wideBand, container.PageBandHeightOf(1));
            Assert.Equal(wideBand, container.PageBandHeightOf(2));

            var words = new List<CssRect>();
            CollectWords(container.Root!, words);
            Assert.NotEmpty(words);
            Assert.True(words.Max(w => w.Bottom) > container.PageTopOf(2),
                "fixture should span at least three pages");
            foreach (var word in words)
            {
                var topSlot = container.PageIndexOf(word.Top + HtmlContainerInt.PageBoundaryEpsilon);
                var bottomSlot = container.PageIndexOf(word.Bottom - HtmlContainerInt.PageBoundaryEpsilon);
                Assert.Equal(topSlot, bottomSlot);
            }
        }

        [Fact]
        public async Task FirstElementNamed_WithOwnMarginTop_Slot0UsesNamedRule()
        {
            // First content of the document sets a named page; the FIRST page is that named page
            // per css-page-3, even though the element's own Y sits below the slot top.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page cover { margin: 0; }
                div, p { margin: 0; }
                body { margin: 0; }
                </style></head><body>
                <div id='c' style='page: cover; margin-top: 20pt'>cover content</div>
                </body></html>
                """);

            Assert.Equal(SheetH, container.PageBandHeightOf(0));
        }

        [Fact]
        public async Task TableRelocatedByPreCheck_TailResyncMovesRegistrationToNewPage()
        {
            // CssLayoutEngineTable's whole-table pre-check assigns the table's Location directly
            // (bypassing OffsetTop's MoveNamedPageElement sync), so the tail of
            // CssBox.PerformLayoutImp must re-sync the early registration to the slot the table
            // actually ended up on. The name is pre-activated by an earlier element so the table
            // itself doesn't force a break (which would land it at a slot top and mask the move).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body { margin: 0; }
                div, p, table, td { margin: 0; }
                </style></head><body>
                <div style='page: tbl; height: 10pt'>x</div>
                <div style='height: 650pt'></div>
                <table id='t' style='page: tbl'><tr><td>row content</td></tr></table>
                </body></html>
                """);

            // Table top starts at 60 + 10 + 650 = 720, inside slot 0 (band [60, 732)); the pre-check's
            // one-line row estimate (~12-30pt) crosses the 732 boundary, relocating the whole table to
            // slot 1's top by direct Location assignment.
            var table = FindById(container.Root!, "t");
            Assert.NotNull(table);
            Assert.Equal(container.PageTopOf(1), table!.Location.Y, 1);

            var registrations = container.NamedPageElements.Where(e => e.Name == "tbl")
                .Select(e => e.Y).OrderBy(y => y).ToList();
            Assert.Equal(2, registrations.Count);
            Assert.Equal(container.PageTopOf(0), registrations[0], 1);
            Assert.Equal(container.PageTopOf(1), registrations[1], 1);
        }

        [Fact]
        public async Task NonBlockBox_RegistersViaTailFallback()
        {
            // A box that never enters PerformLayoutImp's block branch (default inline display) must
            // still register its named page through the method's tail fallback.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);

            var box = new CssBox(container.Root, null) { PageName = "tail" };
            await box.PerformLayout(graphics);

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "tail");
            Assert.Equal(container.PageTopOf(0), registered.Y, 1);
        }

        private static void CollectWords(CssBox box, List<CssRect> words)
        {
            foreach (var word in box.Words)
            {
                if (word.Width > 0 && word.Height > 0)
                    words.Add(word);
            }

            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

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
