using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block's inline content is laid out into one fragmentainer at a time: where a line does not
    /// fit, the flow stops, records where it stopped, and the next pass resumes there
    /// (<see href="https://www.w3.org/TR/css-break-3/#breaking-controls">CSS Fragmentation Level 3
    /// §2/§4.1</see>).
    /// </summary>
    public class ResumableInlineLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        /// <summary>
        /// Enough lines to overflow the first page's 160pt band several times over. The line height
        /// deliberately does not divide that band: at 20pt every line would land flush on a boundary
        /// and fit, so nothing would ever have to break and these would test nothing.
        /// </summary>
        private static string ManyLines(int count) =>
            LayoutHarness.Wrap(
                "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                string.Join("<br>", Enumerable.Range(0, count).Select(i => $"Line{i}")) +
                "</p>");

        [Fact]
        public async Task ParagraphCrossingABoundary_ResumesAtTheBandTop()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(ManyLines(20), pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            // Every line either sits in the band it started in, or is the first line of a band and
            // starts exactly at that band's content edge - never one unit below it.
            foreach (var line in p!.LineBoxes)
            {
                var top = line.Words.Min(w => w.Top);
                var slot = container.PageIndexOf(top + HtmlContainerInt.PageBoundaryEpsilon);
                Assert.InRange(top, container.PageTopOf(slot), container.PageBottomOf(slot));
            }
        }

        [Fact]
        public async Task ParagraphCrossingABoundary_TakesOnePassPerFragmentainerItSpans()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(ManyLines(20), pageHeight: PageHeight, margin: Margin);

            // 20 lines at 22pt is 440pt of content in 160pt bands, and no line lands flush on a
            // boundary, so the flow genuinely has to stop and resume rather than being laid out once
            // and sliced afterwards.
            Assert.True(container.FragmentainerPasses > 1,
                $"expected the flow to resume, but it took {container.FragmentainerPasses} pass(es)");
        }

        [Fact]
        public async Task ResumedFlow_KeepsEveryLineExactlyOnce()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(ManyLines(20), pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            // Lines are appended to the existing list across passes, never rebuilt: a duplicate would
            // mean the resumed pass re-flowed content an earlier fragmentainer already kept, and a gap
            // would mean it dropped some.
            var texts = p!.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).Select(w => w.Text).ToList();
            Assert.Equal([.. Enumerable.Range(0, 20).Select(i => $"Line{i}")], texts);
        }

        [Fact]
        public async Task ResumedFlow_LeavesNoEmptyLineBoxBehind()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(ManyLines(20), pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            // Each resumed pass seeds a line box before flowing; when the first word forces a wrap - a
            // <br>, or content that no longer fits - that seed is abandoned. Left behind it would be an
            // empty line in the middle of the block, which orphans/widows and ::first-line both read by
            // index, and which would consume a blank line's height at the top of the page.
            Assert.All(p!.LineBoxes, l => Assert.NotEmpty(l.Words));
        }

        [Fact]
        public async Task ResumedFlow_KeepsTheRectanglesEarlierFragmentainersAlreadyPainted()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                    "<span id='s' style='background:#0f0'>" +
                    string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
                    "</span></p>"),
                pageHeight: PageHeight, margin: Margin);

            var span = LayoutHarness.FindById(root, "s");
            Assert.NotNull(span);

            // The inline box's per-line rectangles are what its background and borders paint over. A
            // resumed pass must not reset them, or every page before the last would lose them.
            Assert.True(span!.Rectangles.Count > 1,
                $"expected one rectangle per line the span spans, got {span.Rectangles.Count}");
        }

        [Fact]
        public async Task LineMixingTallAndShortContent_MovesWholeInsteadOfStrandingItsShorterWords()
        {
            // The tall image and the text beside it share a line. Positioned so the line straddles the
            // first boundary, the image is tall enough to straddle on its own while the text is not.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'></div>" +
                    "<p id='p' style='margin:0;line-height:12pt;font-size:10pt'>" +
                    "short words before " +
                    "<img src='data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==' " +
                    "style='width:10pt;height:60pt'> after</p>"),
                pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            // css-break-3 §4.1: a line box is a monolithic break unit. The per-word model this replaced
            // moved only the words that individually straddled, stranding the shorter ones on the page
            // the rest of their line had left.
            foreach (var line in p!.LineBoxes)
            {
                var slots = line.Words
                    .Select(w => container.PageIndexOf(w.Top + HtmlContainerInt.PageBoundaryEpsilon))
                    .Distinct()
                    .ToList();

                Assert.True(slots.Count == 1,
                    $"a line was split across fragmentainers {string.Join(", ", slots)}");
            }
        }

        [Fact]
        public async Task ContentTallerThanAWholeBand_OverflowsRatherThanLooping()
        {
            // A single word taller than any fragmentainer has nowhere to fit, so it is monolithic
            // content (§2): it overflows its band rather than being moved forever.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0'>before<img src='data:image/gif;base64," +
                    "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==' " +
                    "style='width:10pt;height:400pt'></p>"),
                pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);
            Assert.NotEmpty(p!.LineBoxes);
        }

        [Fact]
        public async Task TextIndent_AppliesToTheFirstLineOnly_NotTheResumedOne()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt;text-indent:40pt'>" +
                    string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
                    "</p>"),
                pageHeight: PageHeight, margin: Margin);

            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var lines = p!.LineBoxes;
            var firstLeft = lines[0].Words.Min(w => w.Left);

            // CSS Text §5: the indent belongs to the first formatted line of the block, not to the
            // first line of every fragment.
            var resumed = lines.First(l =>
                container.PageIndexOf(l.Words.Min(w => w.Top) + HtmlContainerInt.PageBoundaryEpsilon) > 0);

            Assert.True(resumed.Words.Min(w => w.Left) < firstLeft,
                "the resumed fragment's first line should not be indented");
        }
    }
}
