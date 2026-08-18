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
    /// End-to-end layout tests for real <c>vertical-rl</c>/<c>vertical-lr</c> line flow
    /// (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngine.CreateVerticalLineBoxes"/>), asserting actual
    /// <c>CssBox</c>/word geometry after layout - not just that layout completes - per this repo's testing
    /// conventions for layout-engine changes.
    /// </summary>
    public class VerticalWritingModeLayoutIntegrationTests
    {
        [Fact]
        public async Task VerticalRl_InheritedByBlockChildren_DoesNotMarkTheBlockAncestorMonolithic()
        {
            // writing-mode inherits, so every descendant of a vertical-rl ancestor reports the same
            // WritingMode.Value - including block-level ancestors that never go through
            // CreateVerticalLineBoxes themselves (their own children are block-level <p>s, not inline
            // content). IsUnresumableOrthogonalFlow must be scoped to boxes CssBox.LayoutContents actually
            // routes to CreateVerticalLineBoxes, or a whole multi-paragraph vertical document would be
            // wrongly treated as one indivisible unit, breaking ordinary multi-page pagination.
            var html = LayoutHarness.Wrap("""
                <div id="wrapper" style="writing-mode: vertical-rl">
                  <p id="p1">First paragraph.</p>
                  <p>Second paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var wrapper = LayoutHarness.FindById(root, "wrapper");
            var firstParagraph = LayoutHarness.FindById(root, "p1");
            Assert.NotNull(wrapper);
            Assert.NotNull(firstParagraph);

            Assert.Equal(WritingMode.VerticalRl, wrapper!.WritingMode.Value);
            Assert.Equal(WritingMode.VerticalRl, firstParagraph!.WritingMode.Value);
            // The wrapper itself has block (<p>) children, not inline content - it does not go through
            // CreateVerticalLineBoxes, so it must not be reported as unresumable-orthogonal-flow even
            // though it inherits the same writing-mode every descendant does.
            Assert.False(MonolithicContent.IsUnresumableOrthogonalFlow(wrapper));

            // A <p> holding only text IS itself inline-only, so it genuinely goes through
            // CreateVerticalLineBoxes and is correctly reported as unresumable-orthogonal-flow.
            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(firstParagraph));
        }

        [Fact]
        public async Task VerticalRl_MultiParagraphDocument_PaginatesNormallyAcrossMultiplePages()
        {
            var paragraphs = string.Join("", Enumerable.Range(0, 40).Select(i => $"<p>Paragraph number {i} with some real text content in it.</p>"));
            var html = LayoutHarness.Wrap($"""<div style="writing-mode: vertical-rl">{paragraphs}</div>""");

            // A short page forces this much content across several pages if pagination works normally.
            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 300);

            Assert.NotNull(container.FragmentTree);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                $"40 paragraphs on 300pt pages should span multiple pages if pagination isn't broken by a wrongly-monolithic ancestor; got {container.FragmentTree.Fragmentainers.Count} page(s).");
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
                <div style="writing-mode: vertical-rl">
                  <div id="inner" style="width: 200px; height: 300px">Alpha Beta</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var inner = LayoutHarness.FindById(root, "inner");
            Assert.NotNull(inner);

            // The inner box inherits vertical-rl and is itself inline-only, so it gets real vertical line
            // flow regardless of the outer box's own (block-children) dispatch.
            Assert.Equal(WritingMode.VerticalRl, inner!.WritingMode.Value);
            var words = inner.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();
            Assert.True(words.Count >= 2, "expected at least two words ('Alpha', 'Beta')");
            AssertColumnInRightHalf(words[0].Left, inner, "the inner box's own column should sit in the right half of its own box");
        }
    }
}
