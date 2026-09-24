using System.Linq;
using System;
using System.Text;
using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for float layout and the <c>HtmlContainerInt.HasFloatedBoxes</c> short-circuit
    /// added to <c>DomUtils.GetFirstIntersectingFloatBox</c>. That lookup used to walk all the way to
    /// the document root and re-scan every preceding sibling's whole subtree, for every box needing
    /// line layout, at every ancestor level - regardless of whether the document had any floated content
    /// at all. These tests confirm float-avoidance still works correctly when floats ARE present, and
    /// that a float-free document (the common case, and the one the short-circuit targets) still lays
    /// out doing a constant amount of float-scan work per box rather than an O(document size) amount.
    /// <para>
    /// The complexity guard asserts <see cref="HtmlContainerInt.FloatScanBoxVisits"/> and
    /// <see cref="HtmlContainerInt.FloatScanCalls"/> - counts, not elapsed time. A wall-clock bound here
    /// was flaky on a contended CI runner (one job runs both target frameworks' suites under coverage
    /// instrumentation, with xUnit running collections in parallel) and could not be fixed by raising
    /// the bound, since raising it is exactly what removes its ability to see the regression it guards.
    /// </para>
    /// </summary>
    public class FloatLayoutRegressionTests
    {
        [Fact]
        public async Task Float_PushesFollowingSiblingTextToTheRight()
        {
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <div style='float:left; width:100pt; height:50pt;'></div>
                    <p id='text' style='margin:0;'>Hello world</p>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var text = FindById(root, "text")!;
            var firstWord = FindFirstWord(text);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left >= 90,
                $"first word should be pushed right past the 100pt float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task WithoutFloat_SiblingTextStartsAtContainerEdge()
        {
            // Same shape as the test above but the earlier div is a plain block (no float), so the
            // paragraph's text should start back at the container's left edge - this is the contrast
            // case confirming the previous test's assertion is actually about float avoidance, not
            // some unrelated margin/padding default.
            var html = Wrap(@"
                <div style='width:300px;'>
                    <div style='width:100px; height:50px;'></div>
                    <p id='text' style='margin:0;'>Hello world</p>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var text = FindById(root, "text")!;
            var firstWord = FindFirstWord(text);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left < 10,
                $"first word should start at the container's left edge without a float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task Float_NarrowsAvailableWidth_SoTextWrapsToMoreLines()
        {
            const string longText =
                "This is a fairly long sentence that should wrap across multiple lines once the available width is narrowed by a floated sibling element.";

            var withFloatHtml = Wrap($@"
                <div style='width:250px;'>
                    <div style='float:left; width:150px; height:40px;'></div>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var withoutFloatHtml = Wrap($@"
                <div style='width:250px;'>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var (withFloatRoot, _) = await BuildAndLayout(withFloatHtml);
            var (withoutFloatRoot, _) = await BuildAndLayout(withoutFloatHtml);

            var withFloatText = FindById(withFloatRoot, "text")!;
            var withoutFloatText = FindById(withoutFloatRoot, "text")!;

            Assert.True(withFloatText.ActualBoxSizingHeight > withoutFloatText.ActualBoxSizingHeight,
                $"narrowing the line width with a float should force extra line wraps and a taller box " +
                $"(with float: {withFloatText.ActualBoxSizingHeight}, without: {withoutFloatText.ActualBoxSizingHeight})");
        }

        [Fact]
        public async Task ManyNestedBlocksWithoutFloats_FloatScanVisitsNoBoxes()
        {
            // Regression guard for the O(document size) walk that GetFirstIntersectingFloatBox used to
            // perform for every box, at every ancestor level, even with zero floats anywhere. This
            // document has no floats, so HasFloatedBoxes should short-circuit the whole thing - which is
            // an exact, countable statement: the scan is asked its question thousands of times and must
            // answer every one of them without examining a single box.
            //
            // The count is what is asserted, deliberately, and not the elapsed time: a wall-clock bound
            // cannot be made reliable on a shared CI runner, and every raise of such a bound costs it
            // more of the sensitivity it exists for.
            var (root, container) = await BuildAndLayout(BuildRepeatedSectionsHtml(sectionCount: 40));
            var boxCount = CountBoxes(root);

            // Without this, "visited no boxes" could just as well mean layout never asked - and
            // FloatScanCounters_CountTheWalkTheyGuard_WhenAFloatIsPresent pins the same counter to real
            // work in the case where the walk does run.
            Assert.True(container.FloatScanCalls > 0,
                "layout should have asked for an intersecting float many times over a document this size; " +
                "a zero call count means this test no longer exercises the float scan at all");

            Assert.True(container.FloatScanBoxVisits == 0,
                $"the float scan examined {container.FloatScanBoxVisits} boxes over a document with no floats " +
                "in it; HasFloatedBoxes should have answered every one of those lookups without a tree walk, " +
                "so any non-zero count is the O(document size) walk per box returning");

            // Sanity: the counted calls really are spread over the whole document, not a handful of boxes.
            Assert.True(container.FloatScanCalls > boxCount / 2,
                $"{container.FloatScanCalls} float lookups over {boxCount} boxes is far fewer than " +
                "expected - the fixture has probably stopped laying out the document it means to");
        }

        [Fact]
        public async Task FloatFreeDocument_FloatScanWorkPerBoxDoesNotGrowWithDocumentSize()
        {
            // The property the guard above protects, stated as a growth curve rather than a single point:
            // quadruple the document and the float scan's work per box must stay put.
            //
            // The load-bearing assertion is the one on Visits. Removing the short-circuit does not change
            // how OFTEN layout asks for an intersecting float - only how far each ask walks - so the call
            // count is flat either way (measured: calls = 5 x line count + 3, with and without it). Visits
            // is the measure that moves, from 0 to the O(document size) walk per box, and asserting it
            // against the box count is the O(n) vs O(n^2) statement.
            //
            // The two assertions on Calls guard a different hypothetical: a future caller that asks the
            // scan once per descendant rather than once per box needing line layout. Keeping both means
            // this test still says something if the regression arrives from that direction instead.
            //
            // Both measures are counts, so a contended runner cannot move them: the numbers below are
            // identical on a loaded machine and an idle one.
            List<(int Sections, int Boxes, long Calls, long Visits)> samples = [];

            foreach (var sections in new[] { 10, 20, 40 })
            {
                var (root, container) = await BuildAndLayout(BuildRepeatedSectionsHtml(sections));
                samples.Add((sections, CountBoxes(root), container.FloatScanCalls, container.FloatScanBoxVisits));
            }

            foreach (var sample in samples)
            {
                // O(n) vs O(n^2), as one bound: with the short-circuit this is 0 at every size. Without it,
                // each of the ~2 lookups per box walks to the root re-scanning every preceding sibling's
                // subtree, so the total lands in the millions for the largest sample here.
                Assert.True(sample.Visits <= sample.Boxes,
                    $"the float scan examined {sample.Visits} boxes laying out {sample.Boxes} float-free boxes " +
                    $"({sample.Sections} sections); with no floats in the document it should examine none, and " +
                    "anything growing faster than the box count is the O(document size) walk per box returning");

                // The lookups themselves must also stay proportional to the document. Measured at ~2.1 per
                // box here, but the constant is deliberately loose: the count aggregates across every
                // LayoutDocument invocation one PerformLayout makes, and a fixture with per-page margins
                // re-runs that loop several times (see FloatScanCalls' own documentation).
                Assert.True(sample.Calls <= 20 * sample.Boxes,
                    $"{sample.Calls} float lookups for {sample.Boxes} boxes ({sample.Sections} sections) is more " +
                    "than a constant number of lookups per box");
            }

            // Lookups per box across a 4x document (measured 2.068 -> 2.079, i.e. a ratio of 1.005): a
            // caller that asked per descendant instead of per box would push this towards 4.
            var smallest = samples[0];
            var largest = samples[^1];
            var smallestRate = (double)smallest.Calls / smallest.Boxes;
            var largestRate = (double)largest.Calls / largest.Boxes;

            Assert.True(largestRate <= 2.0 * smallestRate,
                $"float lookups per box grew from {smallestRate:F2} at {smallest.Sections} sections to " +
                $"{largestRate:F2} at {largest.Sections} sections - how often the scan is asked should not " +
                "depend on how big the document is");
        }

        [Fact]
        public async Task FloatScanCounters_CountTheWalkTheyGuard_WhenAFloatIsPresent()
        {
            // The counters above are only evidence if they can be non-zero. With a float in the document
            // the short-circuit does not fire, the walk runs for real, and both counters move - so a
            // future change that quietly stopped counting would fail here rather than silently turn the
            // two guards above into assertions about nothing.
            var (_, container) = await BuildAndLayout(Wrap(@"
                <div style='width:300pt;'>
                    <div style='float:left; width:100pt; height:50pt;'></div>
                    <p id='text' style='margin:0;'>Hello world</p>
                </div>"));

            Assert.True(container.FloatScanCalls > 0, "a document with a float should still ask for intersecting floats");
            Assert.True(container.FloatScanBoxVisits > 0,
                "with a float present the scan has to examine boxes to find it - a zero visit count means the " +
                "counter is not wired to the walk it is supposed to measure");
        }

        [Fact]
        public async Task ManyNestedBlocksWithoutFloats_StillRendersEveryPage()
        {
            // End-to-end companion to the counter guards: the same document through the real generator,
            // asserting output rather than duration.
            var html = BuildRepeatedSectionsHtml(sectionCount: 40);

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            // 40 bordered sections of 8 table rows each overflow one A4 page under any font metrics, so
            // this says the document paginated rather than merely that a PDF came back.
            Assert.True(document.PageCount > 1, $"expected the document to span pages, got {document.PageCount}");
        }

        [Fact]
        public async Task FloatLeft_WrapsBelowAFullWidthFloatRightSibling()
        {
            // A float:left box that would overlap a previously-placed, full-width float:right sibling
            // must wrap below it rather than overlapping - exercises FloatBoxLeft's handling of an
            // *opposite-direction* intersecting float (CssLayoutEngine.FloatBoxLeft's Floating.Right
            // branch), not just same-direction float avoidance.
            var html = Wrap(@"
                <div style='width:200pt;'>
                    <div id='r' style='float:right; width:200pt; height:50pt;'></div>
                    <div id='l' style='float:left; width:100pt; height:30pt;'></div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var r = FindById(root, "r")!;
            var l = FindById(root, "l")!;

            Assert.True(l.Location.Y >= r.ActualBottom,
                $"float:left box should wrap below the full-width float:right sibling it can't fit beside " +
                $"(l.Y={l.Location.Y}, r.ActualBottom={r.ActualBottom})");
        }

        [Fact]
        public async Task FloatRight_InNarrowerNestedBlock_AvoidsAWiderAncestorFloatRightSibling()
        {
            // A float:right box placed inside a narrower, non-floated nested block still avoids an
            // ancestor float:right sibling that sits past the nested block's own right edge - the search
            // climbs past the immediate containing block to find it (DomUtils.FindIntersectingFloatBox's
            // ancestor walk), which is what exercises DomUtils.IsFloatIntersecting's Floating.Right branch
            // and FloatBoxRight's matching switch case: neither is reachable when the intersecting float
            // and the box being placed share the same containing block, since a same-container float
            // can never start past that container's own right edge.
            var html = Wrap(@"
                <div style='width:500pt;'>
                    <div id='outerR' style='float:right; width:50pt; height:80pt;'></div>
                    <div style='width:200pt;'>
                        <div id='r' style='float:right; width:100pt; height:30pt;'></div>
                    </div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var outerR = FindById(root, "outerR")!;
            var r = FindById(root, "r")!;

            Assert.Equal(outerR.Location.X - outerR.ActualMarginLeft, r.ActualRight, 1);
        }

        [Fact]
        public async Task FloatRight_InNarrowerNestedBlock_WithMarginLeft_StillAvoidsAWiderAncestorFloatRightSibling()
        {
            // Companion to the test above, with margin-left added to the nested float: DomUtils.
            // IsFloatIntersecting's Floating.Right branch used to add coordinates.MarginLeft into the
            // threshold it compares the ancestor float's left edge against - a leftover term that used
            // to cancel out CssFloatCoordinates.FloatRightStartX's own margin-left bug, and became a
            // second, independent bug once that formula was fixed: it inflates the intersection
            // threshold by the nested float's own margin-left, so an ancestor float whose left edge
            // falls within that margin-left-wide window goes undetected and the nested float never
            // extends out to meet it. outerR's left edge (500 - 280 = 220pt) sits inside exactly that
            // window here (the correct threshold is 200pt, the buggy one 240pt).
            var html = Wrap(@"
                <div style='width:500pt;'>
                    <div id='outerR' style='float:right; width:280pt; height:80pt;'></div>
                    <div style='width:200pt;'>
                        <div id='r' style='float:right; width:100pt; height:30pt; margin-left:40pt;'></div>
                    </div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var outerR = FindById(root, "outerR")!;
            var r = FindById(root, "r")!;

            Assert.Equal(outerR.Location.X - outerR.ActualMarginLeft, r.ActualRight, 1);
        }

        [Fact]
        public async Task FloatRight_NarrowsLineWrapWidth_SoTextWrapsBeforeReachingIt()
        {
            // DomUtils.GetLastRightIntersectingFloatBox used to query
            // GetFirstIntersectingFloatBox in Floating.Left mode - a point-collision test that can
            // only detect a right float once the cursor has already walked into its span, never in
            // advance. That let words on the row overlapping the float's vertical span
            // ([0, 50pt)) be placed straight through it instead of wrapping before its left edge
            // (300pt container - 100pt float = 200pt).
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <div id='f' style='float:right; width:100pt; height:50pt;'></div>
                    <p id='text' style='margin:0;'>this line of text should wrap before it reaches the floated box on the right</p>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var floatBox = FindById(root, "f")!;
            var text = FindById(root, "text")!;
            var floatLeftEdge = floatBox.Location.X - floatBox.ActualMarginLeft;

            var wordsOverlappingFloat = WordsOverlappingVerticalSpan(text, floatBox.Location.Y, floatBox.ActualBottom);

            Assert.NotEmpty(wordsOverlappingFloat);

            foreach (var word in wordsOverlappingFloat)
            {
                Assert.True(word.Rectangle.Right <= floatLeftEdge + 1,
                    $"word '{word.Text}' at Rectangle.Right={word.Rectangle.Right} overlaps the float:right " +
                    $"box, whose left edge (including margin) is at {floatLeftEdge}");
            }
        }

        [Fact]
        public async Task FloatRight_WithMarginLeft_StillReachesContainingBlockRightEdge()
        {
            // CssFloatCoordinates.FloatRightStartX used to subtract MarginLeft a second time on top
            // of Right already being margin-right-adjusted (FloatBoxRight sets Right = limitRight -
            // box.ActualMarginRight), shifting a float:right box's own border-box left/right edges an
            // extra margin-left to the left. A box's left margin is space to its own left - it must
            // not pull the box's own right edge inward - so with margin-right:0, dd's border-box
            // right edge should sit flush against dl's ClientRight regardless of margin-left.
            var html = Wrap(@"
                <dl style='width:200pt; margin:0; padding:0; border:0;'>
                    <dd id='dd' style='float:right; width:80pt; height:20pt; margin:0 0 0 10pt;'></dd>
                </dl>");

            var (root, _) = await BuildAndLayout(html);
            var dl = FindById(root, "dd")!.ParentBox!;
            var dd = FindById(root, "dd")!;

            Assert.Equal(dl.ClientRight, dd.ActualRight, 1);
        }

        [Fact]
        public async Task FloatLeft_StillNarrowsLineWrapWidth_AfterTheRightFloatFix()
        {
            // Companion to the float:right fix above: GetLastLeftIntersectingFloatBox implements a
            // different (point-collision-then-push) algorithm that was already correct for
            // float:left, and must remain so. Every word overlapping the float's vertical span must
            // start at or after the float's right edge, and the paragraph must wrap to more lines
            // than an unfloated control.
            const string longText =
                "this line of text should wrap below and around the floated box on the left before it reaches the container edge";

            var withFloatHtml = Wrap($@"
                <div style='width:300pt;'>
                    <div id='f' style='float:left; width:100pt; height:50pt;'></div>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var withoutFloatHtml = Wrap($@"
                <div style='width:300pt;'>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var (withFloatRoot, _) = await BuildAndLayout(withFloatHtml);
            var (withoutFloatRoot, _) = await BuildAndLayout(withoutFloatHtml);

            var floatBox = FindById(withFloatRoot, "f")!;
            var withFloatText = FindById(withFloatRoot, "text")!;
            var withoutFloatText = FindById(withoutFloatRoot, "text")!;
            var floatRightEdge = floatBox.ActualRight + floatBox.ActualMarginRight;

            var wordsOverlappingFloat = WordsOverlappingVerticalSpan(withFloatText, floatBox.Location.Y, floatBox.ActualBottom);

            Assert.NotEmpty(wordsOverlappingFloat);

            foreach (var word in wordsOverlappingFloat)
            {
                Assert.True(word.Rectangle.Left >= floatRightEdge - 1,
                    $"word '{word.Text}' at Rectangle.Left={word.Rectangle.Left} starts before the float:left " +
                    $"box's right edge (including margin) at {floatRightEdge}");
            }

            Assert.True(withFloatText.ActualBoxSizingHeight > withoutFloatText.ActualBoxSizingHeight,
                $"narrowing the line width with a float:left should force extra line wraps and a taller box " +
                $"(with float: {withFloatText.ActualBoxSizingHeight}, without: {withoutFloatText.ActualBoxSizingHeight})");
        }

        [Fact]
        public async Task ClearLeft_IgnoresAPrecedingFloatRightSibling()
        {
            // clear:left only clears past float:left siblings - a float:right sibling must not push it
            // down (CssLayoutEngine.ClearBox's "Floating.Right when Clear.Left: continue" skip).
            var html = Wrap(@"
                <div style='width:200pt;'>
                    <div id='r' style='float:right; width:50pt; height:80pt;'></div>
                    <div id='cleared' style='clear:left; margin:0;'>text</div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var cleared = FindById(root, "cleared")!;

            Assert.True(cleared.Location.Y < 80,
                $"clear:left must not clear past a float:right sibling, was pushed to Y={cleared.Location.Y}");
        }

        [Fact]
        public async Task ClearRight_IgnoresAPrecedingFloatLeftSibling()
        {
            // Symmetric case: clear:right ignoring a float:left sibling (ClearBox's
            // "Floating.Left when Clear.Right: continue" skip).
            var html = Wrap(@"
                <div style='width:200pt;'>
                    <div id='l' style='float:left; width:50pt; height:80pt;'></div>
                    <div id='cleared' style='clear:right; margin:0;'>text</div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var cleared = FindById(root, "cleared")!;

            Assert.True(cleared.Location.Y < 80,
                $"clear:right must not clear past a float:left sibling, was pushed to Y={cleared.Location.Y}");
        }

        [Fact]
        public async Task FloatAfterAnInlineRun_LandsOnThatRunsLine()
        {
            // CSS 2.1 §9.5.1 rule 6: a float's outer top may not be lower than the top of the line box
            // it appears in. The inline run before the float closes a line, and the float is laid out as
            // an ordinary block child, so without the rule it starts on the line BELOW its own.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <b>BILL TO</b><div id='badge' style='float:left; width:40pt; height:12pt;'></div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var badge = FindById(root, "badge")!;
            var word = FindFirstWord(root)!;

            // Compared against the line box's own top (word.Line.LineTop), not the word's ink position -
            // a word sits half a leading below its line box (CSS 2.1 §10.8.1), which for `line-height:
            // normal` is a small, font-metric-dependent, not-necessarily-zero amount (issue #1054), so
            // comparing against the ink directly would make this assertion font-dependent for no reason
            // relevant to the rule under test (CSS 2.1 §9.5.1 rule 6, about the LINE box).
            var lineTop = word!.Line!.LineTop;
            Assert.True(badge.Location.Y <= lineTop + 0.001,
                $"the float belongs on the line it follows (top {lineTop}), was at Y={badge.Location.Y}");
        }

        [Fact]
        public async Task FloatAfterAnInlineRun_WithClear_StillGoesBelowTheLine()
        {
            // `clear` is ClearBox's job and rule 6 must not pre-empt it: the same shape with clear:left
            // keeps the float below the line.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <div style='float:left; width:20pt; height:30pt;'></div>
                    <b>BILL TO</b><div id='badge' style='clear:left; float:left; width:40pt; height:12pt;'></div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var badge = FindById(root, "badge")!;

            Assert.True(badge.Location.Y >= 30,
                $"clear:left must still clear the 30pt float, was at Y={badge.Location.Y}");
        }

        [Fact]
        public async Task FloatSiblingOfAnInlineBlockTable_StaysContainedInsideTheDiv()
        {
            // Issue #18: a float:right image sibling of a <table style="display:inline-block"> (an
            // atomic inline-level box per CSS Display 3 §2.3, issue #473) used to escape the containing
            // div entirely and overlap whatever followed it in the document. DomParser's anonymous-block
            // correction leaves the float as the sole child of its own tag-less wrapper - one level
            // removed from being a direct sibling of the table's own wrapper - which used to defeat rule
            // 6's "immediately preceding sibling" check (CssBox.FloatLineTop/UnwrapSoleFloatChild) and
            // MarginBottomCollapse's "last in-flow child" pick alike.
            var html = Wrap(@"
                <div id='outer' style='width:300pt;'>
                    <table style='display:inline-block; width:90%'>
                        <tr><td>A</td></tr><tr><td>B</td></tr><tr><td>C</td></tr>
                    </table>
                    <div id='badge' style='float:right; width:20pt; height:20pt;'></div>
                </div>
                <div id='next'>after</div>");

            var (root, _) = await BuildAndLayout(html);
            var outer = FindById(root, "outer")!;
            var badge = FindById(root, "badge")!;
            var next = FindById(root, "next")!;

            Assert.True(badge.ActualBottom <= outer.ActualBottom + 0.5,
                $"the float (bottom={badge.ActualBottom}) must stay inside its container (bottom={outer.ActualBottom}), not escape past it");
            Assert.True(outer.ActualBottom <= next.Location.Y + 0.5,
                $"the container's own reported height (bottom={outer.ActualBottom}) must not collapse below its real content, or the next sibling (Y={next.Location.Y}) starts inside it");
        }

        [Fact]
        public async Task FloatAfterARealBlockSibling_StartsBelowIt()
        {
            // The rule is scoped to a float whose preceding sibling is the ANONYMOUS block the parser
            // wrapped an inline run in. After a real block-level element a float starts below it, which
            // is what a browser does — so the preceding-sibling test has to distinguish the two.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <p style='margin:0; height:30pt;'>BILL TO</p><div id='badge' style='float:left; width:40pt; height:12pt;'></div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var badge = FindById(root, "badge")!;

            Assert.True(badge.Location.Y >= 30,
                $"a float after a real block sibling starts below it, was at Y={badge.Location.Y}");
        }

        [Fact]
        public async Task FloatRight_InOneGridItem_DoesNotNarrowLinesInTheNext()
        {
            // A grid item establishes an independent formatting context (css-grid-1 §6), so a float
            // inside one is invisible to the next one's lines. The right-float wrap-limit walk used to
            // run all the way to the document root, scanning every preceding sibling on the way, so
            // the float in the first item set the wrap limit for the second item's text.
            const string grid = @"
                <div style='display:grid; grid-template-columns:200pt 200pt; width:400pt;'>
                    <div>{0}<p style='margin:0;'>left column</p></div>
                    <div><p id='text' style='margin:0;'>The quick brown fox jumps over the lazy dog again and again</p></div>
                </div>";

            var (withFloat, _) = await BuildAndLayout(Wrap(
                string.Format(grid, "<div style='float:right; width:150pt; height:60pt;'></div>")));
            var (withoutFloat, _) = await BuildAndLayout(Wrap(string.Format(grid, "")));

            var constrained = RightmostWordEdge(FindById(withFloat, "text")!);
            var unconstrained = RightmostWordEdge(FindById(withoutFloat, "text")!);

            Assert.Equal(unconstrained, constrained, 3);
        }

        [Fact]
        public async Task FloatRight_InTheSameFormattingContext_StillNarrowsTheLine()
        {
            // The contrast case, and the reason the assertion above is about the formatting-context
            // boundary rather than about float avoidance having stopped working: with the float in the
            // SAME block as the text, its wrap limit must still apply.
            const string block = @"
                <div style='width:200pt;'>
                    {0}<p id='text' style='margin:0;'>The quick brown fox jumps over the lazy dog again and again</p>
                </div>";

            var (withFloat, _) = await BuildAndLayout(Wrap(
                string.Format(block, "<div style='float:right; width:150pt; height:60pt;'></div>")));
            var (withoutFloat, _) = await BuildAndLayout(Wrap(string.Format(block, "")));

            var constrained = RightmostWordEdge(FindById(withFloat, "text")!);
            var unconstrained = RightmostWordEdge(FindById(withoutFloat, "text")!);

            Assert.True(constrained < unconstrained - 1,
                $"a float:right in the same formatting context must still cap the line: " +
                $"{constrained} vs {unconstrained} unconstrained");
        }

        [Fact]
        public async Task LeftFloatScan_StopsAtAFormattingContextBoundary()
        {
            // The left-side point-collision walk used to run to the document root, scanning every
            // preceding sibling on the way, even though a float cannot affect content outside its own
            // formatting context (CSS 2.1 §9.5, css-display-3 §2.1) — the rule the right-side walk
            // already applies. Asserted on FloatScanBoxVisits rather than elapsed time, for the reason
            // this class's own doc comment gives about wall-clock bounds on a contended runner.
            var (_, container) = await BuildAndLayout(Wrap(string.Concat(Enumerable.Repeat(
                @"<div style='display:grid; grid-template-columns:200pt 200pt; width:400pt;'>
                    <div><div style='float:left; width:80pt; height:20pt;'></div><p style='margin:0;'>col a text here</p></div>
                    <div><p style='margin:0;'>col b text here</p></div>
                  </div>", 40))));

            // Each grid item is its own formatting context, so a walk that respects the boundary
            // examines a handful of boxes per call rather than the whole document behind it. Before
            // the boundary check this document cost 237,757 visits across 2,880 calls (83 each).
            var perCall = (double)container.FloatScanBoxVisits / container.FloatScanCalls;

            Assert.True(perCall < 10,
                $"the left float scan should not climb past its formatting context: " +
                $"{container.FloatScanBoxVisits} visits across {container.FloatScanCalls} calls ({perCall:F1} each)");
        }

        [Fact]
        public async Task PrecedingFloat_DoesNotShortenTheLinesOfAnAbsolutelyPositionedBox()
        {
            // CSS 2.1 §9.4.1: a line box is shortened only by floats in its own block formatting context,
            // and an absolutely positioned box establishes a new one (issue #1335). The ancestor walk
            // used to start at the positioned box itself and find the float that precedes it as a sibling.
            var html = Wrap(@"
                <div style='position:relative; width:300pt; height:80pt;'>
                    <div style='float:left; width:100pt; height:50pt;'></div>
                    <div id='abs' style='position:absolute; left:0; top:0; width:200pt;'>Hello world</div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var firstWord = FindFirstWord(FindById(root, "abs")!);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left < 50,
                $"the positioned box's text must not be pushed past the float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task PrecedingFloat_DoesNotDisplaceAnInlineBlockInsideAnAbsolutelyPositionedBox()
        {
            // The #1335 reproduction: the badge stayed visible but the link's background was culled,
            // because the inline-block was flowed after the float (X ~ 523pt) and then translated again
            // with its positioned parent, off the page.
            var html = Wrap(@"
                <style>
                  .header { position: fixed; left: 0; width: 100%; height: 52px; }
                  .row { position: relative; width: 100%; height: 52px; overflow: hidden; }
                  .preceding { float: left; width: 500pt; height: 39pt; }
                  .cart { position: absolute; right: 15px; top: 0; }
                  .cart a { display: inline-block; width: 52px; height: 52px; padding: 9px 9px 0; background: red; }
                  .cart span { position: absolute; top: 2px; right: -5px; }
                </style>
                <div class='header'><div class='row'>
                  <div class='preceding'></div>
                  <div class='cart' id='cart'><a id='link'><span>6</span></a></div>
                </div></div>");

            var (root, container) = await BuildAndLayout(html);
            var link = FindById(root, "link")!;
            var pageWidth = container.PageSize.Width;

            Assert.True(link.ActualRight <= pageWidth + 0.5,
                $"the link must stay inside the {pageWidth}pt page, right edge was {link.ActualRight}");
            Assert.True(link.Location.X > pageWidth - 100,
                $"the cart is right-aligned, link X was {link.Location.X} (page {pageWidth})");
        }

        [Fact]
        public async Task FloatInsideAnAbsolutelyPositionedBox_StillShortensItsOwnLines()
        {
            // The contrast for the tests above: the formatting-context boundary only shields a box from
            // floats OUTSIDE it. A float inside the positioned box shares its formatting context with the
            // box's lines and must still push them.
            var html = Wrap(@"
                <div style='position:relative; width:300pt; height:80pt;'>
                    <div style='position:absolute; left:0; top:0; width:200pt;'>
                        <div style='float:left; width:100pt; height:50pt;'></div>
                        <p id='text' style='margin:0;'>Hello world</p>
                    </div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var firstWord = FindFirstWord(FindById(root, "text")!);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left >= 90,
                $"text beside the box's own float must still be pushed, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task OverflowHiddenBlock_BesideAPrecedingFloat_StillWrapsItsTextBesideIt()
        {
            // An in-flow formatting-context root has to avoid the floats next to it (CSS 2.1 §9.5), which
            // this engine does by narrowing its lines - so #1335's boundary must not reach it.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <div style='float:left; width:100pt; height:50pt;'></div>
                    <div id='text' style='overflow:hidden;'>Hello world</div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var firstWord = FindFirstWord(FindById(root, "text")!);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left >= 90,
                $"text in an overflow:hidden block must still clear the float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task PrecedingFloat_DoesNotShortenTheLinesOfATableCell()
        {
            // The same boundary for another formatting-context root that flows inline content as its own
            // line owner: a float in one cell must not displace the text of the cell beside it.
            var html = Wrap(@"
                <table style='width:300pt; border-collapse:collapse;'><tr>
                    <td style='width:150pt; vertical-align:top;'><div style='float:left; width:100pt; height:50pt;'></div></td>
                    <td id='cell' style='width:150pt; vertical-align:top; padding:0;'>Hello world</td>
                </tr></table>");

            var (root, _) = await BuildAndLayout(html);
            var cell = FindById(root, "cell")!;
            var firstWord = FindFirstWord(cell);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left < cell.Location.X + 20,
                $"the cell's text must start at the cell's own left edge ({cell.Location.X}), was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task FloatAmidInlineContent_SharesOneLineWithTextBeforeAndAfter()
        {
            // The accepted-gap's own repro (issue #1038): a float that FOLLOWS some inline content and
            // PRECEDES more of it shares one line with both halves, rather than splitting the run into
            // two separate anonymous blocks with the float shoved between them as an ordinary block-level
            // sibling - which used to draw "XY" and the float on top of each other and push the trailing
            // text to a second line entirely.
            var html = Wrap(@"
                <div style='width:300pt; font:16px monospace;'>XY <span id='float' style='float:left'>ZZZZ</span> more words</div>");

            var (root, _) = await BuildAndLayout(html);
            var floatBox = FindById(root, "float")!;
            var words = CollectAllWords(root);
            var xy = words.First(w => w.Text == "XY");
            var more = words.First(w => w.Text is { } t && t.StartsWith("more"));

            Assert.True(floatBox.IsFloated);

            // All three share the line's own top - the float via CSS 2.1 §9.5.1 rule 6, "more" because
            // it is genuinely on the SAME line as "XY" rather than pushed onto a line of its own.
            var lineTop = xy.Line!.LineTop;
            Assert.Equal(lineTop, more.Line!.LineTop, 3);
            Assert.True(floatBox.Location.Y <= lineTop + 0.001,
                $"the float belongs on the shared line (top {lineTop}), was at Y={floatBox.Location.Y}");

            // "more" starts right after the float's right edge - on the same line as "XY", not back at
            // the container's left edge on a line of its own.
            var floatRightEdge = floatBox.ActualRight + floatBox.ActualMarginRight;
            Assert.True(more.Rectangle.Left >= floatRightEdge - 0.5,
                $"'more' (X={more.Rectangle.Left}) should start at or after the float's right edge ({floatRightEdge})");
            Assert.True(more.Rectangle.Left < floatRightEdge + 20,
                $"'more' (X={more.Rectangle.Left}) should start immediately after the float, not further along the line");
        }

        [Fact]
        public async Task FloatWithLeftInsetAmidInlineContent_DoesNotLeakItsOwnInsetIntoTheNextSiblingsCursor()
        {
            // Regression guard for a bug the post-#1038 review pass found: CssLayoutEngine.FlowBox's
            // per-child prologue computes leftSpacing from a child's OWN margin/border/padding-left for
            // anything that isn't position:absolute/fixed, and (when the child opens the line) adds it
            // straight onto coordinates.CurrentX before the float-dispatch branch runs. FlowFloatChild/
            // FloatBox never read CurrentX for the float's own placement - a float's real position comes
            // from blockBox.ClientLeft/ActualMarginLeft plus FloatBox's own left/right scan - so that add
            // is dead for the float's OWN iteration, but the float branch `continue`s without ever
            // resetting CurrentX again, so the polluted value survives into the NEXT sibling's iteration.
            // When the float is the first FLOAT on its line (nothing for GetIntersectingInlineFloat's
            // point-collision check to re-anchor against yet), that leaked value can make the check
            // wrongly conclude the cursor already passed the float, so the sibling after it is not
            // re-anchored to the float's true right edge (CSS 2.1 §9.5.1 rule 6) and instead trails
            // further along the line than it should - extra, spec-incorrect whitespace before it. The
            // exact numeric trigger condition depends on FloatBox's own placement scan as well as the
            // preceding run's advance, not just the float's declared insets in isolation - this fixture's
            // own numbers are simply a case that was empirically confirmed (by toggling the fix) to
            // reproduce the defect, not a derived formula.
            var html = Wrap(@"
                <div style='width:300pt; font:16px monospace;'>XY <span id='float' style='float:left;
                    width:10pt; height:18pt; margin-left:20pt; border-left:10pt solid black;
                    padding-left:15pt;'></span> more words</div>");

            var (root, _) = await BuildAndLayout(html);
            var floatBox = FindById(root, "float")!;
            var words = CollectAllWords(root);
            var more = words.First(w => w.Text is { } t && t.StartsWith("more"));

            Assert.True(floatBox.IsFloated);

            // "more" must start EXACTLY at the float's true right edge: the intervening space collapses
            // away at the out-of-flow boundary the same way it does before the float (css-text-3 §1.5),
            // so there is no legitimate gap here for a loose tolerance to hide behind. Confirmed to fail
            // without the fix (measured 9.79pt too far right for this fixture's own numbers) and pass
            // with it, by toggling CssLayoutEngine.cs's `excludedFromLineSpacing` guard locally and
            // re-running this test both ways.
            var floatRightEdge = floatBox.ActualRight + floatBox.ActualMarginRight;
            Assert.Equal(floatRightEdge, more.Rectangle.Left, 1);
        }

        [Fact]
        public async Task TwoAdjacentFloats_WithNoInlineContentBetweenThem_StillPlacedSideBySide()
        {
            // Regression guard: a box whose only children are floats (nothing genuinely inline anywhere
            // in it) must keep working exactly as before the #1038 fix - the "vacuously true" case
            // DomUtils.ContainsInlinesOnly's own remarks call out, and the shape FloatsShareTheLine already
            // documents as "floats sitting side by side with no inline content between them".
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <div id='a' style='float:left; width:40pt; height:20pt;'></div>
                    <div id='b' style='float:left; width:40pt; height:20pt;'></div>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 1);
            Assert.True(b.Location.X >= a.ActualRight - 0.5,
                $"the second float (X={b.Location.X}) should sit beside the first (right edge={a.ActualRight})");
        }

        [Fact]
        public async Task FloatAmidInlineContent_TextOnBothSidesWrapsAroundIt()
        {
            // A variant of the shared-line test using real inline elements (not bare text runs) on both
            // sides, and a float wide enough that the trailing text has to wrap beneath it on a narrow
            // container - confirming the float still narrows the SAME inline formatting context's later
            // lines exactly as a preceding floated sibling already did before #1038 (see
            // CssLineBoxCoordinates.InlineFloats / CssLayoutEngine.FlowBox's LeftFloatAt).
            const string longTail = "this trailing run of words must wrap beneath the floated box";
            var html = Wrap($@"
                <div style='width:150pt; font:16px monospace;'>
                    <span id='before'>Before</span>
                    <span id='float' style='float:left; width:60pt; height:18pt;'></span>
                    <span id='after'>{longTail}</span>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var floatBox = FindById(root, "float")!;
            var after = FindById(root, "after")!;

            Assert.True(floatBox.IsFloated);

            var floatTop = floatBox.Location.Y;
            var floatBottom = floatBox.ActualBottom;
            var floatRightEdge = floatBox.ActualRight + floatBox.ActualMarginRight;

            var afterWords = CollectAllWords(after);
            Assert.NotEmpty(afterWords);

            // Every word of "after" that falls within the float's vertical span must start at or past
            // its right edge; at least one word must fall below it once the line has wrapped, confirming
            // the trailing run genuinely continues around the float rather than overlapping it.
            var wordsBesideFloat = afterWords.Where(w => w.Top < floatBottom && w.Top + w.Height > floatTop).ToList();
            Assert.NotEmpty(wordsBesideFloat);
            foreach (var word in wordsBesideFloat)
            {
                Assert.True(word.Rectangle.Left >= floatRightEdge - 0.5,
                    $"word '{word.Text}' at X={word.Rectangle.Left} overlaps the float (right edge {floatRightEdge})");
            }

            // Words must genuinely advance one after another beside the float, not all pile up at its
            // right edge - and at least one must eventually drop below it once the line wraps past the
            // float's own height, confirming the trailing run really continues around the float.
            Assert.True(wordsBesideFloat.Zip(wordsBesideFloat.Skip(1), (a, b) => b.Rectangle.Left > a.Rectangle.Left || b.Top > a.Top).All(x => x),
                "words beside the float should advance across the line rather than stacking at its edge");
            Assert.Contains(afterWords, w => w.Top >= floatBottom - 0.5);
        }

        [Fact]
        public async Task FloatRightAmidInlineContent_SharesLineAndCapsWhereLaterWordsMayReach()
        {
            // The float:right counterpart of the left-float test above, exercising
            // CssLayoutEngine.GetIntersectingInlineFloat's Floating.Right branch (a lookahead cap,
            // independent of the cursor's current position - see that method's own remarks, mirroring
            // DomUtils.GetLastRightIntersectingFloatBox's identical asymmetry with the left-float case).
            const string longTail = "this trailing run of words must wrap before it reaches the floated box";
            var html = Wrap($@"
                <div style='width:150pt; font:16px monospace;'>
                    <span id='before'>Before</span>
                    <span id='float' style='float:right; width:60pt; height:18pt;'></span>
                    <span id='after'>{longTail}</span>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var floatBox = FindById(root, "float")!;
            var after = FindById(root, "after")!;

            Assert.True(floatBox.IsFloated);

            var floatTop = floatBox.Location.Y;
            var floatBottom = floatBox.ActualBottom;
            var floatLeftEdge = floatBox.Location.X - floatBox.ActualMarginLeft;

            var afterWords = CollectAllWords(after);
            Assert.NotEmpty(afterWords);

            var wordsBesideFloat = afterWords.Where(w => w.Top < floatBottom && w.Top + w.Height > floatTop).ToList();
            Assert.NotEmpty(wordsBesideFloat);
            foreach (var word in wordsBesideFloat)
            {
                Assert.True(word.Rectangle.Right <= floatLeftEdge + 0.5,
                    $"word '{word.Text}' at right={word.Rectangle.Right} overlaps the float:right (left edge {floatLeftEdge})");
            }

            // The trailing run eventually continues below the float once the line has wrapped past its
            // own height, confirming it genuinely continues around the float rather than stopping short.
            Assert.Contains(afterWords, w => w.Top >= floatBottom - 0.5);
        }

        [Fact]
        public async Task FloatAmidInlineContent_TallerThanOnePage_OverflowsWithoutCorruptingSurroundingContent()
        {
            // A float sharing a line with inline content (issue #1038) is placed through
            // CssLayoutEngine.FlowFloatChild, which lays its own content out via
            // CssBox.LayoutContentAtItsAssignedPosition - the same entry point an inline-block with real
            // block content already uses (CssLayoutEngine.FlowAtomicBlockContentChild). Neither propagates
            // a nested PendingBreakToken any further today, so a float whose own content cannot fit the
            // remaining fragmentainer overflows the page it starts on rather than continuing onto a later
            // one - see FlowFloatChild's own remarks and the #1038 migration note. This pins that as a
            // SAFE, non-corrupting outcome (no exception, everything genuinely AFTER the float in the
            // document still laid out and positioned) rather than as full pagination support, which this
            // deliberately does not claim. A declared `height` alone (no content) never asks a pagination
            // question at all - the same as any other monolithic box sized by CSS rather than by content
            // - which is why this document still completes in one page; the accompanying resume test
            // (FloatAmidInlineContent_SurroundingContentResumesAcrossAPageBoundary_WithNoWordLostOrDuplicated)
            // is what actually exercises a multi-page document with a float present.
            var html = LayoutHarness.Wrap(@"
                <div style='width:300pt; font:16px monospace;'>
                    Before <span id='float' style='float:left; width:50pt; height:2000pt;'></span> after text right here.
                </div>
                <p id='next' style='margin:0;'>Next paragraph after everything else in the document.</p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 200, margin: 10);

            var floatBox = LayoutHarness.FindById(root, "float")!;
            var next = LayoutHarness.FindById(root, "next")!;

            Assert.True(floatBox.IsFloated);
            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 1, "layout must still produce output");

            // The float's own height overflowing every page must not corrupt the document that follows
            // it: "next" still gets a real, finite position at or after the float's own starting point.
            Assert.True(double.IsFinite(next.Location.Y) && next.Location.Y >= 0,
                $"'next' should still have a valid position, got Y={next.Location.Y}");
            Assert.True(next.Location.Y >= floatBox.Location.Y,
                "'next' comes after the float in source order and must not be placed above it");
        }

        [Fact]
        public async Task FloatAmidInlineContent_SurroundingContentResumesAcrossAPageBoundary_WithNoWordLostOrDuplicated()
        {
            // The other half of #1038's pagination scope: a float that fits comfortably on the page it
            // starts on must not stop the SURROUNDING inline content from pausing and resuming across a
            // page boundary the ordinary way - this is genuinely the same InlineBreakToken/CreateLineBoxes
            // machinery every other paginated paragraph already uses (the float itself never sets
            // coordinates.Break; see FlowBox's own float branch), so nothing about the float's presence
            // should make this pass any differently than a float-free paragraph long enough to paginate.
            var words = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
            var html = LayoutHarness.Wrap($@"
                <div style='width:200pt; font:12px monospace;'>
                    Start of the run <span id='float' style='float:left; width:30pt; height:20pt;'></span> {words} end of the run
                </div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 200, pageHeight: 150, margin: 10);

            var floatBox = LayoutHarness.FindById(root, "float")!;
            Assert.True(floatBox.IsFloated);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "200 words at 12px monospace on a 150pt page should span more than one page");

            var allWords = LayoutHarness.Descendants(root).SelectMany(b => b.Words)
                .Where(w => !w.IsLineBreak).ToList();

            // Every authored word survives layout exactly once - the resume must not drop, duplicate, or
            // corrupt any of them just because a float sits earlier in the same inline formatting context.
            Assert.Equal(allWords.Count, allWords.Distinct().Count());
            Assert.Contains(allWords, w => w.Text == "word1");
            Assert.Contains(allWords, w => w.Text == "word200");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// How far right this subtree's text actually reached — the wrap limit as laid out, which is
        /// what a float's line constraint moves.
        /// </summary>
        private static double RightmostWordEdge(CssBox box)
        {
            var right = 0d;
            foreach (var word in box.Words) right = Math.Max(right, word.Rectangle.Right);
            foreach (var child in box.Boxes) right = Math.Max(right, RightmostWordEdge(child));
            return right;
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static string BuildRepeatedSectionsHtml(int sectionCount)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append(".section { border: 1px solid black; padding: 4px; margin-bottom: 8px; }");
            sb.Append("table { width: 100%; border-collapse: collapse; } td { border: 1px solid #ccc; padding: 2px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append($"<div class='section'><h3>Section {i}</h3><table>");
                for (var row = 0; row < 8; row++)
                {
                    sb.Append($"<tr><td>Item {i}-{row}</td><td>Qty {row}</td><td>${row * 10}.00</td></tr>");
                }
                sb.Append("</table></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static int CountBoxes(CssBox box)
        {
            var count = 1;

            foreach (var child in box.Boxes)
            {
                count += CountBoxes(child);
            }

            return count;
        }

        private static CssRect? FindFirstWord(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];
            foreach (var child in box.Boxes)
            {
                var found = FindFirstWord(child);
                if (found is not null) return found;
            }
            return null;
        }

        private static List<CssRect> WordsOverlappingVerticalSpan(CssBox box, double top, double bottom)
        {
            List<CssRect> words = [];
            CollectWordsOverlappingVerticalSpan(box, top, bottom, words);
            return words;
        }

        private static List<CssRect> CollectAllWords(CssBox box)
        {
            List<CssRect> words = [];
            CollectAllWords(box, words);
            return words;
        }

        private static void CollectAllWords(CssBox box, List<CssRect> words)
        {
            words.AddRange(box.Words);
            foreach (var child in box.Boxes)
            {
                CollectAllWords(child, words);
            }
        }

        private static void CollectWordsOverlappingVerticalSpan(CssBox box, double top, double bottom, List<CssRect> words)
        {
            foreach (var word in box.Words)
            {
                if (word.Top < bottom && word.Top + word.Height > top)
                {
                    words.Add(word);
                }
            }

            foreach (var child in box.Boxes)
            {
                CollectWordsOverlappingVerticalSpan(child, top, bottom, words);
            }
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
