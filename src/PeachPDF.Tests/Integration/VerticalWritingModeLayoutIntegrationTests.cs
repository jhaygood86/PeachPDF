using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;
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
        public async Task VerticalRl_MarginsBetweenBlockChildren_AreSummedNotCollapsed()
        {
            // Deliberate scope boundary (issue #760): margins between block-axis-stacked children are a
            // cheap sum, not real CSS 2.1 SS8.3.1 adjoining-margin collapse - pinned down explicitly so a
            // future change to real collapsing shows up as an intentional test update, not a silent
            // regression.
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
            // edge is the sum of a's own left margin and b's own right margin (10 + 10 = 20), not their
            // max (10, what real collapsing would give).
            var gap = a!.Location.X - b!.ActualRight;
            Assert.Equal(20, gap, 1);
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
    }
}
