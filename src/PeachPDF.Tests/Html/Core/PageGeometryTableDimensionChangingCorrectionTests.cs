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
    /// Tests for <see cref="PageGeometryTable.ProbeDimensionChangingCorrection"/> and
    /// <see cref="PageGeometryTable.MaterializedNumberOverrides"/> — issue #1041's narrow,
    /// relayout-gated residual on top of <see cref="PageGeometryTable.ResolveForMaterializedPage"/>'s
    /// existing zero-relayout correction (see <see cref="PageGeometryTableResolveForMaterializedPageTests"/>
    /// for that method's own tests). Mirrors that file's convention: builds <see cref="HtmlContainerInt.PageRules"/>
    /// directly from parsed <c>@page</c> rules, no real layout needed for the geometry-table pieces
    /// alone.
    /// </summary>
    public class PageGeometryTableDimensionChangingCorrectionTests
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
                // PageRuleResolver.ResolvePageSize silently falls back to the base size when this is
                // null (it needs an em/rem basis for a %/em/ex `size`, even though every fixture here
                // uses plain pt) - real documents always have one by the time layout runs
                // (HtmlContainerInt.SetHtml's own CascadeApplyPageStyles sets it), so a direct-construction
                // harness like this one has to set it itself or `size` overrides silently no-op.
                PageLengthContext = new PeachPDF.Html.Core.Entities.PageLengthContext(16, 16, 0),
            };
            return container;
        }

        [Fact]
        public void WidthOnlyChanges_ReturnsTheCorrectedCandidate()
        {
            // Same fixture as PageGeometryTableResolveForMaterializedPageTests's own
            // AsymmetricLeftRightMargins_DifferentBandWidth_ReturnsNull - ResolveForMaterializedPage
            // still declines it (unchanged contract), but the probe below is exactly the case that
            // method's own decline exists to hand off to the caller's relayout-gated path.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            var gridGeometry = container.PageGeometry.GetPage(2);
            var candidate = container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 2);

            Assert.NotNull(candidate);
            Assert.Equal(100, candidate!.Value.MarginLeftPt);
            Assert.Equal(40, candidate.Value.MarginRightPt);
            Assert.NotEqual(gridGeometry.BandWidth, candidate.Value.BandWidth);
            // Height is untouched - only width differs between :left and :right here.
            Assert.Equal(gridGeometry.BandHeight, candidate.Value.BandHeight);

            // ResolveForMaterializedPage's own contract is unchanged: it still declines this case.
            Assert.Null(container.PageGeometry.ResolveForMaterializedPage(2, materializedPageNumber: 2));
        }

        [Fact]
        public void HeightOnlyChanges_ReturnsTheCorrectedCandidate()
        {
            // :left's own `size` override shrinks the sheet HEIGHT only - its margins keep the same
            // BandWidth as :right/the base rule (both use the base 50pt left/right margin and the
            // base 612pt sheet width), isolating the height-only branch of the "exactly one dimension"
            // gate from the width-only case above.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { size: 612pt 500pt; margin: 60pt 50pt; }
                @page :right { margin-left: 50pt; margin-right: 50pt; }
                """);

            var gridGeometry = container.PageGeometry.GetPage(2);
            var candidate = container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 2);

            Assert.NotNull(candidate);
            Assert.Equal(500, candidate!.Value.SheetHeightPt);
            Assert.NotEqual(gridGeometry.BandHeight, candidate.Value.BandHeight);
            Assert.Equal(gridGeometry.BandWidth, candidate.Value.BandWidth);
        }

        [Fact]
        public void BothDimensionsChange_ReturnsNull_StaysOutOfScope()
        {
            // :left changes BOTH the margins (width) and the sheet size (height) relative to :right -
            // the residual this issue deliberately leaves open (see the accepted-gap file): nothing
            // here can tell "a relayout would fix the width" apart from "...and also the height",
            // so both changing at once is declined exactly like ResolveForMaterializedPage's own
            // width-and-height guard.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 100pt; size: 400pt 400pt; }
                @page :right { margin-left: 10pt; margin-right: 10pt; }
                """);

            var candidate = container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 2);

            Assert.Null(candidate);
        }

        [Fact]
        public void NamedPageActive_ReturnsNull_EvenThoughDimensionChanges()
        {
            // The same width-only-changing fixture as WidthOnlyChanges_ReturnsTheCorrectedCandidate,
            // but with a named page registered before this slot's top - a named-page run already
            // drives its own bounded-not-guaranteed convergence loop
            // (HtmlContainerInt.PageAssignmentSignature), and stacking this issue's independent
            // relayout mechanism on top of that one is explicitly out of scope (see
            // ProbeDimensionChangingCorrection's own remarks). There is no separate "flagged fragile
            // named-page run" registry beyond PageBandGeometry.ActiveName itself.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            var slot2Top = container.PageGeometry.GetPage(2).Top;
            container.RegisterNamedPageElement("chapter", slot2Top);

            var candidate = container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 2);

            Assert.Null(candidate);
        }

        [Fact]
        public void NumbersAgree_ReturnsNull()
        {
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            Assert.Null(container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 3));
        }

        [Fact]
        public void NoSelectorCarryingOverrides_ReturnsNullWithoutRecomputing()
        {
            var container = CreateContainer("@page { margin: 60pt 50pt; }");

            Assert.Null(container.PageGeometry.ProbeDimensionChangingCorrection(2, materializedPageNumber: 2));
        }

        [Fact]
        public void MaterializedNumberOverrides_MakesComputeUseTheOverriddenNumberForThatSlotOnly()
        {
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            // Slot 1 (grid page 2, even -> :left) is left alone; only slot 2 (grid page 3, odd ->
            // :right) is overridden to resolve as if it were materialized page 2 (even -> :left).
            container.PageGeometry.MaterializedNumberOverrides = new Dictionary<int, int> { [2] = 2 };
            container.PageGeometry.Reset();

            var slot1 = container.PageGeometry.GetPage(1);
            var slot2 = container.PageGeometry.GetPage(2);

            Assert.Equal(100, slot1.MarginLeftPt); // untouched - still resolves via its own grid number (2)
            Assert.Equal(100, slot2.MarginLeftPt); // overridden - now resolves as :left too
            Assert.Equal(40, slot2.MarginRightPt);
        }

        [Fact]
        public void MaterializedNumberOverrides_SurvivesReset()
        {
            // Reset() runs at the start of every LayoutDocument pass - the override has to survive it,
            // or HtmlContainerInt's own extra pass (which sets the override, then calls
            // LayoutDocument, which calls Reset() as its very first step) could never work at all.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            container.PageGeometry.MaterializedNumberOverrides = new Dictionary<int, int> { [2] = 2 };

            // Force slot 2 to be cached once under the override...
            Assert.Equal(100, container.PageGeometry.GetPage(2).MarginLeftPt);

            // ...then simulate LayoutDocument's own Reset() call and confirm the override still applies
            // to a freshly (re)computed slot 2.
            container.PageGeometry.Reset();

            Assert.Equal(100, container.PageGeometry.GetPage(2).MarginLeftPt);

            container.PageGeometry.MaterializedNumberOverrides = null;
            container.PageGeometry.Reset();

            Assert.Equal(20, container.PageGeometry.GetPage(2).MarginLeftPt);
        }
    }
}
