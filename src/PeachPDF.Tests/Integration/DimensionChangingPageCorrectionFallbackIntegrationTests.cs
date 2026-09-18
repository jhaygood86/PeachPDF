using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-level fallback-safety coverage for issue #1041's narrow, relayout-gated correction
    /// (<see cref="HtmlContainerInt.TryApplyDimensionChangingPageCorrection"/>) - the property this
    /// issue's own task cared about most: a wrong "corrected" layout is worse than the honest gap
    /// <see cref="Html.Core.PageGeometryTable.ResolveForMaterializedPage"/> already leaves in place
    /// (see <c>.claude/accepted-gaps/left-right-page-geometry-vs-materialized-numbering.md</c>), so
    /// the one extra speculative pass must never be baked in unless it provably left every other
    /// page's content-emptiness and box-to-page assignment untouched. <see cref="LeftRightPageGeometryMaterializedNumberTests"/>
    /// covers the corrected case (and the still-out-of-scope both-dimensions-changing case) end to
    /// end through real PDF output; this file uses the direct <see cref="HtmlContainerInt"/>
    /// layout harness (this repo's convention for asserting box/fragment geometry without a full PDF
    /// round-trip - see <c>PerPageGeometryLayoutIntegrationTests</c>) to reach into the fragment tree
    /// directly.
    /// </summary>
    public class DimensionChangingPageCorrectionFallbackIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        [Fact]
        public async Task WidthChangeThatWouldReflowContentAcrossAPageBoundary_FallsBackToTheGridNumberedGeometry()
        {
            // The corrected pass's own re-resolution can genuinely disagree with itself: correcting
            // slot 2's width from :right's wide content box (572pt) to :left's narrower one (412pt)
            // changes how much room a box on that page has - here, via `aspect-ratio` (a definite
            // width -> auto height derivation with NO font-metric dependency, unlike text wrapping),
            // a box tall enough to overflow onto a third page at the WIDE (grid, :right) width but
            // short enough to fit entirely on one page at the NARROW (materialized, :left) width.
            //
            // Every size here is deterministic and font-metric-free (explicit-height marker boxes,
            // not text): the 20pt marker plus the 1324pt gap total EXACTLY two full 672pt bands, so
            // the aspect-ratio box starts precisely at slot 2's own top with its full band available -
            // at the WIDE width (572pt) its ratio-derived height (572 * 1.4 = 800.8pt) overflows that
            // 672pt band; at the NARROW width (412pt) its height (412 * 1.4 = 576.8pt) does not.
            //
            // TryApplyDimensionChangingPageCorrection must detect that its own corrected pass changed
            // the content-emptiness signature (3 kept slots before, only 2 after) and discard it,
            // falling back to a third, override-free pass that reproduces the original (declined)
            // 3-page, grid-numbered result exactly - not a partially-converged 2-page bake-in.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 100pt; }
                @page :right { margin-left: 20pt; margin-right: 20pt; }
                body { margin: 0; }
                </style></head><body>
                <div style='height: 20pt; background: rgb(0,0,0);'></div>
                <div style='height: 1324pt'></div>
                <div id='ar' style='width: 100%; aspect-ratio: 1 / 1.4; background: rgb(0,0,0);'></div>
                </body></html>
                """);

            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToList();

            // The grid-numbered (declined, unaffected) result: slot 0 (the marker), slot 1 skipped
            // (the 1324pt gap is content-empty), slot 2 + slot 3 (the aspect-ratio box, too tall for
            // one page at the WIDE :right width it's actually still using).
            Assert.Equal([0, 2, 3], slots);

            var slot2Geometry = container.FragmentTree.Fragmentainers.Single(f => f.SlotIndex == 2).Geometry;

            // The fallback-safety guarantee itself: slot 2 kept its GRID-numbered margin (:right,
            // 20pt), not the materialized-numbered one (:left, 100pt) the corrected-but-discarded
            // pass would otherwise have baked in.
            Assert.Equal(20, slot2Geometry.MarginLeftPt);
            Assert.Equal(20, slot2Geometry.MarginRightPt);

            // The override must never leak past the one speculative pass it was scoped to.
            Assert.Null(container.PageGeometry.MaterializedNumberOverrides);
        }

        [Fact]
        public async Task DocumentUsesFootnotes_DeclinesEvenAnOtherwiseEligibleSingleDimensionChange()
        {
            // A footnote body is wrapped by the SEPARATE footnote convergence loop
            // (HtmlContainerInt.ResolveFootnotesForThisAttempt), against the PRE-correction width, into
            // state (FootnoteAreaHeightsBySlot/the per-slot call list) that lives outside Root's own box
            // tree - so a corrected pass that never re-runs that loop would leave it stale while
            // AttachFootnoteAreas (which runs after this correction, against the POST-correction
            // geometry) sizes the footnote-area divider for the new width. TryApplyDimensionChangingPageCorrection
            // declines the whole correction outright whenever the document uses `float: footnote` at
            // all, rather than try to detect this after the fact - this fixture is otherwise IDENTICAL
            // to the width-only-changing case LeftRightPageGeometryMaterializedNumberTests proves DOES
            // get corrected, so the only variable is the footnote call added to the first paragraph.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 100pt; }
                @page :right { margin-left: 10pt; margin-right: 10pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p>page one content<sup style='float:footnote'>a footnote body</sup></p>
                <div style='height: 1500pt'></div>
                <p>page after the gap</p>
                </body></html>
                """);

            Assert.True(container.HasFootnotes, "fixture must actually exercise float: footnote");

            var slot2Geometry = container.FragmentTree!.Fragmentainers.Single(f => f.SlotIndex == 2).Geometry;

            // Declined: slot 2 keeps its GRID-numbered margin (:right, 10pt), not the
            // materialized-numbered one (:left, 100pt) an otherwise-identical footnote-free document
            // would have received (see LeftRightPageGeometryMaterializedNumberTests's own
            // AsymmetricLeftRightMargins_WidthOnlyChanges_CorrectsViaOneExtraRelayoutPass).
            Assert.Equal(10, slot2Geometry.MarginLeftPt);
            Assert.Equal(10, slot2Geometry.MarginRightPt);
            Assert.Null(container.PageGeometry.MaterializedNumberOverrides);
        }

        [Fact]
        public async Task MoreThanOneSlotProbesEligible_DeclinesTheCorrectionForAllOfThem()
        {
            // A single content-empty gap early in the document shifts EVERY later slot's materialized
            // number off its grid number by the same one-slot offset - so with :first/:left/:right
            // rules whose widths all differ from each other, more than one KEPT slot after the gap can
            // independently probe eligible for the width-only-changing correction (slot 1's grid rule,
            // :left, disagrees with its materialized rule, :first; slot 2's grid rule, :right, disagrees
            // with ITS materialized rule, :left - both width-changing, both otherwise eligible on their
            // own). TryApplyDimensionChangingPageCorrection's own gate (see its remarks on why exactly
            // one, not zero-or-many, gated slots is required) declines the correction entirely rather
            // than applying it to a subset - proven here by asserting BOTH slots keep their GRID-numbered
            // margins, not just one of them.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0pt; margin-right: 0pt; }
                @page :left { margin-left: 100pt; margin-right: 100pt; }
                @page :right { margin-left: 10pt; margin-right: 10pt; }
                body { margin: 0; }
                </style></head><body>
                <div style='height: 672pt'></div>
                <div style='height: 600pt; background: rgb(0,0,0);'></div>
                <div style='height: 600pt; background: rgb(0,0,0);'></div>
                </body></html>
                """);

            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToList();

            // slot 0 (the leading 672pt gap, exactly one content-empty band) is skipped; both markers
            // are kept, landing on slot 1 and slot 2.
            Assert.Equal([1, 2], slots);

            var slot1Geometry = container.FragmentTree.Fragmentainers.Single(f => f.SlotIndex == 1).Geometry;
            var slot2Geometry = container.FragmentTree.Fragmentainers.Single(f => f.SlotIndex == 2).Geometry;

            // Declined for BOTH: slot 1 keeps :left's grid-numbered margin (100pt), not :first's
            // materialized-numbered one (0pt); slot 2 keeps :right's grid-numbered margin (10pt), not
            // :left's materialized-numbered one (100pt).
            Assert.Equal(100, slot1Geometry.MarginLeftPt);
            Assert.Equal(10, slot2Geometry.MarginLeftPt);
            Assert.Null(container.PageGeometry.MaterializedNumberOverrides);
        }

        [Fact]
        public async Task NoPageSideOverrides_TheCorrectionMechanismNeverEngages()
        {
            // A document with no @page rule at all (not even a selector-less base one) can never
            // have a slot whose rule selection varies by page number - PageRules.Count == 0 is the
            // same cheap bail LayoutMarginBoxes's own ResolveForMaterializedPage call already uses.
            // Proven directly here (rather than only inferred from PDF output) by asserting the
            // override map this mechanism drives its one extra pass through is never even set.
            //
            // With no @page rule at all, margins default to 0 and the band is the full 792pt sheet -
            // the 20pt marker plus the 1564pt gap total exactly two full 792pt bands, so the same
            // "lands exactly on a slot boundary" shape as the fixture above puts the final marker
            // exactly at slot 2's top.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <div style='height: 20pt; background: rgb(0,0,0);'></div>
                <div style='height: 1564pt'></div>
                <div style='height: 20pt; background: rgb(0,0,0);'></div>
                </body></html>
                """);

            Assert.Equal([0, 2], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
            Assert.Null(container.PageGeometry.MaterializedNumberOverrides);
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
