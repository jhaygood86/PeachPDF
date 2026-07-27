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
    /// A word the flow never reached carries no position of its own, and document Y 0 lies inside the
    /// <i>first</i> slot's own band — so the first page's fragment claimed every word below the break as
    /// well as the page the word really lands on (issue #433).
    /// </summary>
    /// <remarks>
    /// The margin matters and is taken from production. <see cref="LayoutHarness"/>'s own default of 20pt
    /// puts slot 0's band below a word's zero rectangle (height ≈ 13pt) entirely, so the defect is
    /// invisible there; <c>PdfGenerateConfig</c>'s default is 10pt, where it is not.
    /// </remarks>
    public class UnreachedWordClaimTests
    {
        /// <summary>
        /// #374's workhorse invariant, over the whole document: every word the document authored is claimed
        /// by exactly one fragment. It fails one way if a fragment claims a word another one also holds, and
        /// the other way if a word is dropped entirely.
        /// </summary>
        /// <remarks>
        /// Asked of several shapes because what stops is the fill rather than the paragraph: an inline box,
        /// a float and a multi-column container each reach <c>CreateLineBoxes</c> by their own route. Two
        /// shapes are deliberately absent, each with a pre-existing residual measured identical with and
        /// without this fix — a list item drops its own <c>::marker</c> (#444), and an absolutely-positioned
        /// inline drops its words (#318).
        /// </remarks>
        [Theory]
        [InlineData("<p>{F}</p>")]
        [InlineData("<p>{F} <b>bold words carried across the break</b> {F}</p>")]
        [InlineData("<p><span style='color:red'>{F}</span></p>")]
        [InlineData("<div>{F}<span style='float:left;width:40pt'>fl oa ted</span>{F}</div>")]
        [InlineData("<div style='column-count:2'><p>{F}</p></div>")]
        public async Task AParagraphSplitAtAPageBoundary_ClaimsEveryWordExactlyOnce(string template)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document(template, 2500), margin: 10);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            var authored = WordsIn(root);
            var claimed = ClaimedWords(container);

            Assert.NotEmpty(authored);
            Assert.Equal(claimed.Distinct(ReferenceEqualityComparer.Instance).Count(), claimed.Count);
            Assert.Equal(claimed.Count, authored.Count);
        }

        /// <summary>
        /// The symptom the invariant above states abstractly: the first page's own text layer holds only the
        /// words that page shows. Every later slot's band starts below 0, so an unpositioned word was only
        /// ever inside the first one's.
        /// </summary>
        [Fact]
        public async Task TheFirstPage_ClaimsOnlyTheWordsItShows()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document("<p>{F}</p>", 3000), margin: 10);

            var fragmentainers = container.FragmentTree!.Fragmentainers;
            Assert.True(fragmentainers.Count > 1, "the fixture must span more than one page");

            var onFirstPage = Flatten(fragmentainers[0].Root).SelectMany(f => f.Words).ToList();
            var authored = WordsIn(root).Count;

            Assert.NotEmpty(onFirstPage);
            Assert.True(onFirstPage.Count < authored,
                $"the first page claimed {onFirstPage.Count} of the document's {authored} words");

            // Stated from the page grid rather than from the flag the fix sets, so that it is an independent
            // statement of the symptom: every word this page claims really does sit in this page's band.
            Assert.All(onFirstPage, w => Assert.Equal(0, container.SlotStartingAt(w.Word.Top)));
        }

        /// <summary>
        /// Saying "this layout has placed none of these words yet" reaches every word in the block's
        /// subtree, which is a larger set than its inline flow visits — an <i>outside</i> <c>::marker</c> is
        /// skipped by the flow (<c>CssLayoutEngine.FlowBox</c>) and laid out by the item's own epilogue
        /// instead. That epilogue runs in the same pass for an item that does not itself break, so the
        /// marker is positioned before the slot is frozen and the claim stands.
        /// </summary>
        [Fact]
        public async Task AListWhoseItemsDoNotBreak_StillClaimsEveryMarker()
        {
            var items = string.Join("", Enumerable.Range(0, 200).Select(i => $"<li>item {i} of the list</li>"));
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<ul>{items}</ul>"), margin: 10);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            var markerWords = LayoutHarness.Descendants(root)
                .Where(b => b.IsMarkerPseudoElement)
                .SelectMany(b => b.Words)
                .ToList();

            Assert.NotEmpty(markerWords);

            var claimed = ClaimedWords(container).ToHashSet(ReferenceEqualityComparer.Instance);

            Assert.All(markerWords, w => Assert.Contains(w, claimed));
        }

        private static string Document(string template, int wordCount) =>
            LayoutHarness.Wrap(template.Replace(
                "{F}", string.Join(" ", Enumerable.Range(0, wordCount).Select(i => $"w{i}"))));

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
