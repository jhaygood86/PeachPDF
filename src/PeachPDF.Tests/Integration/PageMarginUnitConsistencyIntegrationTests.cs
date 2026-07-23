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
    /// Issue #150: a textually identical <c>@page</c> margin must resolve to identical page geometry
    /// whether it sits in the base rule (resolved by <c>DomParser.CascadeApplyPageStyles</c> at parse
    /// time) or a per-page rule (resolved by <c>PageRuleResolver.ResolvePageMargins</c> through the
    /// captured <c>PageLengthContext</c> at band-geometry time) — for every supported unit, including
    /// spec-correct px (1px = 0.75pt), em/rem (root font), % (layout page width), and calc().
    /// </summary>
    public class PageMarginUnitConsistencyIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        [Theory]
        [InlineData("96px")]
        [InlineData("2em")]
        [InlineData("1.5rem")]
        [InlineData("10%")]
        [InlineData("calc(10% + 96px)")]
        public async Task BaseRule_And_FirstPageOverride_ResolveIdenticalMarginTop(string marginTop)
        {
            // Container A: the margin in the BASE rule -> lands on HtmlContainerInt.MarginTop.
            var containerA = await BuildAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin-top: {{marginTop}}; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            // Container B: the same margin in a :first override -> lands on slot 0's band geometry.
            var containerB = await BuildAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: {{marginTop}}; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            var basePt = containerA.MarginTop;                          // ppp = 1: layout units == pt
            var overridePt = containerB.PageGeometry.GetPage(0).MarginTopPt;

            Assert.True(basePt > 0, "fixture margin must actually resolve to a positive value");
            Assert.Equal(basePt, overridePt, 3);
        }

        [Fact]
        public async Task BaseRule_PxMargin_ResolvesSpecCorrect()
        {
            // CSS Values & Units §6.2: 1px = 1/96in = 0.75pt -> 96px is exactly 72pt, in the base
            // rule too (previously the base rule used the old engine 1px = 1pt convention: 96).
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 96px; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(72.0, container.MarginTop, 3);
            Assert.Equal(72.0, container.MarginLeft, 3);
        }

        [Fact]
        public async Task BaseRule_CalcPxMargin_ResolvesSpecCorrect()
        {
            // calc() px leaves go through the same shared conversion: 48px + 36pt = 36 + 36 = 72pt.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: calc(48px + 36pt); }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(72.0, container.MarginTop, 3);
        }

        [Fact]
        public async Task BaseRule_PxMargin_ScalesByPixelsPerPoint_ExactlyOnce()
        {
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 96px; }
                </style></head><body><p>content</p></body></html>
                """, ppp: 1.5);

            // 72pt resolved value, scaled into internal pixel space once: 72 x 1.5 = 108.
            Assert.Equal(108.0, container.MarginTop, 3);
        }

        [Fact]
        public async Task FirstPageEmMarginOverride_DrivesBandGeometry()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            var emPt = container.Root!.GetEmHeight(); // ppp = 1: layout units == pt
            var expectedFirstBand = SheetH - 2 * emPt - 60;

            Assert.Equal(2 * emPt, container.PageGeometry.GetPage(0).MarginTopPt, 3);
            Assert.Equal(expectedFirstBand, container.PageBandHeightOf(0), 3);
            // Later slots return to the base band.
            Assert.Equal(SheetH - 120, container.PageBandHeightOf(1), 3);
        }

        [Fact]
        public async Task FirstPageEmMarginOverride_ScalesByPixelsPerPoint_ExactlyOnce()
        {
            const double ppp = 1.5;
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """, ppp);

            // MarginTopPt stays in true points; the band height applies the ppp scaling exactly
            // once (issue-#113 discipline): band = sheetPx - (mT + mB) * ppp.
            var marginTopPt = container.PageGeometry.GetPage(0).MarginTopPt;
            var emPt = container.Root!.GetEmHeight() / ppp;
            Assert.Equal(2 * emPt, marginTopPt, 3);

            var sheetPx = container.PageSize.Height + container.MarginTop + container.MarginBottom;
            Assert.Equal(sheetPx - (marginTopPt + 60) * ppp, container.PageBandHeightOf(0), 3);
        }

        [Fact]
        public async Task FirstPageViewportUnitMargin_FallsBackToBaseMargin()
        {
            // vw has no meaningful page context: the per-page side must fall back to the base
            // margin, not silently resolve to a zero margin.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 10vw; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(60.0, container.PageGeometry.GetPage(0).MarginTopPt, 3);
            Assert.Equal(SheetH - 120, container.PageBandHeightOf(0), 3);
        }

        [Fact]
        public async Task BaseRule_ViewportOrChUnitMargin_LeavesConfiguredMarginUntouched()
        {
            // Issue #154: a BASE @page margin in a unit with no page context (vw/vh/vmin/vmax/ch) is
            // an invalid declaration — per CSS Syntax error handling it must be dropped, leaving the
            // previously-configured (PdfGenerateConfig/UA-default) margin in place, not silently
            // resolved to zero. This is the base-rule analog of
            // FirstPageViewportUnitMargin_FallsBackToBaseMargin's per-page assertion.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(SheetW, SheetH),
                MarginTop = 40,
                MarginLeft = 30,
            };

            await container.SetHtml("""
                <!DOCTYPE html><html><head><style>
                @page { margin-top: 10vw; margin-left: 3ch; }
                </style></head><body><p>content</p></body></html>
                """, null);

            Assert.Equal(40.0, container.MarginTop, 3);
            Assert.Equal(30.0, container.MarginLeft, 3);
        }

        [Fact]
        public async Task BaseRule_AbsoluteMargin_StillOverridesConfiguredMargin()
        {
            // Regression guard for the #154 fix: a resolvable base margin still overrides the
            // configured value (the null-aware routing must not drop valid declarations).
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(SheetW, SheetH),
                MarginTop = 40,
            };

            await container.SetHtml("""
                <!DOCTYPE html><html><head><style>
                @page { margin-top: 72pt; }
                </style></head><body><p>content</p></body></html>
                """, null);

            Assert.Equal(72.0, container.MarginTop, 3);
        }

        // ─── Issue #162: @page em/ex margins resolve against the @page context's own font-size ───

        [Fact]
        public async Task BaseRule_EmMargin_ResolvesAgainstPageFontSize()
        {
            // A base @page rule that sets its own font-size makes em/ex margins resolve against THAT font
            // (css-page-3 §7.1), not the root font: font-size:30pt => 2em = 60pt.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { font-size: 30pt; margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(60.0, container.MarginTop, 3);   // ppp = 1: layout units == pt
        }

        [Fact]
        public async Task FirstPage_EmMargin_ResolvesAgainstOwnPageFontSize()
        {
            // A per-page rule with its own font-size re-bases its em/ex margins on it: :first font-size:40pt
            // => margin-top:2em = 80pt.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { font-size: 40pt; margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(80.0, container.PageGeometry.GetPage(0).MarginTopPt, 3);
        }

        [Fact]
        public async Task FirstPage_EmMargin_InheritsBasePageFontSize()
        {
            // A per-page rule that does NOT set its own font-size inherits the base @page font for its em
            // basis: base font-size:30pt, :first margin-top:2em => 60pt (not the root font).
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { font-size: 30pt; margin: 60pt 50pt; }
                @page :first { margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(60.0, container.PageGeometry.GetPage(0).MarginTopPt, 3);
        }

        [Fact]
        public async Task BaseRule_EmMargin_NoPageFontSize_StaysRootBased()
        {
            // Regression: with no @page font-size, em margins keep resolving against the root font.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin-top: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Equal(2 * container.Root!.GetEmHeight(), container.MarginTop, 3);
        }

        /// <summary>
        /// Parse-only harness: PageSize carries the raw sheet BEFORE SetHtml so parse-time relative
        /// units (%/em against the captured PageLengthContext) resolve against a real width — same
        /// arrangement as GhostTextOnPreviousPageIntegrationTests' percentage-margin test.
        /// </summary>
        private static async Task<HtmlContainerInt> BuildAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(SheetW * ppp, SheetH * ppp)
            };
            await container.SetHtml(html, null);
            return container;
        }

        /// <summary>
        /// Full-layout harness mirroring PerPageGeometryLayoutIntegrationTests.BuildLayoutAsync:
        /// after SetHtml, PageSize becomes the content band and layout runs, so the band-geometry
        /// helpers (PageBandHeightOf / PageGeometry.GetPage) reflect production's derivation.
        /// </summary>
        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(SheetW * ppp, SheetH * ppp)
            };
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
