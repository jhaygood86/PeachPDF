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
    /// <para>
    /// The margin matters and is taken from production. <see cref="LayoutHarness"/>'s own default of 20pt
    /// puts slot 0's band below a word's zero rectangle (height ≈ 13pt) entirely, so the defect is
    /// invisible there; <c>PdfGenerateConfig</c>'s default is 10pt, where it is not.
    /// </para>
    /// <para>
    /// The line geometry is pinned rather than left to the platform's default font, and the two other
    /// mechanisms that can move a word are turned off (<c>orphans: 1; widows: 1</c>), so that what these
    /// fixtures measure is which fragment claims a word the flow never reached. A 20pt line against an
    /// 830pt band leaves every page's last line 10pt clear of the boundary, which keeps them out of the
    /// 0.5pt window where layout and the emitter disagree about membership (#446) — reached on
    /// <c>windows-latest</c> only, and a different defect.
    /// </para>
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
        /// a float, a list item and a multi-column container each reach <c>CreateLineBoxes</c> by their own
        /// route. One shape is deliberately absent, with a pre-existing residual measured identical with and
        /// without this fix — an absolutely-positioned inline drops its words (#318).
        /// </remarks>
        [Theory]
        [InlineData("<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}</p>")]
        [InlineData("<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F} <b>bold words carried across the break</b> {F}</p>")]
        [InlineData("<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'><span style='color:red'>{F}</span></p>")]
        [InlineData("<div style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}<span style='float:left;width:40pt'>fl oa ted</span>{F}</div>")]
        [InlineData("<div style='column-count:2'><p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}</p></div>")]
        [InlineData("<ul><li style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}</li></ul>")]
        public async Task AParagraphSplitAtAPageBoundary_ClaimsEveryWordExactlyOnce(string template)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document(template, 2500), pageHeight: 850, margin: 10);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            var authored = WordsIn(root);
            var claimed = ClaimedWords(container);

            Assert.NotEmpty(authored);
            Assert.True(claimed.Count == claimed.Distinct(ReferenceEqualityComparer.Instance).Count(),
                DescribeDoubleClaims(container));
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
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}</p>", 3000),
                pageHeight: 850, margin: 10);

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
            var items = string.Join("", Enumerable.Range(0, 200).Select(i => $"<li style='font-size:10pt;line-height:20pt'>item {i} of the list</li>"));
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<ul>{items}</ul>"), pageHeight: 850, margin: 10);

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

        /// <summary>
        /// Names the words more than one fragment claims, with the slots that claim them and the slot the
        /// word's own position falls in — the three facts needed to tell #433's shape from any other.
        /// </summary>
        private static string DescribeDoubleClaims(HtmlContainerInt container)
        {
            var claims = new Dictionary<CssRect, List<int>>(ReferenceEqualityComparer.Instance);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(fragmentainer.Root).SelectMany(f => f.Words))
                {
                    if (!claims.TryGetValue(word.Word, out var slots))
                    {
                        claims[word.Word] = slots = [];
                    }

                    slots.Add(fragmentainer.SlotIndex);
                }
            }

            var doubled = claims.Where(c => c.Value.Count > 1).ToList();

            return $"{doubled.Count} words claimed more than once: " + string.Join("; ", doubled
                .Take(8)
                .Select(c => $"'{c.Key.Text}' by [{string.Join(",", c.Value)}], lives in "
                             + container.SlotStartingAt(c.Key.Top)));
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
