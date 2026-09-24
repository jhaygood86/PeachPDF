using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A float, or an inline-block, taller than the page it sits on shows all of its content: the lines that do
    /// not fit one page carry on onto the next instead of vanishing (issues #1201 and #1332, css-break-3 §2 and
    /// §4.4 "avoid losing content off the edge of the fragmentainer").
    /// </summary>
    /// <remarks>
    /// 200pt pages with 20pt margins leave a 160pt band, which holds eight of the 20pt lines below - fixed
    /// line heights, one word per line, so nothing here depends on a font's metrics.
    /// </remarks>
    public class TallFloatIntegrationTests
    {
        private const int Pages = 200;

        private static string Lines(string prefix, int count) =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"<div>{prefix}{i}</div> "));

        private static IEnumerable<string> ExpectedWords(string prefix, int count) =>
            Enumerable.Range(1, count).Select(i => $"{prefix}{i}");

        /// <summary>The words painted on every page, in page order, each with the page it landed on.</summary>
        private static List<(int Page, string Text)> WordsByPage(PeachPDF.Html.Core.HtmlContainerInt container)
        {
            var words = new List<(int, string)>();
            var pages = container.FragmentTree!.Fragmentainers.Count;

            for (var page = 0; page < pages; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);
                words.AddRange(g.DrawStringCalls.Select(w => (page, w.Text.Trim())));
            }

            return words;
        }

        private static void AssertEveryLineOnceInOrder(List<(int Page, string Text)> words, string prefix, int count)
        {
            var found = words.Where(w => w.Text.StartsWith(prefix)).ToList();

            Assert.Equal(ExpectedWords(prefix, count), found.Select(w => w.Text));
            Assert.True(found.Select(w => w.Page).Distinct().Count() > 1, "the content was expected to span pages");
            Assert.Equal(found.Select(w => w.Page).OrderBy(p => p), found.Select(w => w.Page));
        }

        [Fact]
        public async Task FloatAmongInlineContent_TallerThanOnePage_ShowsEveryLine()
        {
            var (_, container) = await LayoutAsync(
                Wrap($"<div style='width:300pt; font-size:10pt; line-height:20pt'>Before <span style='float:left; width:50pt'>{Lines("G", 20)}</span> after</div>"),
                pageWidth: Pages, pageHeight: Pages, margin: 20);

            var words = WordsByPage(container);

            AssertEveryLineOnceInOrder(words, "G", 20);
            Assert.Single(words, w => w.Text == "Before");
            Assert.Single(words, w => w.Text == "after");
        }

        [Fact]
        public async Task InlineBlockWithBlockContent_TallerThanOnePage_ShowsEveryLine()
        {
            var (_, container) = await LayoutAsync(
                Wrap($"<div style='width:300pt; font-size:10pt; line-height:20pt'>Before <span style='display:inline-block; width:50pt'>{Lines("I", 20)}</span> after</div>"),
                pageWidth: Pages, pageHeight: Pages, margin: 20);

            var words = WordsByPage(container);

            AssertEveryLineOnceInOrder(words, "I", 20);
            Assert.Single(words, w => w.Text == "Before");
            Assert.Single(words, w => w.Text == "after");
        }

        [Fact]
        public async Task FloatAmongInlineContent_ThatFitsOnePage_IsUnaffected()
        {
            var (_, container) = await LayoutAsync(
                Wrap($"<div style='width:300pt; font-size:10pt; line-height:20pt'>Before <span style='float:left; width:50pt'>{Lines("G", 4)}</span> after</div>"),
                pageWidth: Pages, pageHeight: Pages, margin: 20);

            var words = WordsByPage(container);

            Assert.Equal(ExpectedWords("G", 4), words.Where(w => w.Text.StartsWith("G")).Select(w => w.Text));
            Assert.All(words, w => Assert.Equal(0, w.Page));
        }

        [Theory]
        [InlineData("overflow:hidden;")]
        [InlineData("")]
        public async Task PageFloatBottom_TallerThanThePageBand_ShowsEveryLine(string extraStyle)
        {
            // 240pt of lines against a 160pt band: placed at the foot of the band its top sat 80pt above the
            // page, so the first four lines were drawn on no page at all.
            var (root, container) = await LayoutAsync(
                Wrap($"<div style='height:95pt'>filler</div><div><div id='f' style='float:bottom; {extraStyle} width:60pt; line-height:20pt; font-size:10pt'>{Lines("F", 12)}</div> after</div>"),
                pageWidth: Pages, pageHeight: Pages, margin: 20);

            var words = WordsByPage(container);
            var f = FindById(root, "f")!;

            AssertEveryLineOnceInOrder(words, "F", 12);
            Assert.Single(words, w => w.Text == "filler");
            Assert.Single(words, w => w.Text == "after");
            Assert.Equal(container.PageTopOf(0), f.Location.Y, 0.5);
        }

        [Fact]
        public async Task PageFloatBottom_ThatFitsTheBand_StaysAtTheFootOfItsPage()
        {
            // The control for the oversized case: a float that fits is still reserved for and anchored at the
            // page's foot, so only one taller than the band takes the top-of-page placement.
            var (root, container) = await LayoutAsync(
                Wrap("<div style='height:20pt'>filler</div><div id='f' style='float:bottom; width:60pt; height:150pt'></div>"),
                pageWidth: Pages, pageHeight: Pages, margin: 20);

            var f = FindById(root, "f")!;

            Assert.Equal(container.PageBottomOf(0), f.ActualBottom, 0.5);
            Assert.Equal(container.PageBottomOf(0) - 150, f.Location.Y, 0.5);
            Assert.Equal(150, container.BottomFloatAreaHeightsBySlot[0], 0.5);
        }
    }
}
