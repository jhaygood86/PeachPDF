using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Tests for the page box's own border/padding (issue #1147) as modeled by
    /// <see cref="PageGeometryTable"/>: <see cref="PageGeometryTable.PageBandGeometry.BandWidth"/>/
    /// <see cref="PageGeometryTable.PageBandGeometry.BandHeight"/> shrinking by the resolved
    /// border+padding extent (on top of margin), the degenerate-override fallback, and the
    /// <see cref="PageGeometryTable.HasVerticalBorderPaddingOverrides"/>/
    /// <see cref="PageGeometryTable.HasHorizontalBorderPaddingOverrides"/> flags that route a
    /// border/padding-bearing document through this table at all (mirroring
    /// <see cref="PageGeometryTableResolveForMaterializedPageTests"/>'s own container-construction
    /// convention - no real layout is needed to exercise the geometry table directly).
    /// </summary>
    public class PageGeometryTableBorderPaddingTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMargin = 50;

        private static HtmlContainerInt CreateContainer(string pageRulesCss)
        {
            var rules = new StylesheetParser().Parse(pageRulesCss).Rules.OfType<PageRule>().ToList();
            var container = new HtmlContainerInt(new PdfSharpAdapter())
            {
                MarginLeft = BaseMargin,
                MarginTop = BaseMargin,
                MarginRight = BaseMargin,
                MarginBottom = BaseMargin,
                PageSize = new RSize(SheetW - 2 * BaseMargin, SheetH - 2 * BaseMargin),
                PageRules = rules,
            };
            return container;
        }

        [Fact]
        public void NoBorderOrPaddingDeclared_AllZero_BandUnaffected()
        {
            var container = CreateContainer("@page { margin: 50pt; }");

            var geom = container.PageGeometry.GetPage(0);

            Assert.Equal(0, geom.BorderLeftPt);
            Assert.Equal(0, geom.PaddingLeftPt);
            Assert.Equal(SheetW - 2 * BaseMargin, geom.BandWidth);
            Assert.Equal(SheetH - 2 * BaseMargin, geom.BandHeight);
            Assert.False(container.PageGeometry.HasVerticalBorderPaddingOverrides);
            Assert.False(container.PageGeometry.HasHorizontalBorderPaddingOverrides);
        }

        [Fact]
        public void BorderAndPaddingOnBaseRule_ShrinksBandOnEveryPage()
        {
            // The base (selector-less) rule counts for HasVerticalBorderPaddingOverrides/
            // HasHorizontalBorderPaddingOverrides, unlike the margin-override flags - there is no
            // separate "baked into container.MarginTop" fast path for border/padding, so even a
            // uniform, non-page-varying declaration must route through this table.
            var container = CreateContainer("@page { margin: 50pt; border: 5pt solid black; padding: 10pt; }");

            Assert.True(container.PageGeometry.HasVerticalBorderPaddingOverrides);
            Assert.True(container.PageGeometry.HasHorizontalBorderPaddingOverrides);

            var page0 = container.PageGeometry.GetPage(0);
            var page1 = container.PageGeometry.GetPage(1);

            foreach (var geom in new[] { page0, page1 })
            {
                Assert.Equal(5, geom.BorderLeftPt);
                Assert.Equal(5, geom.BorderTopPt);
                Assert.Equal(10, geom.PaddingLeftPt);
                Assert.Equal(10, geom.PaddingTopPt);
                // Content band shrinks by margin(50) + border(5) + padding(10) = 65pt per side.
                Assert.Equal(SheetW - 2 * 65, geom.BandWidth);
                Assert.Equal(SheetH - 2 * 65, geom.BandHeight);
                Assert.Equal(BaseMargin + 5 + 10, geom.ContentLeftPt);
                Assert.Equal(BaseMargin + 5 + 10, geom.ContentTopPt);
            }
        }

        [Fact]
        public void PaddingOnlyOnNamedPseudoRule_OnlyThatPageShrinks()
        {
            var container = CreateContainer("""
                @page { margin: 50pt; }
                @page :first { padding: 20pt; }
                """);

            var firstPage = container.PageGeometry.GetPage(0);
            var secondPage = container.PageGeometry.GetPage(1);

            Assert.Equal(20, firstPage.PaddingLeftPt);
            Assert.Equal(SheetW - 2 * (BaseMargin + 20), firstPage.BandWidth);

            Assert.Equal(0, secondPage.PaddingLeftPt);
            Assert.Equal(SheetW - 2 * BaseMargin, secondPage.BandWidth);
        }

        [Fact]
        public void DegenerateVerticalOverride_DiscardsBorderAndPaddingAlongsideMargin()
        {
            // Top+bottom margin+border+padding together would exceed the sheet height entirely -
            // the whole vertical contribution (margin AND border/padding) falls back to the base
            // margins with no border/padding, exactly as an over-large margin-only override already
            // discarded just the margin before #1147.
            var container = CreateContainer("@page { margin: 50pt; border-top: 800pt solid black; }");

            var geom = container.PageGeometry.GetPage(0);

            Assert.Equal(BaseMargin, geom.MarginTopPt);
            Assert.Equal(0, geom.BorderTopPt);
            Assert.Equal(SheetH - 2 * BaseMargin, geom.BandHeight);
        }

        [Fact]
        public void DegenerateHorizontalOverride_DiscardsBorderAndPaddingAlongsideBandWidth()
        {
            var container = CreateContainer("@page { margin: 50pt; padding-left: 700pt; }");

            var geom = container.PageGeometry.GetPage(0);

            // mL/mR themselves are NOT reset (matching the pre-#1147 asymmetry the class doc comment
            // already documents for margin) - only the derived band width and the border/padding used
            // to compute it fall back.
            Assert.Equal(BaseMargin, geom.MarginLeftPt);
            Assert.Equal(0, geom.PaddingLeftPt);
            Assert.Equal(SheetW - 2 * BaseMargin, geom.BandWidth);
        }

        [Fact]
        public void PercentagePadding_ResolvesAgainstTheSheetPerAxis()
        {
            var container = CreateContainer("@page { margin: 0; padding-left: 10%; padding-top: 10%; }");

            var geom = container.PageGeometry.GetPage(0);

            Assert.Equal(SheetW * 0.10, geom.PaddingLeftPt, 3);
            Assert.Equal(SheetH * 0.10, geom.PaddingTopPt, 3);
        }
    }
}
