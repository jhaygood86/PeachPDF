using System.Text;
using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;

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

        // ── Helpers ────────────────────────────────────────────────────────────

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
