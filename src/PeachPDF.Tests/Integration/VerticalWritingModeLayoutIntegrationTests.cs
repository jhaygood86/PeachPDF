using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end layout tests for real <c>vertical-rl</c>/<c>vertical-lr</c> layout: both line flow
    /// (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngine.CreateVerticalLineBoxes"/>) and block-level
    /// child placement (<c>CssBox.LayoutVerticalBlockChildren</c>, issue #760), asserting actual
    /// <c>CssBox</c>/word geometry after layout - not just that layout completes - per this repo's testing
    /// conventions for layout-engine changes.
    /// </summary>
    public class VerticalWritingModeLayoutIntegrationTests
    {
        [Fact]
        public async Task VerticalRl_BlockChildren_AreTreatedAsMonolithicToo_ButAnEngineOfItsOwnStaysExcluded()
        {
            // Issue #760 gave a vertical box with block-level children its own dispatch
            // (LayoutVerticalBlockChildren), so - unlike before that landed - IsUnresumableOrthogonalFlow
            // must now report true for the wrapper too, exactly like the inline-only case already did:
            // the whole subtree is laid out in one unresumable pass. What must still stay excluded is a
            // box that runs an engine of its own (Flex/Grid/Table), since that engine already hands back
            // a real, resumable per-item/per-row break record regardless of writing-mode.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <p id="p1">First paragraph.</p>
                  <p>Second paragraph.</p>
                </div>
                <div id="flexWrapper" style="writing-mode: vertical-rl; display: flex">
                  <p>Flex item.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var firstParagraph = LayoutHarness.FindById(root, "p1");
            var flexWrapper = LayoutHarness.FindById(root, "flexWrapper");
            Assert.NotNull(wrapper);
            Assert.NotNull(firstParagraph);
            Assert.NotNull(flexWrapper);

            Assert.Equal(WritingMode.VerticalRl, wrapper!.WritingMode.Value);
            Assert.Equal(WritingMode.VerticalRl, firstParagraph!.WritingMode.Value);

            // The wrapper's own block (<p>) children now go through LayoutVerticalBlockChildren, so the
            // wrapper itself is unresumable too.
            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(wrapper));

            // A <p> holding only text is itself inline-only and still goes through CreateVerticalLineBoxes.
            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(firstParagraph));

            // A flex container runs an engine of its own, so it stays excluded even under a vertical
            // writing mode with block-level items.
            Assert.False(MonolithicContent.IsUnresumableOrthogonalFlow(flexWrapper));
        }

        [Fact]
        public async Task VerticalRl_MultiColumnBox_IsUnresumableOnlyWhenInlineOnly_NotWhenItHasBlockChildren()
        {
            // CssBox.LayoutContents checks the inlines-only vertical branch BEFORE the multi-column branch
            // (CssBox.cs), so a vertical box that is both multi-column AND inline-only still dispatches to
            // the unresumable CreateVerticalLineBoxes, never to the genuinely resumable
            // CssLayoutEngineColumns - IsUnresumableOrthogonalFlow must report true for it (a post-change
            // review caught this as a real regression when the predicate briefly checked only
            // !EstablishesMultiColumnContext, dropping ContainsInlinesOnly entirely). A multi-column box
            // with genuine block-level children, by contrast, does reach CssLayoutEngineColumns and must
            // stay resumable.
            var html = LayoutHarness.Wrap("""
                <div id="inlineOnly" style="writing-mode: vertical-rl; column-count: 2; width: 300px; height: 100px">
                  Plain inline text content, nothing block-level here.
                </div>
                <div id="withBlockChildren" style="writing-mode: vertical-rl; column-count: 2; width: 300px; height: 100px">
                  <p>A block-level paragraph child.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var inlineOnly = LayoutHarness.FindById(root, "inlineOnly");
            var withBlockChildren = LayoutHarness.FindById(root, "withBlockChildren");
            Assert.NotNull(inlineOnly);
            Assert.NotNull(withBlockChildren);

            Assert.True(inlineOnly!.EstablishesMultiColumnContext);
            Assert.True(withBlockChildren!.EstablishesMultiColumnContext);

            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(inlineOnly));
            Assert.False(MonolithicContent.IsUnresumableOrthogonalFlow(withBlockChildren));
        }

        [Fact]
        public async Task VerticalRl_FewModestBlockChildren_StackAlongTheBlockAxisAndFitOnOnePage()
        {
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <p id="p1" style="width: 60px">First.</p>
                  <p id="p2" style="width: 60px">Second.</p>
                  <p id="p3" style="width: 60px">Third.</p>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var p1 = LayoutHarness.FindById(root, "p1");
            var p2 = LayoutHarness.FindById(root, "p2");
            var p3 = LayoutHarness.FindById(root, "p3");
            Assert.NotNull(p1);
            Assert.NotNull(p2);
            Assert.NotNull(p3);

            Assert.NotNull(container.FragmentTree);
            Assert.Single(container.FragmentTree!.Fragmentainers);

            // vertical-rl stacks block children right-to-left along the block axis (physical X), all at
            // the same cross-axis (physical Y) start.
            Assert.True(p1!.Location.X > p2!.Location.X, "vertical-rl should stack right-to-left");
            Assert.True(p2.Location.X > p3!.Location.X, "vertical-rl should stack right-to-left");
            Assert.Equal(p1.Location.Y, p2.Location.Y, 1);
            Assert.Equal(p2.Location.Y, p3.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_TallSingleBlockChild_OverflowsMonolithicallyRatherThanBeingSliced()
        {
            // Documents the deliberate scope boundary from issue #760: a vertical box's own block-axis
            // content is monolithic w.r.t. its parent's fragmentation (like the inline-only case already
            // was), so a child tall enough to exceed one page's own band is not sliced across pages - real
            // per-child fragmentation of a vertical box's block content is tracked separately (#767).
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <div id="tall" style="width: 60pt; height: 500pt">Tall child.</div>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 200);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var tall = LayoutHarness.FindById(root, "tall");
            Assert.NotNull(wrapper);
            Assert.NotNull(tall);

            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(wrapper));
            Assert.Equal(500, tall!.ActualBottom - tall.Location.Y, 1);
            Assert.NotNull(container.FragmentTree);
        }

        [Fact]
        public async Task VerticalLr_BlockChildren_StackLeftToRightAlongThePhysicalBlockAxis()
        {
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-lr">
                  <p id="p1" style="width: 60px">First.</p>
                  <p id="p2" style="width: 60px">Second.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p1 = LayoutHarness.FindById(root, "p1");
            var p2 = LayoutHarness.FindById(root, "p2");
            Assert.NotNull(p1);
            Assert.NotNull(p2);

            Assert.True(p1!.Location.X < p2!.Location.X, "vertical-lr should stack left-to-right");
            Assert.Equal(p1.Location.Y, p2.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_AutoWidthAndHeight_ShrinkToAccumulatedBlockAndCrossAxisExtent()
        {
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; height: 60pt">A</div>
                  <div id="b" style="width: 30pt; height: 90pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            Assert.NotNull(wrapper);

            // Auto width shrinks to the combined block-axis extent of both children (40 + 30 = 70, no
            // margins on either).
            Assert.Equal(70, wrapper!.ActualRight - wrapper.Location.X, 1);

            // Auto height shrinks to the taller child's own cross-axis (physical Y) extent (90, not
            // 60 + 90 - the children stack side by side along X, not stacked along Y).
            Assert.Equal(90, wrapper.ActualBottom - wrapper.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_MarginsBetweenBlockChildren_CollapseToTheLarger()
        {
            // Issue #776: adjoining margins between block-axis-stacked siblings really collapse per CSS2.1
            // SS8.3.1 (max, not sum) - closing the deliberate scope boundary #760 left behind (previously
            // pinned down as a cheap sum by this same test, then named ...AreSummedNotCollapsed).
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin: 0 10pt">A</div>
                  <div id="b" style="width: 40pt; margin: 0 10pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // vertical-rl: a sits to the right of b. The gap between a's own left edge and b's own right
            // edge is the max of a's own left margin and b's own right margin (10, not their sum, 20).
            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(10, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_MismatchedSignMarginsBetweenBlockChildren_CollapseToMaxPositivePlusMinNegative()
        {
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin-left: 8pt">A</div>
                  <div id="b" style="width: 40pt; margin-right: -3pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // CSS2.1 SS8.3.1: max positive (8) plus min negative (-3) = 5.
            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(5, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_BothNegativeMarginsBetweenBlockChildren_CollapseToTheMoreNegative()
        {
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin-left: -4pt">A</div>
                  <div id="b" style="width: 40pt; margin-right: -9pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // CSS2.1 SS8.3.1: both negative - the more-negative (-9) wins, giving a NEGATIVE gap (overlap).
            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(-9, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_SelfCollapsingEmptyChildBetweenTwoSiblings_FoldsIntoOneSharedGroup()
        {
            // A self-collapsing (empty, zero resolved block-axis extent) middle child folds its own two
            // margins into the SAME adjoining set as its neighbors (CSS2.1 SS8.3.1), rather than resolving
            // pairwise - the gap between the two REAL siblings should be the max across all three adjoining
            // margins (a's own 5, empty's own 20 and 3, b's own 8), not just max(a,b).
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin-left: 5pt">A</div>
                  <div id="empty" style="width: 0; margin-right: 20pt; margin-left: 3pt; overflow: visible"></div>
                  <div id="b" style="width: 40pt; margin-right: 8pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(20, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_NestedSelfCollapsingEmptyChildren_StillCollapseAsOneGroup()
        {
            // The self-collapse check recurses into a collapse-through child's own in-flow children
            // (mirrors IsMarginCollapseThrough's own recursive definition) - a zero-width wrapper around
            // another zero-width, zero-margin box is still itself self-collapsing, so its own (larger)
            // margin still joins the shared group between the two real siblings.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin-left: 5pt">A</div>
                  <div id="outer" style="width: 0; margin-right: 15pt; overflow: visible">
                    <div id="inner" style="width: 0; overflow: visible"></div>
                  </div>
                  <div id="b" style="width: 40pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(15, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_SelfCollapsingWrapperWithTwoSelfCollapsingSiblingChildren_BothChildrensMarginsJoinTheGroup()
        {
            // A self-collapsing wrapper's own leading chain-walk (FoldOwnAdjoiningBlockStartMargins) only
            // follows its FIRST in-flow child - a second (or later) self-collapsing sibling descendant is
            // reached only via a full self-collapsing-subtree fold (FoldSelfCollapsingBlockMargins,
            // mirroring FoldSelfCollapsingMargins for ordinary horizontal-tb flow). m2's own 30pt margin
            // (larger than m1's 9pt) must still dominate the group even though only m1 sits on the
            // leading chain the wrapper's own positioning walk follows.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div id="a" style="width: 40pt; margin-left: 5pt">A</div>
                  <div id="wrapper" style="width: 0; overflow: visible">
                    <div id="m1" style="width: 0; margin-right: 9pt; overflow: visible"></div>
                    <div id="m2" style="width: 0; margin-left: 30pt; overflow: visible"></div>
                  </div>
                  <div id="b" style="width: 40pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(30, gap, 1);
        }

        [Fact]
        public async Task VerticalRl_NestedVerticalRlWrapper_MultiLevelChainCollapsesTogether()
        {
            // Issue #776's box-own-edge case: a vertical-rl wrapper whose own first stacked child is ALSO
            // a vertical-rl box (non-orthogonal, same writing mode) with a block-start-adjoining margin of
            // its own further down. Box-own-edge collapse only has anything to fold against when the
            // OUTER context shares the same block axis (an ordinary horizontal-tb parent's own top/bottom
            // axis is unrelated to a vertical child's internal left/right one, so there's nothing to
            // collapse there - see the CollapsedMarginBeforeTests writing-mode-boundary test for that
            // case) - nested vertical-in-vertical is the case where it applies.
            //
            // Exactly mirroring FoldOwnAdjoiningBlockStartMargins's own multi-level chain for ordinary
            // horizontal-tb flow: the OUTERMOST box in the chain (here, "inner", positioned by "outer")
            // ends up with the group's FULL collapsed value as ITS OWN gap from its parent, while every
            // DEEPER chain member (here, "grandchild") sits flush against ITS OWN immediate parent - the
            // group value is "spent" exactly once, at the outermost level, not re-applied at every level.
            var html = LayoutHarness.Wrap("""
                <div id="outer" style="writing-mode: vertical-rl; width: 100pt">
                  <div id="inner" style="writing-mode: vertical-rl; width: 60pt">
                    <div id="grandchild" style="width: 30pt; margin-right: 18pt">Grandchild.</div>
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var outer = LayoutHarness.FindById(root, "outer");
            var inner = LayoutHarness.FindById(root, "inner");
            var grandchild = LayoutHarness.FindById(root, "grandchild");
            Assert.NotNull(outer);
            Assert.NotNull(inner);
            Assert.NotNull(grandchild);

            // "inner" carries the full 18pt collapsed value as its own gap from "outer" (grandchild's
            // margin escaped past inner's own zero border/padding block-start edge).
            Assert.Equal(18, outer!.ClientRight - inner!.ActualRight, 1);

            // "grandchild" sits flush against "inner" - its own margin was already "spent" one level up.
            Assert.Equal(inner.ClientRight, grandchild!.ActualRight, 1);
        }

        [Fact]
        public async Task VerticalRl_NestedVerticalRlWrapper_TrailingEdgeChainCollapsesTogether()
        {
            // Issue #776's box-own-edge case, trailing side: mirrors the leading-side test above but for
            // the block-end (physical left) edge - "inner"'s own trailing margin (30pt) is larger than its
            // only child "grandchild"'s own trailing margin (12pt), so folding them together (rather than
            // using grandchild's raw contribution alone) demonstrably changes inner's own shrink-to-fit
            // width: 30(grandchild's width) + 30(the larger, collapsed value) = 60, not 30 + 12 = 42.
            var html = LayoutHarness.Wrap("""
                <div id="outer" style="writing-mode: vertical-rl; width: 100pt">
                  <div id="inner" style="writing-mode: vertical-rl; margin-left: 30pt">
                    <div id="grandchild" style="width: 30pt; margin-left: 12pt">Grandchild.</div>
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var inner = LayoutHarness.FindById(root, "inner");
            Assert.NotNull(inner);

            Assert.Equal(60, inner!.ActualRight - inner.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRl_MirroredDirectionFirstChild_BoxOwnEdgeCollapseStopsAtTheBoundary()
        {
            // A vertical-rl wrapper whose first child is vertical-lr (mirrored block-start/end physical
            // mapping) is a writing-mode boundary the chain must stop at - the child's own margin still
            // collapses once (against the wrapper's own edge), but the chain must not continue into the
            // mirrored child's own first grandchild, which would otherwise misread the wrong physical side.
            var html = LayoutHarness.Wrap("""
                <div id="outer" style="writing-mode: vertical-rl; width: 100pt">
                  <div id="mirrored" style="writing-mode: vertical-lr; width: 60pt">
                    <div id="grandchild" style="width: 30pt; margin-left: 18pt">Grandchild.</div>
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var outer = LayoutHarness.FindById(root, "outer");
            var mirrored = LayoutHarness.FindById(root, "mirrored");
            var grandchild = LayoutHarness.FindById(root, "grandchild");
            Assert.NotNull(outer);
            Assert.NotNull(mirrored);
            Assert.NotNull(grandchild);

            // The mirrored child itself sits flush against the outer wrapper's own block-start edge (its
            // own margin-right is 0/auto, so the chain resolves to 0 here) ...
            Assert.Equal(outer!.ClientRight, mirrored!.ActualRight, 1);

            // ... but the grandchild's OWN block-start margin (18pt, on the mirrored child's own physical
            // left side) must NOT have been folded into that same outer chain - it stays purely internal
            // to the mirrored child's own (vertical-lr) stacking, leaving a real 18pt gap from the mirrored
            // child's own block-start (physical left) content edge.
            Assert.Equal(18, grandchild!.Location.X - mirrored.ClientLeft, 1);
        }

        [Fact]
        public async Task VerticalRl_RunningPositionedAndOutOfFlowChildren_AreSkippedFromStackingButDoNotCrash()
        {
            // position: running() (css-gcpm-3) and out-of-flow (float/absolute/fixed) children are
            // excluded from - or, for out-of-flow, routed around - LayoutVerticalBlockChildren's own
            // block-axis stacking loop, the same way LayoutBlockChildren's ordinary loop already treats
            // them (issue #768's existing scope boundary for floats/positioning inside vertical content).
            // Only the two ordinary in-flow children below should participate in the block-axis stacking;
            // nothing should crash.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl">
                  <div style="position: running(aside)"><p>Running content.</p></div>
                  <div style="float: left; width: 20pt; height: 20pt">Float.</div>
                  <p id="p1" style="width: 40pt">First.</p>
                  <p id="p2" style="width: 40pt">Second.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p1 = LayoutHarness.FindById(root, "p1");
            var p2 = LayoutHarness.FindById(root, "p2");
            Assert.NotNull(p1);
            Assert.NotNull(p2);

            // The running-positioned and floated children contribute nothing to the block-axis cursor,
            // so the two ordinary children still stack immediately adjacent to each other (40pt apart).
            Assert.Equal(40, p1!.Location.X - p2!.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRl_Rtl_AutoHeight_BlockChildrenAnchorToBottomEdge_ShorterChildLeavesGapAtTop()
        {
            // Issue #778: direction: rtl's inline-start is the physical bottom edge (CSS Writing Modes 4),
            // not the physical top ltr uses - every stacked child should hang flush from the box's own
            // (here, shrink-wrapped) bottom, with a shorter child's own unused space appearing as a gap at
            // the top rather than the bottom.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; direction: rtl">
                  <div id="a" style="width: 40pt; height: 60pt">A</div>
                  <div id="b" style="width: 30pt; height: 100pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(wrapper);
            Assert.NotNull(a);
            Assert.NotNull(b);

            // Auto height shrinks to the taller child's own extent (100), same as the ltr case.
            Assert.Equal(100, wrapper!.ActualBottom - wrapper.Location.Y, 1);

            // Both children hang flush from the wrapper's own (shrink-wrapped) bottom edge.
            Assert.Equal(wrapper.ActualBottom, a!.ActualBottom, 1);
            Assert.Equal(wrapper.ActualBottom, b!.ActualBottom, 1);

            // The shorter child (a, 60pt tall in a 100pt-tall box) shows its 40pt gap at the top, not the
            // bottom - its own top sits below the wrapper's own ClientTop.
            Assert.Equal(wrapper.ClientTop + 40, a.Location.Y, 1);
            // The tallest child (b) needed no repositioning: its top is already flush with ClientTop.
            Assert.Equal(wrapper.ClientTop, b.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_Rtl_ExplicitHeight_BlockChildrenAnchorToTheBoxsRealBottomEdge_NotJustTheTallestChild()
        {
            // An explicit height taller than every child must still be the edge children hang from - a
            // naive "reflect around the tallest child" implementation would anchor to 100 (b's own extent)
            // instead of the box's real 200pt content height, leaving a visible gap at the wrong edge.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; direction: rtl; height: 200pt">
                  <div id="a" style="width: 40pt; height: 60pt">A</div>
                  <div id="b" style="width: 30pt; height: 100pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(wrapper);
            Assert.NotNull(a);
            Assert.NotNull(b);

            // Both children hang flush from the box's own real (explicit) bottom edge - not just b's own
            // 100pt extent.
            Assert.Equal(wrapper!.ClientBottom, a!.ActualBottom, 1);
            Assert.Equal(wrapper.ClientBottom, b!.ActualBottom, 1);

            // Gaps at the top scale with each child's own shortfall against the real 200pt content height.
            Assert.Equal(wrapper.ClientTop + 140, a.Location.Y, 1);
            Assert.Equal(wrapper.ClientTop + 100, b.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalLr_Rtl_BlockChildrenAlsoAnchorToBottom()
        {
            // The cross-axis anchor is independent of which physical edge the block axis itself grows
            // from - vertical-lr + direction: rtl should reflect on the cross axis exactly like vertical-rl
            // does.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-lr; direction: rtl">
                  <div id="a" style="width: 40pt; height: 60pt">A</div>
                  <div id="b" style="width: 30pt; height: 100pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(wrapper);
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(wrapper!.ActualBottom, a!.ActualBottom, 1);
            Assert.Equal(wrapper.ActualBottom, b!.ActualBottom, 1);
            Assert.Equal(wrapper.ClientTop + 40, a.Location.Y, 1);
            Assert.Equal(wrapper.ClientTop, b.Location.Y, 1);

            // vertical-lr still stacks the block axis left-to-right, unaffected by direction.
            Assert.True(a.Location.X < b.Location.X, "vertical-lr should still stack left-to-right");
        }

        [Fact]
        public async Task VerticalRl_Rtl_MinHeightGrowsTheBoxAfterContent_ChildrenStillAnchorToTheRealFinalBottom()
        {
            // A post-change review of the first cut of this fix found that reflecting against the
            // content-driven extent (computed inside LayoutVerticalBlockChildren, before min-height is
            // applied in the epilogue) would anchor children to a stale, pre-clamp edge whenever min-height
            // grows the box past its own content - reintroducing a version of #778's own symptom. The fix
            // defers reflection to PerformLayoutEpilogue, after CssLayoutEngine.ApplyHeight has settled
            // min-height, so this must anchor to the box's real 300pt min-height bottom, not to 100pt (the
            // taller child's own content-only extent).
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; direction: rtl; min-height: 300pt">
                  <div id="a" style="width: 40pt; height: 60pt">A</div>
                  <div id="b" style="width: 30pt; height: 100pt">B</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(wrapper);
            Assert.NotNull(a);
            Assert.NotNull(b);

            // min-height won: the box's own real height is 300pt, not the 100pt its tallest child alone
            // would content-drive it to.
            Assert.Equal(300, wrapper!.ActualBottom - wrapper.Location.Y, 1);

            // Both children hang flush from the box's real (min-height-driven) bottom edge, not from the
            // 100pt content-only edge a pre-clamp reflection would have used.
            Assert.Equal(wrapper.ClientBottom, a!.ActualBottom, 1);
            Assert.Equal(wrapper.ClientBottom, b!.ActualBottom, 1);
            Assert.Equal(wrapper.ClientTop + 240, a.Location.Y, 1);
            Assert.Equal(wrapper.ClientTop + 200, b.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_OrthogonalHorizontalChild_LaysOutOwnContentHorizontally_WhilePlacedAsOneAtomicBlock()
        {
            // A writing-mode: horizontal-tb block child nested inside a vertical-rl parent is an
            // orthogonal flow (CSS Writing Modes 4 SS4.3). Its own recursive LayoutContents dispatch is
            // driven entirely by its own WritingMode.Value, so it lays its own lines out along physical Y
            // exactly as it would anywhere else - LayoutVerticalBlockChildren needs no special case for it
            // at all, and simply places its whole resulting box as one atomic unit along the parent's own
            // block axis (physical X).
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <div id="ortho" style="writing-mode: horizontal-tb; width: 100px">
                    One two three four five six seven eight nine ten eleven twelve.
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var ortho = LayoutHarness.FindById(root, "ortho");
            Assert.NotNull(wrapper);
            Assert.NotNull(ortho);

            Assert.Equal(WritingMode.HorizontalTb, ortho!.WritingMode.Value);

            // The orthogonal child's own content wraps onto multiple physical-Y rows within its own box -
            // ordinary horizontal-tb line flow, unaffected by the parent's own vertical writing mode.
            var distinctTops = ortho.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak)
                .Select(w => System.Math.Round(w.Top)).Distinct().OrderBy(t => t).ToList();
            Assert.True(distinctTops.Count > 1, "a 100px-wide box should force this sentence to wrap onto multiple lines");

            // The child itself is placed as one atomic block along the parent's own block axis: it sits
            // flush against the wrapper's own block-start (right, under vertical-rl) edge.
            Assert.Equal(wrapper!.ClientRight, ortho.ActualRight, 1);
        }

        [Fact]
        public async Task VerticalRl_OrthogonalAutoWidthChild_ShrinksToFitInsteadOfStretchingToTheWrapper()
        {
            // Issue #777: CSS Writing Modes 4 SS4.3 requires an auto-sized orthogonal flow root (this
            // child's own writing-mode is horizontal-tb, perpendicular to the vertical-rl wrapper) to be
            // sized via shrink-to-fit, not ordinary stretch-to-containing-block auto-width. A wrapper much
            // wider than "Hi" needs, so a stretch-filled child (the pre-#777 bug) would be trivially
            // distinguishable from a shrink-to-fit one.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 400px">
                  <div id="ortho" style="writing-mode: horizontal-tb">Hi</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var ortho = LayoutHarness.FindById(root, "ortho");
            Assert.NotNull(wrapper);
            Assert.NotNull(ortho);

            var childWidth = ortho!.ActualRight - ortho.Location.X;

            // Shrink-to-fit: the child's own physical width is its content's max-content width, nowhere
            // near the wrapper's full 300pt (400px) client width.
            Assert.True(childWidth < 100,
                $"expected the orthogonal auto-width child to shrink to its content ('Hi'), got {childWidth}pt wide");

            // It still sits flush against the wrapper's own block-start (right, under vertical-rl) edge -
            // shrink-to-fit changes the child's own width, not where the block-axis stacking anchors it.
            Assert.Equal(wrapper!.ClientRight, ortho.ActualRight, 1);
        }

        [Fact]
        public async Task VerticalRl_OrthogonalAutoWidthChild_StillFillsTheConstraintWhenContentIsWideEnough()
        {
            // The other half of shrink-to-fit's min(max-content, max(min-content, constraint)): content
            // wide enough to need the whole available block-axis extent still gets it, exactly as ordinary
            // stretch-to-containing-block auto-width already did before #777 - shrink-to-fit only ever
            // narrows a child, never widens one past what stretch would have given it.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 200px">
                  <div id="stretch" style="writing-mode: horizontal-tb">
                    One two three four five six seven eight nine ten eleven twelve thirteen fourteen.
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var stretch = LayoutHarness.FindById(root, "stretch");
            Assert.NotNull(wrapper);
            Assert.NotNull(stretch);

            var childWidth = stretch!.ActualRight - stretch.Location.X;
            var constraint = wrapper!.ClientRight - wrapper.ClientLeft;

            Assert.Equal(constraint, childWidth, 1);
        }

        [Fact]
        public async Task VerticalRl_OrthogonalAutoWidthChild_MinWidthFloorsTheShrunkWidth()
        {
            // GetFitContentWidth has no notion of the child's own min-width, so LayoutVerticalBlockChildren
            // floats the shrunk result back up against it explicitly - a min-width wider than the content
            // itself ("Hi") must still win, per CSS 2.1 SS10.4 (min-width overrides a smaller computed width).
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 400px">
                  <div id="ortho" style="writing-mode: horizontal-tb; min-width: 150px">Hi</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var ortho = LayoutHarness.FindById(root, "ortho");
            Assert.NotNull(wrapper);
            Assert.NotNull(ortho);

            var childWidth = ortho!.ActualRight - ortho.Location.X;

            // 150px * 0.75pt/px (Length.PointsPerPx) = 112.5pt - the min-width floor, well above "Hi"'s
            // own shrink-to-fit content width and well below the wrapper's full 300pt (400px) client width.
            Assert.Equal(112.5, childWidth, 1);
        }

        [Fact]
        public async Task VerticalRl_OrthogonalAutoWidthChild_NeverShrinksBelowItsOwnMinContent()
        {
            // The other floor CSS Writing Modes 4 SS4.3's own formula requires - min(max-content,
            // max(min-content, constraint)) - is the ALGORITHMIC min-content (the longest unbreakable
            // run), independent of any CSS min-width. A constraint narrower than a single long,
            // unbreakable word must not squeeze the child below that word's own width - the same floor
            // CssLayoutEngine.GetMinContentWidth already provides for position:absolute shrink-to-fit.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 20px">
                  <div id="ortho" style="writing-mode: horizontal-tb">Supercalifragilisticexpialidocious</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var ortho = LayoutHarness.FindById(root, "ortho");
            Assert.NotNull(wrapper);
            Assert.NotNull(ortho);

            var childWidth = ortho!.ActualRight - ortho.Location.X;
            var constraint = wrapper!.ClientRight - wrapper.ClientLeft;

            // The 20px (15pt) wrapper is far narrower than the word needs - the child's own min-content
            // (this one unbreakable word) must win over that narrow constraint.
            Assert.True(constraint < 20, $"expected a narrow constraint to set up the test, got {constraint}pt");
            Assert.True(childWidth > constraint,
                $"expected the orthogonal child's own min-content to floor its width above the {constraint}pt constraint, got {childWidth}pt");
        }

        [Fact]
        public async Task VerticalRl_AutoWidthWrapper_OrthogonalAutoWidthChild_BothShrinkToTheContent()
        {
            // The shrink-to-fit constraint this fix reuses (the child's own pre-correction stretch value)
            // is itself derived from the vertical wrapper's own tentative, not-yet-shrunk width when the
            // wrapper's own `width` is auto too - exercising that chain end to end: an auto-width wrapper
            // holding a single short auto-width orthogonal child should have both shrink down to the
            // content ("Hi"), not have the child's shrink-to-fit constraint stick at the wrapper's own
            // large, page-width tentative value.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <div id="ortho" style="writing-mode: horizontal-tb">Hi</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var ortho = LayoutHarness.FindById(root, "ortho");
            Assert.NotNull(wrapper);
            Assert.NotNull(ortho);

            var wrapperWidth = wrapper!.ClientRight - wrapper.ClientLeft;
            var childWidth = ortho!.ActualRight - ortho.Location.X;

            Assert.True(wrapperWidth < 100,
                $"expected the auto-width wrapper to shrink to its single short child's content, got {wrapperWidth}pt wide");
            Assert.Equal(wrapperWidth, childWidth, 1);
        }

        [Fact]
        public async Task VerticalRl_AutoHeight_WrapLimitIsPositionIndependent_NotSelfLimitingWhenFarDownThePage()
        {
            // clientTop is document-continuous (grows across page boundaries), not page-relative - a wrap
            // limit computed as "PageSize.Height - clientTop" collapses to ~0 for content far enough down
            // the document, forcing every word onto its own column. Precede the vertical box with enough
            // content to push it well past where a page-relative-but-not-page-aware fallback would break.
            var filler = string.Join("", Enumerable.Range(0, 30).Select(i => $"<p>Filler paragraph {i}.</p>"));
            var html = LayoutHarness.Wrap($"""
                {filler}
                <div id="el" style="writing-mode: vertical-rl; width: 200px">Alpha Beta Gamma Delta</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 300);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count >= 4);

            // If the wrap limit had collapsed to ~1pt, every word would land in its own column (as many
            // columns as words). A sane, position-independent wrap limit keeps at least two words together
            // on one column for four short words in a 200px-wide box.
            var distinctColumns = words.Select(w => System.Math.Round(w.Left)).Distinct().Count();
            Assert.True(distinctColumns < words.Count,
                $"expected some words to share a column; got {distinctColumns} distinct columns for {words.Count} words (wrap limit likely collapsed)");
        }

        [Fact]
        public async Task VerticalRl_InlineImage_MeasuredWithoutCrashing()
        {
            // NaturalWordSize's image/leader branch reads Width/Height as-is (matching how
            // MeasureWordsSize itself sizes an image word, from intrinsic dimensions rather than text
            // shaping) - exercise it directly so a future change to that branch can't silently break
            // without a test noticing, even though full image-in-vertical-text support is out of scope.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">
                  before <img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC" width="10" height="10"> after
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Contains(words, w => w.IsImage);
        }

        [Fact]
        public async Task VerticalRl_NestedInsideFlex_RepeatedLayoutPassesDoNotCorruptWordGeometry()
        {
            // A flex ancestor's own sizing runs a provisional content-measurement pass before its final
            // one, so a flex-nested vertical box's CreateVerticalLineBoxes call is not guaranteed to run
            // only once. word.Width/Height get overwritten with the word's *physical* (rotated) footprint
            // once placed - a naive re-entry that trusted them as still-natural on a second pass would
            // compound the rotation and collapse every word onto the same, wrong position.
            var html = LayoutHarness.Wrap("""
                <div style="display: flex">
                  <div id="el" style="writing-mode: vertical-rl; width: 220px; height: 300px">Hello vertical world this is a test</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count >= 2);

            // No two words collapsed onto the same position, and every word's rect is a real, sane size
            // (not the vanishing/ballooning footprint repeated rotation would produce).
            for (var i = 0; i < words.Count; i++)
            {
                Assert.True(words[i].Width > 0 && words[i].Width < 300, $"word {i} ('{words[i].Text}') has an implausible width {words[i].Width}");
                Assert.True(words[i].Height > 0 && words[i].Height < 300, $"word {i} ('{words[i].Text}') has an implausible height {words[i].Height}");

                for (var j = i + 1; j < words.Count; j++)
                {
                    Assert.False(words[i].Left == words[j].Left && words[i].Top == words[j].Top,
                        $"words {i} ('{words[i].Text}') and {j} ('{words[j].Text}') landed on the exact same position");
                }
            }
        }

        [Fact]
        public async Task VerticalRl_LetterSpacing_WidensTheWordsInlineExtentAccordingly()
        {
            var narrowHtml = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">Hello</div>
                """);
            var spacedHtml = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px; letter-spacing: 4px">Hello</div>
                """);

            var (narrowRoot, _) = await LayoutHarness.LayoutAsync(narrowHtml);
            var (spacedRoot, _) = await LayoutHarness.LayoutAsync(spacedHtml);
            var narrowWord = LayoutHarness.FindById(narrowRoot, "el")!.LineBoxes[0].Words.First(w => !w.IsLineBreak);
            var spacedWord = LayoutHarness.FindById(spacedRoot, "el")!.LineBoxes[0].Words.First(w => !w.IsLineBreak);

            // The word's inline-axis footprint (physical Height, for vertical-rl) grows with letter-spacing.
            Assert.True(spacedWord.Height > narrowWord.Height,
                $"letter-spacing should widen the word's inline extent (physical Height): narrow={narrowWord.Height}, spaced={spacedWord.Height}");
        }

        [Fact]
        public async Task VerticalRl_SingleLine_WordsStackTopToBottomInOneColumnNearTheRightEdge()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">AB CD</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.True(el!.LineBoxes.Count >= 1);

            var words = el.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).OrderBy(w => w.Top).ToList();
            Assert.True(words.Count >= 2, "expected at least two words ('AB', 'CD')");

            var first = words[0];
            var second = words[1];

            // Both words fit on one line (300pt is plenty for a line of text at default font size), so
            // they share the same column: same physical X extent, second word further down (higher Top).
            Assert.Equal(first.Left, second.Left, 1);
            Assert.True(second.Top > first.Top, "second word should sit below the first within the column");

            // block-start for vertical-rl is the right edge: the (only) column sits at the box's own
            // client-right edge, not the left.
            Assert.True(first.Left + first.Width <= el.ClientRight + 0.5, "column must not exceed the box's right edge");
            AssertColumnInRightHalf(first.Left, el, "the one column should sit in the right half of the box (block-start = right edge under vertical-rl)");
        }

        /// <summary>
        /// Shared by every test asserting a vertical-rl column's block-start position: under vertical-rl
        /// the block axis grows from the box's own right edge leftward, so a column at (or near) block-start
        /// sits in the right half of the box's own client area.
        /// </summary>
        private static void AssertColumnInRightHalf(double columnLeft, CssBox box, string because) =>
            Assert.True(columnLeft > box.ClientLeft + (box.ClientRight - box.ClientLeft) / 2, because);

        [Fact]
        public async Task VerticalRl_Wrapping_SecondColumnSitsToTheLeftOfTheFirst()
        {
            // A very short height forces a wrap after the first word, producing a second column.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 20px">Alpha Beta</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.True(el!.LineBoxes.Count >= 2, "a 20pt-tall box should force at least two columns for two words");

            var words = el.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var firstColumnWord = words.First();
            var secondColumnWord = words.First(w => w.Left < firstColumnWord.Left - 1);

            // block axis grows from the right edge leftward under vertical-rl.
            Assert.True(secondColumnWord.Left < firstColumnWord.Left);
        }

        [Fact]
        public async Task VerticalRl_ForcedLineBreak_StartsANewColumnEvenWithRoomToSpare()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">One<br>Two</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            // "One" and "Two" both fit comfortably within 300pt, but the <br> forces a new column anyway.
            Assert.True(el!.LineBoxes.Count >= 2, "a forced line break must start a new column regardless of remaining space");

            var words = el.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var one = words.First(w => w.Text == "One");
            var two = words.First(w => w.Text == "Two");

            Assert.True(two.Left < one.Left, "the column after the forced break sits further along the block axis");
            // The second column starts fresh at the inline axis's own start, not partway down.
            Assert.Equal(el.ClientTop, two.Top, 1);
        }

        [Fact]
        public async Task VerticalLr_BlockAxisGrowsFromLeftEdgeRightward()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-lr; width: 200px; height: 20px">Alpha Beta</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.True(el!.LineBoxes.Count >= 2);

            var words = el.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var firstColumnWord = words.First();
            var secondColumnWord = words.First(w => w.Left > firstColumnWord.Left + 1);

            Assert.True(secondColumnWord.Left > firstColumnWord.Left);
            // The first column should sit at the box's own left edge under vertical-lr.
            Assert.True(firstColumnWord.Left <= el.ClientLeft + 1);
        }

        [Fact]
        public async Task VerticalRl_AutoHeight_ShrinksToTheContentsInlineExtent()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px">Hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            var contentHeight = el!.ActualBottom - el.Location.Y;

            // Should shrink to roughly one line's worth of text, nowhere near a full page.
            Assert.True(contentHeight > 0);
            Assert.True(contentHeight < 100, $"expected auto height to shrink to content (~one line), got {contentHeight}");
        }

        [Fact]
        public async Task VerticalRl_AutoWidth_ShrinksToTheContentsBlockExtent_NotTheFullAvailableWidth()
        {
            // No explicit width, and a short explicit height forces two columns - issue #761. Block-start
            // for vertical-rl is the right edge, so the box's own Location.X (not ActualRight) is what
            // moves to shrink it.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; height: 20px">Alpha Beta</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(el.LineBoxes.Count >= 2, "a 20pt-tall box should force at least two columns for two words");

            var contentWidth = el.ActualRight - el.Location.X;
            // Two columns of default-size text, nowhere near the ~555pt available content width a
            // fill-available box would take.
            Assert.True(contentWidth is > 0 and < 100,
                $"expected auto width to shrink to content (~two columns), got {contentWidth}");

            // Every word's own physical rect must still fall inside the shrunk box - a regression that
            // moved the box without correctly re-anchoring word positions would pass the aggregate-width
            // assertion above while actually clipping or overhanging content.
            foreach (var word in words)
            {
                Assert.True(word.Left >= el.Location.X - 0.5 && word.Left + word.Width <= el.ActualRight + 0.5,
                    $"word '{word.Text}' (Left={word.Left}, Width={word.Width}) falls outside the shrunk box [{el.Location.X}, {el.ActualRight}]");
            }
            // Block-start for vertical-rl is the right edge, so the first column should sit flush there.
            Assert.Equal(el.ActualRight, words[0].Left + words[0].Width, 1);
        }

        [Fact]
        public async Task VerticalLr_AutoWidth_ShrinksToTheContentsBlockExtent()
        {
            // Same as the vertical-rl case, but block-start is the left edge for vertical-lr, so
            // ActualRight (not Location.X) is what moves.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-lr; height: 20px">Alpha Beta</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(el.LineBoxes.Count >= 2, "a 20pt-tall box should force at least two columns for two words");

            var contentWidth = el.ActualRight - el.Location.X;
            Assert.True(contentWidth is > 0 and < 100,
                $"expected auto width to shrink to content (~two columns), got {contentWidth}");

            foreach (var word in words)
            {
                Assert.True(word.Left >= el.Location.X - 0.5 && word.Left + word.Width <= el.ActualRight + 0.5,
                    $"word '{word.Text}' (Left={word.Left}, Width={word.Width}) falls outside the shrunk box [{el.Location.X}, {el.ActualRight}]");
            }
            // Block-start for vertical-lr is the left edge, so the first column should sit flush there.
            Assert.Equal(el.Location.X, words[0].Left, 1);
        }

        [Fact]
        public async Task VerticalRl_AutoWidth_NoContent_ShrinksToZeroRatherThanFillingAvailableSpace()
        {
            // The words.Count == 0 early-return branch of CreateVerticalLineBoxes - a box with no text
            // content at all still needs its auto width settled rather than left at the fill-available
            // default GetBoxWidth assigned before this method ran.
            var html = LayoutHarness.Wrap("""<div id="el" style="writing-mode: vertical-rl; height: 20px"></div>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var contentWidth = el!.ActualRight - el.Location.X;
            Assert.True(contentWidth is >= 0 and < 10,
                $"expected an empty auto-width vertical box to shrink to ~zero content width, got {contentWidth}");
        }

        [Fact]
        public async Task HorizontalTb_UnaffectedByTheNewVerticalDispatchBranch()
        {
            var html = LayoutHarness.Wrap("""<div id="el" style="width: 200px">plain horizontal text</div>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.True(el!.LineBoxes.Count >= 1);
            var word = el.LineBoxes[0].Words.First();
            // Ordinary horizontal placement: first word starts at the box's own left edge.
            Assert.Equal(el.ClientLeft, word.Left, 1);
        }

        [Fact]
        public async Task VerticalRl_PaintsEachWordRotated_PushTransformDrawStringPopTransformInOrder()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">Hi</div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintBox(container, el!, recording);

            // Exactly one PushTransform/DrawString/PopTransform triplet for the single word "Hi", in order
            // - not a bare DrawString the way horizontal text paints.
            var relevant = recording.Log
                .Where(op => op.Kind is PaintOpKind.PushTransform or PaintOpKind.DrawString or PaintOpKind.PopTransform)
                .ToList();

            Assert.Equal(3, relevant.Count);
            Assert.Equal(PaintOpKind.PushTransform, relevant[0].Kind);
            Assert.Equal(PaintOpKind.DrawString, relevant[1].Kind);
            Assert.Equal("Hi", relevant[1].Text);
            Assert.Equal(PaintOpKind.PopTransform, relevant[2].Kind);

            // The word is drawn at its own natural (untranslated) origin - the pushed matrix carries the
            // rotation/position, not the DrawString point.
            Assert.Equal(0, relevant[1].Bounds.X, 3);

            // The pushed matrix is a pure 90°-clockwise rotation: (x,y) -> (-y,x), i.e. M11=0,M12=1,M21=-1,M22=0.
            var matrix = relevant[0].Matrix;
            Assert.NotNull(matrix);
            Assert.Equal(0, matrix!.Value.M11, 6);
            Assert.Equal(1, matrix.Value.M12, 6);
            Assert.Equal(-1, matrix.Value.M21, 6);
            Assert.Equal(0, matrix.Value.M22, 6);
        }

        [Fact]
        public async Task HorizontalTb_PaintsWordsWithoutAnyTransform()
        {
            var html = LayoutHarness.Wrap("""<div id="el" style="width: 200px">Hi</div>""");

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintBox(container, el!, recording);

            Assert.DoesNotContain(recording.Log, op => op.Kind is PaintOpKind.PushTransform or PaintOpKind.PopTransform);
            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.DrawString && op.Text == "Hi");
        }

        [Fact]
        public async Task VerticalRl_NestedInlineBlock_IsFlattenedInsteadOfTreatedAsAnAtomicUnit()
        {
            // Documents a known gap (see .claude/accepted-gaps/no-vertical-writing-mode-layout.md,
            // issue #771): MeasureAndCollectWordsInDocumentOrder walks every descendant box with no
            // formatting-context check, so an inline-block child's own words get flattened straight into
            // the parent's flat word list instead of being measured/placed as one atomic unit (the way
            // FlowBox's ApplyAtomicInlineVerticalInsets does for the horizontal engine). The inline-block
            // box itself never gets positioned, so its own border/padding/background never paint.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 300px; height: 400px">before <span id="ib" style="display: inline-block; border: 1px solid red; padding: 4px;">nested block</span> after</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            var ib = LayoutHarness.FindById(root, "ib");
            Assert.NotNull(el);
            Assert.NotNull(ib);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).Select(w => w.Text).ToList();
            // The inline-block's own text ("nested", "block") shows up as ordinary flat words alongside
            // the surrounding plain text ("before", "after") - not aggregated into one atomic placement.
            Assert.Equal(["before", "nested", "block", "after"], words);

            // The inline-block box itself was never positioned by the flattening walk (no analog of
            // FlowBox's atomic-inline placement/inset bookkeeping exists in the vertical engine yet).
            Assert.Equal(0, ib!.Location.X);
            Assert.Equal(0, ib.Location.Y);
        }

        [Fact]
        public async Task HorizontalAncestor_WithVerticalRlBlockChild_BothFlowCorrectlyInTheirOwnWritingMode()
        {
            // The composition claim behind WritingModeFrame's design: a box's own content-box bounds are
            // already fully resolved, true-physical values by the time its own content phase runs, so a
            // child never needs to know or undo an ancestor's writing mode. Here an ordinary horizontal-tb
            // document (normal top-to-bottom block flow) contains a vertical-rl block-level child - the
            // outer box places the inner one as an ordinary physical block (unaffected by the child's own
            // writing mode), and the inner box gets its own real vertical line flow, independently.
            var html = LayoutHarness.Wrap("""
                <p id="before">Leading horizontal paragraph.</p>
                <div id="vert" style="writing-mode: vertical-rl; width: 200px; height: 300px">AB CD</div>
                <p id="after">Trailing horizontal paragraph.</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var before = LayoutHarness.FindById(root, "before");
            var vert = LayoutHarness.FindById(root, "vert");
            var after = LayoutHarness.FindById(root, "after");
            Assert.NotNull(before);
            Assert.NotNull(vert);
            Assert.NotNull(after);

            // Ordinary top-to-bottom physical block stacking around the vertical child, unaffected by its
            // writing mode.
            Assert.True(vert!.Location.Y >= before!.ActualBottom, "the vertical box should sit below the leading paragraph");
            Assert.True(after!.Location.Y >= vert.ActualBottom, "the trailing paragraph should sit below the vertical box");

            // The vertical child's own content still gets real vertical line flow: words stack along the
            // block axis (right-to-left), not ordinary horizontal placement.
            var words = vert.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).OrderBy(w => w.Top).ToList();
            Assert.True(words.Count >= 2, "expected at least two words ('AB', 'CD')");
            Assert.Equal(words[0].Left, words[1].Left, 1);
            AssertColumnInRightHalf(words[0].Left, vert, "the vertical child's own column should still sit in the right half of its own box");
        }

        [Fact]
        public async Task VerticalRl_NestedInsideAnotherVerticalRlBlockChild_BothLayersFlowCorrectly()
        {
            // Two levels of vertical-rl nesting (an inline-only vertical box as a block-level child of
            // another vertical-rl ancestor that itself has block children, so the outer box does NOT go
            // through CreateVerticalLineBoxes itself - only the inner, inline-only one does). Verifies the
            // per-box independent-dispatch model composes for same-writing-mode nesting too, not just the
            // mixed horizontal/vertical case above.
            var html = LayoutHarness.Wrap("""
                <div id="outer" style="writing-mode: vertical-rl">
                  <div id="inner" style="width: 200px; height: 300px">Alpha Beta</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var outer = LayoutHarness.FindById(root, "outer");
            var inner = LayoutHarness.FindById(root, "inner");
            Assert.NotNull(outer);
            Assert.NotNull(inner);

            // The inner box inherits vertical-rl and is itself inline-only, so it gets real vertical line
            // flow regardless of the outer box's own (block-children) dispatch.
            Assert.Equal(WritingMode.VerticalRl, inner!.WritingMode.Value);
            var words = inner.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count >= 2, "expected at least two words ('Alpha', 'Beta')");
            AssertColumnInRightHalf(words[0].Left, inner, "the inner box's own column should sit in the right half of its own box");

            // The outer box's own LayoutVerticalBlockChildren places its only child flush against the
            // outer box's own block-start (right, under vertical-rl) edge.
            Assert.Equal(outer!.ClientRight, inner.ActualRight, 1);
        }

        // ── Issue #768: hyphenation ─────────────────────────────────────────────

        [Fact]
        public async Task VerticalRl_HyphensAuto_LongWordHyphenatesAcrossColumnBoundary()
        {
            // lang must be on <html> (DocumentLanguage only reads it there, matching
            // AutoHyphenationIntegrationTests' own convention) - LayoutHarness.Wrap's <html> tag carries none.
            const string html = """
                <!DOCTYPE html><html lang="en"><head></head><body style="margin:0">
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 80px; hyphens: auto">antidisestablishmentarianism</div>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count > 1, "expected the word to be split into multiple fragments");
            Assert.Contains(words, w => w.Text!.EndsWith('-'));
            Assert.True(el.LineBoxes.Count >= 2, "a hyphenated split should straddle a column boundary");

            var prefix = words.First(w => w.Text!.EndsWith('-'));
            var suffix = words[words.IndexOf(prefix) + 1];
            var prefixColumn = el.LineBoxes.First(l => l.Words.Contains(prefix));
            var suffixColumn = el.LineBoxes.First(l => l.Words.Contains(suffix));
            Assert.NotSame(prefixColumn, suffixColumn);

            var candidates = HyphenationEngine.FindHyphenationPoints("antidisestablishmentarianism", "en");
            Assert.Contains(prefix.Text!.TrimEnd('-').Length, candidates);
        }

        [Fact]
        public async Task VerticalRl_SoftHyphen_IsABreakOpportunityAcrossColumns()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 60px; hyphens: manual">hyphen&shy;ation</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count > 1, "the soft hyphen should be an available break opportunity");
            Assert.Contains(words, w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task VerticalRl_HyphenateLimitChars_WordMinimumSuppressesHyphenation()
        {
            const string html = """
                <!DOCTYPE html><html lang="en"><head></head><body style="margin:0">
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 80px; hyphens: auto; hyphenate-limit-chars: 50">antidisestablishmentarianism</div>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Single(words);
            Assert.Equal("antidisestablishmentarianism", words[0].Text);
        }

        [Fact]
        public async Task VerticalRl_HyphenateLimitLines_One_NeverProducesTwoConsecutiveHyphenatedColumns()
        {
            const string html = """
                <!DOCTYPE html><html lang="en"><head></head><body style="margin:0">
                <div id="el" style="writing-mode: vertical-rl; width: 400px; height: 40px; hyphens: auto; hyphenate-limit-lines: 1">
                antidisestablishmentarianism antidisestablishmentarianism antidisestablishmentarianism
                </div>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);
            Assert.True(el!.LineBoxes.Count >= 2, "expected multiple columns for this much text at 40pt height");

            var endsInHyphen = el.LineBoxes
                .Select(l => l.Words.LastOrDefault(w => !w.IsSpaces && !w.IsLineBreak))
                .Select(w => w?.Text?.EndsWith('-') == true)
                .ToList();

            for (var i = 0; i < endsInHyphen.Count - 1; i++)
            {
                Assert.False(endsInHyphen[i] && endsInHyphen[i + 1],
                    "hyphenate-limit-lines: 1 must never allow two consecutive hyphenated columns");
            }
        }

        [Fact]
        public async Task VerticalRl_HyphenateCharacter_CustomString_AppearsAtTheSplit()
        {
            const string html = """
                <!DOCTYPE html><html lang="en"><head></head><body style="margin:0">
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 80px; hyphens: auto; hyphenate-character: '~'">antidisestablishmentarianism</div>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count > 1);
            Assert.Contains(words, w => w.Text!.EndsWith('~'));
            Assert.DoesNotContain(words, w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task VerticalRl_HyphensAuto_NoLanguage_DoesNotHyphenate_Regression()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 80px; hyphens: auto">antidisestablishmentarianism</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            Assert.NotNull(el);

            var words = el!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Single(words);
            Assert.Equal("antidisestablishmentarianism", words[0].Text);
        }

        // ── Issue #768: position: absolute/fixed inside vertical inline content ──

        [Fact]
        public async Task VerticalRl_InlineNestedAbsolutePositionedSpan_DoesNotReserveColumnSpaceAndPositionsAgainstAncestor()
        {
            // position:absolute blockifies (CSS2.1 §9.7, DomParser.BlockifyPositionedBox) - a plain <span>
            // gets exactly the same full left/top resolution a <div> would, so DomParser's own
            // anonymous-block correction splits it out of "Before"/"After" the same way it splits out a
            // float; el itself ends up block-children-dispatched (LineBoxes.Count == 0), not inline-only,
            // so words are gathered from every descendant rather than from el's own LineBoxes. An explicit
            // width sidesteps a separate, pre-existing bug (CreateVerticalLineBoxes's own auto-width-shrink
            // logic, from #761, corrupts an absolutely-positioned descendant's own Location.X when its
            // width is auto - unrelated to #768, not fixed here).
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; position: relative; width: 200px; height: 200px">
                Before <span id="abs" style="position: absolute; left: 5pt; top: 5pt; width: 20pt">X</span> After
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            var abs = LayoutHarness.FindById(root, "abs");
            Assert.NotNull(el);
            Assert.NotNull(abs);

            // "Before"/"After" wrap as if the absolutely-positioned span's own content wasn't there at all -
            // and its own "X" never leaks into the ordinary word stream (not an exact count: leading/
            // trailing whitespace from the raw-string fixture can add its own space-only fragments).
            var ordinaryWords = LayoutHarness.Descendants(el!).SelectMany(d => d.LineBoxes).SelectMany(l => l.Words)
                .Where(w => !w.IsLineBreak && !w.IsSpaces).ToList();
            Assert.Contains(ordinaryWords, w => w.Text == "Before");
            Assert.Contains(ordinaryWords, w => w.Text == "After");
            Assert.DoesNotContain(ordinaryWords, w => w.OwnerBox == abs);

            // Positioned against el's own padding-box (the nearest positioned ancestor), not wherever it
            // would have sat inline.
            Assert.Equal(el!.ClientLeft + 5, abs!.Location.X, 1);
            Assert.Equal(el.ClientTop + 5, abs.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_InlineNestedFixedPositionedSpan_PositionsAgainstThePage()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 200px">
                Before <span id="fixed" style="position: fixed; left: 10pt; top: 10pt; width: 20pt">X</span> After
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var fixedBox = LayoutHarness.FindById(root, "fixed");
            Assert.NotNull(fixedBox);

            Assert.Equal(10, fixedBox!.Location.X, 1);
            Assert.Equal(10, fixedBox.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_InlineNestedAbsolutePositionedDiv_PlacesItselfAsBlockBoxCase_FullyPositioned()
        {
            // Same shape as the <span> case above, using an already-block-level element - both reach the
            // same fully-resolved positioning path since position:absolute blockifies either way.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; position: relative; width: 200px; height: 200px">
                Before <div id="abs" style="position: absolute; left: 15pt; top: 20pt; width: 10pt; height: 10pt">X</div> After
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");
            var abs = LayoutHarness.FindById(root, "abs");
            Assert.NotNull(el);
            Assert.NotNull(abs);

            Assert.Equal(el!.ClientLeft + 15, abs!.Location.X, 1);
            Assert.Equal(el.ClientTop + 20, abs.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_InlineNestedAbsolutePositionedDescendant_DoesNotCorruptSurroundingColumnLayout()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200px; height: 300px">
                <p id="p1" style="width: 40pt">First. <span style="position: absolute">Positioned.</span></p>
                <p id="p2" style="width: 40pt">Second.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p1 = LayoutHarness.FindById(root, "p1");
            var p2 = LayoutHarness.FindById(root, "p2");
            Assert.NotNull(p1);
            Assert.NotNull(p2);

            // The positioned descendant contributes nothing to the block-axis cursor, so the two ordinary
            // paragraphs still stack immediately adjacent to each other (40pt apart), the same assertion
            // shape as VerticalRl_RunningPositionedAndOutOfFlowChildren_AreSkippedFromStackingButDoNotCrash.
            Assert.Equal(40, p1!.Location.X - p2!.Location.X, 1);
        }

        // ── Issue #768: real float wrap-around for vertical column flow ─────────

        [Fact]
        public async Task VerticalRl_FloatRight_NarrowsEarlyColumnsAndNoWordOverlapsIt()
        {
            await AssertFloatAvoidance(
                floated: """
                    <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                      <div id="floatBox" style="float: right; width: 30pt; height: 60pt"></div>
                      <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """,
                unfloated: """
                    <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                      <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """);
        }

        [Fact]
        public async Task VerticalRl_FloatLeft_NarrowsLaterColumnsAndNoWordOverlapsIt()
        {
            // "after" has an explicit width (200pt) giving it a fixed, font-independent physical span of
            // [wrapper.ClientRight-200, wrapper.ClientRight] - but its actual columns are only created as
            // content needs them, so how far left they actually reach (and whether they overflow past that
            // declared span) is column-pitch/word-height dependent, i.e. font-metric dependent. A narrow
            // float sized to just barely reach into that overflow zone on one font's metrics can miss
            // entirely on another's (this previously passed locally on Windows but failed in CI on
            // Ubuntu/macOS, whose default fallback font produces different metrics). Two independent
            // safety margins remove that dependency: the float is widened/heightened to overlap most of
            // "after"'s own always-present, font-independent [120,320] span rather than relying on
            // overflow past it, and the word count is 6x'd so even a compact font's fewer, wider columns
            // still walk deep into that span.
            const string words = """
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon Phi Chi Psi Omega Digamma Koppa Sampi Heta San Wau.
                """;
            await AssertFloatAvoidance(
                floated: $"""
                    <div id="wrapper" style="writing-mode: vertical-rl; width: 400px">
                      <div id="floatBox" style="float: left; width: 200pt; height: 60pt"></div>
                      <p id="after" style="width: 200pt; height: 60pt">{words}</p>
                    </div>
                    """,
                unfloated: $"""
                    <div id="wrapper" style="writing-mode: vertical-rl; width: 400px">
                      <p id="after" style="width: 200pt; height: 60pt">{words}</p>
                    </div>
                    """);
        }

        [Fact]
        public async Task VerticalLr_FloatLeft_NarrowsEarlyColumnsAndNoWordOverlapsIt()
        {
            await AssertFloatAvoidance(
                floated: """
                    <div id="wrapper" style="writing-mode: vertical-lr; width: 300px">
                      <div id="floatBox" style="float: left; width: 30pt; height: 60pt"></div>
                      <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """,
                unfloated: """
                    <div id="wrapper" style="writing-mode: vertical-lr; width: 300px">
                      <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """);
        }

        [Fact]
        public async Task VerticalRl_Rtl_FloatRight_NarrowsEarlyColumnsAndNoWordOverlapsIt()
        {
            // Explicit height on "after" - an auto-height inline-only vertical box under direction:rtl has
            // a separate, pre-existing bug (words anchor against the provisional page-height bottom edge,
            // not the final settled one, landing outside the box - unrelated to #768, not fixed here);
            // sidestepped here since it isn't what this test is about.
            await AssertFloatAvoidance(
                floated: """
                    <div id="wrapper" style="writing-mode: vertical-rl; direction: rtl; width: 300px; height: 350pt">
                      <div id="floatBox" style="float: right; width: 30pt; height: 60pt"></div>
                      <p id="after" style="width: 200pt; height: 330pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """,
                unfloated: """
                    <div id="wrapper" style="writing-mode: vertical-rl; direction: rtl; width: 300px; height: 350pt">
                      <p id="after" style="width: 200pt; height: 330pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p>
                    </div>
                    """);
        }

        [Fact]
        public async Task VerticalRl_Float_TextBeforeItInSourceOrderIsUnaffected()
        {
            // A float only ever constrains content that comes at or after it in the flow (CSS2.1 §9.5.1) -
            // the anonymous block preceding it in the split tree must lay out identically with or without it.
            var withFloat = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                  <p id="before" style="width: 200pt">Alpha Beta Gamma Delta Epsilon.</p>
                  <div style="float: right; width: 30pt; height: 60pt"></div>
                </div>
                """);
            var withoutFloat = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                  <p id="before" style="width: 200pt">Alpha Beta Gamma Delta Epsilon.</p>
                </div>
                """);

            var (rootWith, _) = await LayoutHarness.LayoutAsync(withFloat);
            var (rootWithout, _) = await LayoutHarness.LayoutAsync(withoutFloat);
            var beforeWith = LayoutHarness.FindById(rootWith, "before");
            var beforeWithout = LayoutHarness.FindById(rootWithout, "before");
            Assert.NotNull(beforeWith);
            Assert.NotNull(beforeWithout);

            var wordsWith = beforeWith!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var wordsWithout = beforeWithout!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Equal(wordsWithout.Count, wordsWith.Count);

            for (var i = 0; i < wordsWith.Count; i++)
            {
                Assert.Equal(wordsWithout[i].Top, wordsWith[i].Top, 1);
                Assert.Equal(wordsWithout[i].Left, wordsWith[i].Left, 1);
            }
        }

        [Fact]
        public async Task VerticalRl_Float_AlreadyClearedByTheTimeContentStarts_HasNoEffect()
        {
            // A float whose own inline-axis (Y) span lies entirely before this content's own ClientTop -
            // "already passed" in the direction this content's columns grow into - must have zero effect,
            // even if its physical-X span happens to overlap where this content's own columns land. Guards
            // DomUtils.ScanForVerticalFloatConstraint's own perpendicular-axis relevance check: an earlier,
            // buggy version treated any X-overlapping float as a same-column constraint regardless of
            // whether its Y-span ever actually reached this content at all, clamping a negative "extent"
            // to zero (a spurious full block) instead of recognizing the float as irrelevant.
            var withFloat = LayoutHarness.Wrap("""
                <div style="float: left; width: 300px; height: 20pt; background: red"></div>
                <div style="height: 400pt"></div>
                <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                  <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon.</p>
                </div>
                """);
            var withoutFloat = LayoutHarness.Wrap("""
                <div style="height: 400pt"></div>
                <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                  <p id="after" style="width: 200pt">Alpha Beta Gamma Delta Epsilon.</p>
                </div>
                """);

            var (rootWith, _) = await LayoutHarness.LayoutAsync(withFloat);
            var (rootWithout, _) = await LayoutHarness.LayoutAsync(withoutFloat);
            var afterWith = LayoutHarness.FindById(rootWith, "after");
            var afterWithout = LayoutHarness.FindById(rootWithout, "after");
            Assert.NotNull(afterWith);
            Assert.NotNull(afterWithout);

            var wordsWith = afterWith!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var wordsWithout = afterWithout!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Equal(wordsWithout.Count, wordsWith.Count);

            for (var i = 0; i < wordsWith.Count; i++)
            {
                Assert.Equal(wordsWithout[i].Top, wordsWith[i].Top, 1);
                Assert.Equal(wordsWithout[i].Left, wordsWith[i].Left, 1);
            }
        }

        [Fact]
        public async Task VerticalRl_Float_RoutesThroughLayoutVerticalBlockChildren_NotWordStream()
        {
            // Guards the routing assumption the whole float-avoidance design depends on: DomParser's
            // anonymous-block correction always splits a floated sibling out of otherwise inline-only
            // content before layout runs, so "wrapper" itself never reaches CreateVerticalLineBoxes - each
            // text run around the float gets its own, independent CreateVerticalLineBoxes call instead.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl; width: 300px">
                  Some text.
                  <div id="floatBox" style="float: left; width: 20pt; height: 20pt"></div>
                  More text.
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var floatBox = LayoutHarness.FindById(root, "floatBox");
            Assert.NotNull(wrapper);
            Assert.NotNull(floatBox);

            // wrapper itself holds no LineBoxes of its own (block-children dispatch, not inline-only).
            Assert.Empty(wrapper!.LineBoxes);
            Assert.True(wrapper.Boxes.Count > 1, "the float should have been split out into its own sibling");
            Assert.True(floatBox!.IsFloated);
            Assert.NotEqual(default, floatBox.Location);
        }

        private static async Task AssertFloatAvoidance(string floated, string unfloated)
        {
            var (floatedRoot, _) = await LayoutHarness.LayoutAsync(floated);
            var (unfloatedRoot, _) = await LayoutHarness.LayoutAsync(unfloated);

            var floatBox = LayoutHarness.FindById(floatedRoot, "floatBox");
            var floatedAfter = LayoutHarness.FindById(floatedRoot, "after");
            var unfloatedAfter = LayoutHarness.FindById(unfloatedRoot, "after");
            Assert.NotNull(floatBox);
            Assert.NotNull(floatedAfter);
            Assert.NotNull(unfloatedAfter);

            var floatedWords = floatedAfter!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            var unfloatedWords = unfloatedAfter!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.Equal(unfloatedWords.Count, floatedWords.Count);

            // The float must have actually changed something - a no-op implementation would produce
            // byte-identical output versus the no-float baseline.
            var anyDifference = false;
            for (var i = 0; i < floatedWords.Count; i++)
            {
                if (System.Math.Abs(floatedWords[i].Top - unfloatedWords[i].Top) > 0.5
                    || System.Math.Abs(floatedWords[i].Left - unfloatedWords[i].Left) > 0.5)
                {
                    anyDifference = true;
                    break;
                }
            }
            Assert.True(anyDifference, "the float should have changed at least one word's position versus the no-float baseline");

            // No word's own painted rectangle overlaps the float's - except a column's own first word,
            // which (mirroring FlowBox's identical accepted behavior for an unavoidable overflow) is always
            // placed regardless of fit, to guarantee the column-stacking loop keeps making forward
            // progress even when a float leaves a column no usable room at all.
            foreach (var lineBox in floatedAfter.LineBoxes)
            {
                var columnWords = lineBox.Words.Where(w => !w.IsLineBreak).ToList();
                foreach (var word in columnWords.Skip(1))
                {
                    var overlapsX = word.Left < floatBox!.ActualRight + floatBox.ActualMarginRight
                                    && word.Left + word.Width > floatBox.Location.X - floatBox.ActualMarginLeft;
                    var overlapsY = word.Top < floatBox.ActualBottom + floatBox.ActualMarginBottom
                                     && word.Top + word.Height > floatBox.Location.Y - floatBox.ActualMarginTop;

                    Assert.False(overlapsX && overlapsY,
                        $"word '{word.Text}' at ({word.Left},{word.Top},{word.Width}x{word.Height}) overlaps the float");
                }
            }
        }
    }
}
