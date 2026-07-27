using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A word the pass never reached carries no position of its own, and document Y 0 lies inside the
    /// <i>first</i> slot's own band — so the first page's fragment used to claim every word below the
    /// break as well as the page the word really lands on (issue #433).
    /// </summary>
    public class UnreachedWordClaimTests
    {
        /// <summary>
        /// #374's workhorse invariant, over the whole document rather than around the first page: every
        /// word the document authored is claimed by exactly one fragment. It fails one way if a fragment
        /// claims a word another one also holds, and the other way if a word is dropped entirely.
        /// </summary>
        [Theory]
        [InlineData(1500)]
        [InlineData(3000)]
        public async Task AParagraphSplitAtTheFirstBoundary_ClaimsEveryWordExactlyOnce(int wordCount)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document(wordCount), margin: 10);

            var authored = WordsIn(root);
            var claimed = ClaimedWords(container);

            Assert.NotEmpty(authored);
            Assert.Equal(claimed.Count, claimed.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(authored.Count, claimed.Count);
        }

        /// <summary>
        /// The symptom the invariant above states abstractly: the first page's own text layer holds only
        /// the words that page shows. Every later slot's band starts below 0, so an unpositioned word was
        /// only ever inside the first one's.
        /// </summary>
        [Fact]
        public async Task TheFirstPage_ClaimsOnlyTheWordsItShows()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document(3000), margin: 10);

            var fragmentainers = container.FragmentTree!.Fragmentainers;
            Assert.True(fragmentainers.Count > 1, "the fixture must span more than one page");

            var onFirstPage = Flatten(fragmentainers[0].Root).SelectMany(f => f.Words).Count();

            var authored = WordsIn(root).Count;

            Assert.True(onFirstPage < authored,
                $"the first page claimed {onFirstPage} of the document's {authored} words");

            // What the page really shows: the words on the line boxes whose own position falls in the
            // first slot's band. The fragment's own count has to agree with it.
            var shown = WordsIn(root)
                .Count(w => container.SlotStartingAt(w.Top) == 0 && !w.AwaitsTheNextFragmentainer);

            Assert.Equal(shown, onFirstPage);
        }

        private static string Document(int wordCount) =>
            LayoutHarness.Wrap($"<p>{string.Join(" ", Enumerable.Range(0, wordCount).Select(i => $"w{i}"))}</p>");

        private static List<CssRect> WordsIn(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

        private static List<CssRect> ClaimedWords(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

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
