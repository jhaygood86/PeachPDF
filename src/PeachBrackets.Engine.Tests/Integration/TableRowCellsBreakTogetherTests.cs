using PeachPDF;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A table row that does not fit at a page's foot breaks all its cells together: no cell is left behind
    /// at the foot with its text cut by the page edge while a sibling cell has moved on
    /// (css-tables-3 §6.1, css-break-3 §4.4).
    /// </summary>
    public class TableRowCellsBreakTogetherTests
    {
        private const double PageContentBottom = 250 - 20;

        private const string Document =
            "<!DOCTYPE html><html><head><style>@page { size: 300pt 250pt; margin: 20pt; } " +
            "body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; }</style></head><body>" +
            "<div><table border=\"1\"><tr><td><h3>w50774_239</h3><h3>w50774_240 w50774_241 w50774_242 w50774_243 w50774_244 " +
            "w50774_245 w50774_246 w50774_247 w50774_248 w50774_249 w50774_250 w50774_251</h3>w50774_252 w50774_253 w50774_254 " +
            "w50774_255 w50774_256 w50774_257 w50774_258 w50774_259</td><td>w50774_260 w50774_261</td></tr></table></div></body></html>";

        [Fact]
        public async Task EveryWord_IsDrawnInsideThePageThatHoldsIt()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;

            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                var words = Flatten(page.Root).SelectMany(f => f.Words).ToList();

                foreach (var word in words.Where(w => w.Word.Text.StartsWith("w50774_")))
                {
                    Assert.True(
                        word.Rect.Bottom <= PageContentBottom + 0.5,
                        $"page {index}: {word.Word.Text} ends at {word.Rect.Bottom:F1}, past the content area's {PageContentBottom}");
                }
            }
        }

        [Fact]
        public async Task TheSecondCell_IsNotLeftBehindOnAPageOfItsOwn()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;
            var first = PageOf(pages, "w50774_239");
            var second = PageOf(pages, "w50774_260");

            Assert.Equal(first, second);
        }

        private static int PageOf(IReadOnlyList<FragmentainerFragment> pages, string text) =>
            Enumerable.Range(0, pages.Count)
                .First(i => Flatten(pages[i].Root).SelectMany(f => f.Words).Any(w => w.Word.Text == text));

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
