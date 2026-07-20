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
    /// Layout-level tests for variable per-page geometry: per-page <c>@page</c> rules overriding
    /// top/bottom margins give each pagination slot its own content-band height and cumulative top
    /// (CSS Paged Media 3 page-box model), consumed by layout page-break decisions through the
    /// <see cref="HtmlContainerInt.PageIndexOf"/>/<see cref="HtmlContainerInt.PageTopOf"/>/
    /// <see cref="HtmlContainerInt.PageBandHeightOf"/> helpers. Follows the repo's layout-harness
    /// convention (build a container, PerformLayout, assert box positions) with a harness mirroring
    /// PdfGenerator.SetContent's geometry derivation.
    /// </summary>
    public class PerPageGeometryLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        // Base fixture margins: @page { margin: 60pt 50pt } -> band 512 x 672 at (50, 60).
        private const double BaseMt = 60;
        private const double BaseMb = 60;
        private const double BaseBand = SheetH - BaseMt - BaseMb;

        [Fact]
        public async Task FirstPageMarginZero_BandCoversFullSheet()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            // Slot 0 reclaims the full sheet; document space stays anchored at the BASE content
            // origin (paint's per-page translate moves the band to the physical top edge).
            Assert.Equal(SheetH, container.PageBandHeightOf(0));
            Assert.Equal(BaseMt, container.PageTopOf(0));
            Assert.Equal(BaseMt + SheetH, container.PageTopOf(1));

            // Later slots return to the base band.
            Assert.Equal(BaseBand, container.PageBandHeightOf(1));
            Assert.Equal(BaseMt + SheetH + BaseBand, container.PageTopOf(2));
        }

        [Fact]
        public async Task ExactFitCover_ForcedBreak_NextContentAtSlot1Top_NoBlankPage()
        {
            // The cover is sized to slot 0's entire full-bleed band (792pt); its forced break must
            // target the boundary it already ends on (css-break-3: no empty fragmentainer for a
            // single forced break at a boundary), not a slot further.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                .cover { height: 792pt; background: rgb(20,20,20); page-break-after: always; }
                p { margin: 0; }
                </style></head><body>
                <div class='cover'></div>
                <p id='after'>after the cover</p>
                </body></html>
                """);

            var cover = FindByClass(container.Root!, "cover");
            var after = FindById(container.Root!, "after");
            Assert.NotNull(cover);
            Assert.NotNull(after);

            Assert.Equal(BaseMt, cover!.Location.Y, 0.1);
            Assert.Equal(BaseMt + SheetH, cover.ActualBottom, 0.1);
            Assert.Equal(BaseMt + SheetH, after!.Location.Y, 0.1);

            var slots = container.GetPaginationSlots();
            Assert.Equal([0, 1], slots.Select(s => s.SlotIndex));
        }

        [Fact]
        public async Task ConsecutiveForcedBreaks_StillProduceIntentionalBlankSlot()
        {
            // Break points A|B and B|C are distinct: B lands at slot 1's top via A's break-after,
            // and the break between B and C must still push C past B's slot - the flush-boundary
            // rule must not collapse an intentional blank page. B carries a token height because a
            // fully empty div is margin-collapse-through (CSS2.1 §8.3.1): the next sibling's
            // position collapses straight through it to A's bottom, defeating the bumped-
            // ActualBottom break mechanism entirely - a pre-existing engine boundary that predates
            // (and is unchanged by) the flush-boundary epsilon.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div style='page-break-after: always'>a</div>
                <div id='b' style='page-break-after: always; height: 1pt'></div>
                <p id='c'>c</p>
                </body></html>
                """);

            var b = FindById(container.Root!, "b");
            var c = FindById(container.Root!, "c");
            Assert.NotNull(b);
            Assert.NotNull(c);

            Assert.Equal(container.PageTopOf(1), b!.Location.Y, 0.1);
            Assert.Equal(container.PageTopOf(2), c!.Location.Y, 0.1);
        }

        [Fact]
        public async Task LeftRightDifferentTopMargins_AlternatingBandHeights_ContentBreaksAtVariableBoundaries()
        {
            // Odd (right) pages: margin-top 100 -> band 632; even (left) pages: margin-top 30 ->
            // band 702. Slot tops accumulate the alternating heights.
            var blocks = string.Concat(Enumerable.Range(0, 120).Select(i =>
                $"<p>flowing paragraph number {i}</p>"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :right { margin-top: 100pt; }
                @page :left { margin-top: 30pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>{{blocks}}</body></html>
                """);

            const double rightBand = SheetH - 100 - BaseMb; // 632
            const double leftBand = SheetH - 30 - BaseMb;   // 702

            Assert.Equal(rightBand, container.PageBandHeightOf(0));
            Assert.Equal(leftBand, container.PageBandHeightOf(1));
            Assert.Equal(rightBand, container.PageBandHeightOf(2));
            Assert.Equal(BaseMt + rightBand, container.PageTopOf(1));
            Assert.Equal(BaseMt + rightBand + leftBand, container.PageTopOf(2));

            // Text must fragment at the VARIABLE boundaries: no laid-out word may straddle a slot
            // boundary (CssRect.BreakPage relocates a straddling line to the next band's top), and
            // the flow must genuinely reach past the first two bands so several variable
            // boundaries were actually exercised.
            var words = new List<PeachPDF.Html.Core.Dom.CssRect>();
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

        private static void CollectWords(CssBox box, List<PeachPDF.Html.Core.Dom.CssRect> words)
        {
            foreach (var word in box.Words)
            {
                if (word.Width > 0 && word.Height > 0)
                    words.Add(word);
            }

            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        [Fact]
        public async Task NamedPage_FullBleedBand_StartsAtItsForcedBreakSlot_AndPropagates()
        {
            // The named-page change forces a break, so the wide band starts exactly at slot 1's
            // top; the name propagates forward, so slot 2 keeps the wide band too.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page wide { margin: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <p>normal page content</p>
                <div id='wide' style='page: wide'>wide content</div>
                </body></html>
                """);

            var wide = FindById(container.Root!, "wide");
            Assert.NotNull(wide);

            Assert.Equal(BaseBand, container.PageBandHeightOf(0));
            Assert.Equal(BaseMt + BaseBand, container.PageTopOf(1));
            Assert.Equal(container.PageTopOf(1), wide!.Location.Y, 0.1);
            Assert.Equal(SheetH, container.PageBandHeightOf(1));
            Assert.Equal(SheetH, container.PageBandHeightOf(2));
            Assert.Equal(BaseMt + BaseBand + SheetH, container.PageTopOf(2));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public async Task FirstPageMarginZero_BandMath_ScalesWithPixelsPerPoint(double ppp)
        {
            // Issue-#113 regression class: margins resolve in true points and scale by
            // PixelsPerPoint exactly once - the band math must be the pt-space result times ppp.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """, ppp);

            Assert.Equal(SheetH * ppp, container.PageBandHeightOf(0), 0.001);
            Assert.Equal(BaseBand * ppp, container.PageBandHeightOf(1), 0.001);
            Assert.Equal((BaseMt + SheetH) * ppp, container.PageTopOf(1), 0.001);
        }

        [Fact]
        public async Task DegenerateOverride_MarginsConsumeSheet_ClampsToBaseBand()
        {
            // 400 + 400 > 792: the override is discarded for band purposes so pagination always
            // advances; the slot falls back to the base band.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 400pt; margin-bottom: 400pt; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(BaseBand, container.PageBandHeightOf(0));
            Assert.Equal(BaseMt + BaseBand, container.PageTopOf(1));
        }

        // --- Harness (mirrors PdfGenerator.SetContent's geometry derivation) ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            // SetHtml runs CascadeApplyPageStyles: base @page margins land on the container
            // (already PixelsPerPoint-scaled) and PageRules are captured for per-page selection.
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

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var classAttr = box.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr) &&
                classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                return box;
            }

            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }

            return null;
        }
    }
}
