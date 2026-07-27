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
    /// was flaky on contended CI runners (both target frameworks' test runs share one two-core job) and
    /// could not be fixed by raising the bound, since raising it is exactly what removes its ability to
    /// see the regression it guards.
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
            // cannot be made reliable on a shared CI runner (the two TFM test runs execute concurrently
            // in one job), and every raise of such a bound costs it more of the sensitivity it exists for.
            var (root, container) = await BuildAndLayout(BuildRepeatedSectionsHtml(sectionCount: 40));

            // Without this, "visited no boxes" could just as well mean layout never asked - see the
            // companion test below, which pins the counter to real work when floats ARE present.
            Assert.True(container.FloatScanCalls > 0,
                "layout should have asked for an intersecting float many times over a document this size; " +
                "a zero call count means this test no longer exercises the float scan at all");

            Assert.Equal(0, container.FloatScanBoxVisits);

            // Sanity: the counted calls really are spread over the whole document, not a handful of boxes.
            Assert.True(container.FloatScanCalls > CountBoxes(root) / 2,
                $"{container.FloatScanCalls} float lookups over {CountBoxes(root)} boxes is far fewer than " +
                "expected - the fixture has probably stopped laying out the document it means to");
        }

        [Fact]
        public async Task FloatFreeDocument_FloatScanWorkPerBoxDoesNotGrowWithDocumentSize()
        {
            // The property the guard above protects, stated as a growth curve rather than a single point:
            // quadruple the document and the float scan's work per box must stay put. A regression to the
            // O(document size) walk makes the work per box grow *with* the document, which is exactly the
            // quadratic total the short-circuit was added to remove.
            //
            // Both measures are counts, so a contended runner cannot move them: the numbers below are
            // identical on a loaded machine and an idle one.
            var samples = new List<(int Sections, int Boxes, long Calls, long Visits)>();

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

                // The lookups themselves must also stay proportional to the document (measured: ~2.1 per box).
                Assert.True(sample.Calls <= 4 * sample.Boxes,
                    $"{sample.Calls} float lookups for {sample.Boxes} boxes ({sample.Sections} sections) is more " +
                    "than a constant number of lookups per box");
            }

            // Work per box across a 4x document: flat is ~1.0, quadratic would be ~4.0.
            var smallest = samples[0];
            var largest = samples[^1];
            var smallestRate = (double)smallest.Calls / smallest.Boxes;
            var largestRate = (double)largest.Calls / largest.Boxes;

            Assert.True(largestRate <= 2.0 * smallestRate,
                $"float-scan work per box grew from {smallestRate:F2} at {smallest.Sections} sections to " +
                $"{largestRate:F2} at {largest.Sections} sections - work per box should not depend on how big " +
                "the document is");
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

            Assert.True(document.PageCount > 0);
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
