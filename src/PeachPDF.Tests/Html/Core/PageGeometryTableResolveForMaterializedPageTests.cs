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
    /// Tests for <see cref="PageGeometryTable.ResolveForMaterializedPage"/> — the paint-time
    /// correction for issue #148 (a content-empty gap skipped earlier in the document can leave a
    /// kept slot's raw grid number and its final materialized page number disagreeing on
    /// <c>:first</c>/<c>:left</c>/<c>:right</c> parity). Builds <see cref="HtmlContainerInt.PageRules"/>
    /// directly from parsed <c>@page</c> rules (mirroring <c>PageRuleResolverTests</c>'s own
    /// <c>ParsePageRule</c> convention) rather than running full layout — this method's own inputs
    /// (a slot index, a hypothetical page number) don't need real content, only the geometry table.
    /// </summary>
    public class PageGeometryTableResolveForMaterializedPageTests
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
        public void NumbersAgree_ReturnsNull_MeaningNothingToCorrect()
        {
            // No content-empty gap has been skipped before this slot - the ordinary case. The caller
            // is expected to fall back to its own already-known geometry (fragmentainer.Geometry).
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 40pt; margin-right: 100pt; }
                """);

            var resolved = container.PageGeometry.ResolveForMaterializedPage(2, materializedPageNumber: 3);

            Assert.Null(resolved);
        }

        [Fact]
        public void MirroredLeftRightMargins_ParityFlip_SwapsToTheOtherSidesMargins()
        {
            // Mirrored binding-gutter pair: equal total left+right margin (140pt) on both sides, only
            // the split differs - BandWidth is identical either way, so the substitution is safe.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 40pt; margin-right: 100pt; }
                """);

            // Grid slot 2 (page number 3, odd -> :right) but a skipped slot earlier means this slot's
            // materialized page number is actually 2 (even -> :left).
            var gridGeometry = container.PageGeometry.GetPage(2);
            Assert.Equal(40, gridGeometry.MarginLeftPt);
            Assert.Equal(100, gridGeometry.MarginRightPt);

            var resolved = container.PageGeometry.ResolveForMaterializedPage(2, materializedPageNumber: 2);

            Assert.NotNull(resolved);
            Assert.Equal(100, resolved!.Value.MarginLeftPt);
            Assert.Equal(40, resolved.Value.MarginRightPt);
            // The dimensions content was actually wrapped/fragmented against are unchanged.
            Assert.Equal(gridGeometry.BandWidth, resolved.Value.BandWidth);
            Assert.Equal(gridGeometry.BandHeight, resolved.Value.BandHeight);
            Assert.Equal(gridGeometry.Top, resolved.Value.Top);
        }

        [Fact]
        public void AsymmetricLeftRightMargins_DifferentBandWidth_ReturnsNull()
        {
            // :left's total margin (140pt) genuinely differs from :right's (40pt) - a parity flip
            // here would paint a page sized differently from what its content was wrapped against, so
            // the substitution must decline rather than risk a mismatch.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                """);

            var resolved = container.PageGeometry.ResolveForMaterializedPage(2, materializedPageNumber: 2);

            Assert.Null(resolved);
        }

        [Fact]
        public void FirstPageSizeOverride_DifferentBandHeight_ReturnsNull()
        {
            // :first's own `size` override changes the sheet (and so the band) height - even though
            // slot 0's materialized number could only ever be 1 in practice, this pins the guard's
            // height check independently of the width check above.
            var container = CreateContainer("""
                @page { margin: 60pt 50pt; }
                @page :first { size: 300pt 300pt; margin: 20pt; }
                """);

            var resolved = container.PageGeometry.ResolveForMaterializedPage(1, materializedPageNumber: 1);

            Assert.Null(resolved);
        }

        [Fact]
        public void NoSelectorCarryingOverrides_ReturnsNullWithoutRecomputing()
        {
            // Only the base @page rule is declared (no :first/:left/:right/named-page selector at
            // all) - no rule in the document could possibly vary by page number, so the numbers
            // disagreeing here is irrelevant: there is nothing a recompute could ever change.
            var container = CreateContainer("@page { margin: 60pt 50pt; }");

            var resolved = container.PageGeometry.ResolveForMaterializedPage(2, materializedPageNumber: 2);

            Assert.Null(resolved);
        }
    }
}
