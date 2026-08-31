using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer B of mixed page orientation/size support: <see cref="PageGeometryTable"/> resolving a
    /// per-slot physical sheet size (<see cref="PageBandGeometry.SheetWidthPt"/>/
    /// <see cref="PageBandGeometry.SheetHeightPt"/>) from a named/pseudo <c>@page</c> rule's own
    /// <c>size</c>, via <see cref="PageRuleResolver.ResolvePageSize"/>, mirroring exactly how margins
    /// already resolve per slot. Same harness convention as
    /// <see cref="NamedPageGeometryAttributionTests"/>/<see cref="PerPageGeometryLayoutIntegrationTests"/>.
    /// </summary>
    public class PerPageSizeGeometryLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMt = 60;
        private const double BaseMb = 60;

        [Fact]
        public async Task UniformDocument_NoSizeOverride_HasSizeOverridesIsFalse_AndEverySlotUsesBaseSheetSize()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                div, p { margin: 0; }
                </style></head><body>
                <div style="height: 2000pt">tall content spanning several pages</div>
                </body></html>
                """);

            Assert.False(container.PageGeometry.HasSizeOverrides);

            for (var slot = 0; slot < 3; slot++)
            {
                var geom = container.PageGeometry.GetPage(slot);
                Assert.Equal(SheetW, geom.SheetWidthPt, 3);
                Assert.Equal(SheetH, geom.SheetHeightPt, 3);
            }
        }

        [Fact]
        public async Task NamedPageWithExplicitSize_SlotGetsItsOwnSheetDimensions_OtherSlotsStayBase()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page wide { size: 900pt 400pt; margin: 20pt; }
                div, p { margin: 0; }
                </style></head><body>
                <p>default page one</p>
                <div style="page: wide; height: 50pt">wide section</div>
                <p>default page three</p>
                </body></html>
                """);

            Assert.True(container.PageGeometry.HasSizeOverrides);

            // Slot 0: default page, base sheet size.
            var slot0 = container.PageGeometry.GetPage(0);
            Assert.Equal(SheetW, slot0.SheetWidthPt, 3);
            Assert.Equal(SheetH, slot0.SheetHeightPt, 3);

            // Slot 1: the named page's own forced break lands it here, carrying its own sheet size.
            var slot1 = container.PageGeometry.GetPage(1);
            Assert.Equal(900, slot1.SheetWidthPt, 3);
            Assert.Equal(400, slot1.SheetHeightPt, 3);
            Assert.Equal(20, slot1.MarginLeftPt, 3);
            Assert.Equal(20, slot1.MarginTopPt, 3);

            // Slot 2: reverted to the default name - base sheet size restored.
            var slot2 = container.PageGeometry.GetPage(2);
            Assert.Equal(SheetW, slot2.SheetWidthPt, 3);
            Assert.Equal(SheetH, slot2.SheetHeightPt, 3);
        }

        [Fact]
        public async Task NamedPageWithNamedSizeAndOrientation_ResolvesIndependentlyOfTheBaseSheet()
        {
            // A4 landscape via the named-size keyword table - independent of the base Letter sheet,
            // confirming ResolvePageSize's named-size branch is reached (not just explicit lengths).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape-table { size: a4 landscape; margin: 15mm; }
                div, p { margin: 0; }
                </style></head><body>
                <p>default page one</p>
                <div style="page: landscape-table; height: 50pt">landscape section</div>
                </body></html>
                """);

            var slot1 = container.PageGeometry.GetPage(1);
            Assert.True(slot1.SheetWidthPt > slot1.SheetHeightPt);
            Assert.Equal(841.89, slot1.SheetWidthPt, 2);
            Assert.Equal(595.28, slot1.SheetHeightPt, 2);
        }

        [Fact]
        public async Task NamedPageSizeWithDegenerateMargins_KeepsTheResolvedSheetSize_MarginsFallBackToBase()
        {
            // The named rule's own top+bottom margins (300+300=600) consume more than its own resolved
            // 300pt-tall sheet - band-height purposes discard the margins and fall back to the base
            // document margins (60+60=120, which DOES fit the 300pt sheet), but the SHEET SIZE ITSELF
            // stays the named override (400x300), not the base (612x792): only the degenerate margin is
            // discarded, per PageGeometryTable.Compute's remarks.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page tiny { size: 400pt 300pt; margin: 300pt 20pt; }
                div, p { margin: 0; }
                </style></head><body>
                <p>default page one</p>
                <div style="page: tiny; height: 50pt">tiny section</div>
                </body></html>
                """);

            var slot1 = container.PageGeometry.GetPage(1);
            Assert.Equal(400, slot1.SheetWidthPt, 3);
            Assert.Equal(300, slot1.SheetHeightPt, 3);
            Assert.Equal(300 - BaseMt - BaseMb, slot1.BandHeight, 3); // resolved sheet's own band under base margins
            Assert.Equal(BaseMt, slot1.MarginTopPt, 3);
            Assert.Equal(BaseMb, slot1.MarginBottomPt, 3);
        }

        [Fact]
        public async Task NamedPageSizeSmallerThanBaseMargins_DiscardsMarginsEntirely_ReclaimsFullResolvedSheet()
        {
            // A resolved sheet so small even the BASE margins (60+60=120) don't fit its own 100pt
            // height - the second-level fallback discards margins entirely rather than let pagination
            // stall, reclaiming the full resolved sheet as the band (mirrors the base-sheet-only
            // "margin: 0" full-bleed pattern, but reached via a too-small size override instead).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page tiny { size: 400pt 100pt; margin: 300pt 20pt; }
                div, p { margin: 0; }
                </style></head><body>
                <p>default page one</p>
                <div style="page: tiny; height: 50pt">tiny section</div>
                </body></html>
                """);

            var slot1 = container.PageGeometry.GetPage(1);
            Assert.Equal(400, slot1.SheetWidthPt, 3);
            Assert.Equal(100, slot1.SheetHeightPt, 3);
            Assert.Equal(100, slot1.BandHeight, 3);
            Assert.Equal(0, slot1.MarginTopPt, 3);
            Assert.Equal(0, slot1.MarginBottomPt, 3);
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
    }
}
