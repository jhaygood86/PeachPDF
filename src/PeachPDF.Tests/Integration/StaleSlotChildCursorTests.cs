using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A page emitted again after a later relocation reopened it still claims every word it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ChildrenOf</c> starts a container's child walk past a prefix of children each already
    /// confirmed <c>EmittedNothingAtOrBefore</c>, caching how many in <c>CssBox.LiveChildStartFor</c>.
    /// That answer is "this subtree's fragments are all behind slot S", which is a claim about S and
    /// every slot after it and about no slot before it — and the cache stored the layout generation
    /// but not S. <c>CatchUpStaleSlotsBehind</c> re-emits a frozen slot behind the frontier, read the
    /// cursor there, and skipped children whose content belonged to exactly that slot. Their words
    /// were then in no fragmentainer at all, leaving a gap of their own height where they had been
    /// laid out.
    /// </para>
    /// <para>
    /// The fixture is headings and paragraphs of varying length in a small page. The UA print
    /// stylesheet's <c>h1-h6 { break-after: avoid }</c> is what supplies the relocations: a heading
    /// that would be stranded at the foot of a page is taken to the next one together with the run
    /// below it, which is a §4.3 retroactive mover reaching back into a page already frozen. Which
    /// page that lands on, and how many pages the document takes, is left to whatever the platform's
    /// font measures — the assertion does not depend on it.
    /// </para>
    /// <para>
    /// <b>Why the assertion is a word census rather than a geometry check.</b> Content loss is the
    /// symptom, and it is the one statement about this that is true whatever the pagination:
    /// wherever the pages fall, every word the document laid out has to be on one of them. Stated as
    /// a page number or a page count it would say something different on every platform — the same
    /// trap <see cref="UnreachedWordClaimTests"/> avoids for the same reason, and whose invariant
    /// this is a one-sided form of (a repeated running element genuinely is claimed more than once,
    /// so this asks only that nothing is claimed less than once).
    /// </para>
    /// </remarks>
    public class StaleSlotChildCursorTests
    {
        private const double PageWidth = 300;
        private const double PageHeight = 200;
        private const double Margin = 20;

        /// <summary>
        /// Paragraph lengths, in words. Each set paginates differently; the four here were drawn at
        /// random and kept because they space the section boundaries differently against the page
        /// boundary, which is what decides whether a heading is stranded and a relocation happens at
        /// all. The first three lose words against a build without the fix on this machine; the
        /// fourth does not, and is here as the case where nothing is disturbed.
        /// </summary>
        public static TheoryData<int[]> ParagraphLengths => new()
        {
            new[] { 23, 16, 31, 38, 12, 8, 38, 24, 22, 20, 38, 38, 33, 17 },
            new[] { 16, 12, 24, 15, 39, 36, 38, 32, 21, 14, 39, 9, 32, 35 },
            new[] { 10, 35, 38, 8, 21, 37, 39, 25, 18, 10, 39, 28, 12, 23 },
            new[] { 36, 37, 36, 40, 20, 19, 40, 38, 19, 14, 36, 27, 17, 13 },
        };

        [Theory]
        [MemberData(nameof(ParagraphLengths))]
        public async Task EveryWordLaidOut_IsClaimedByAFragmentainer(int[] lengths)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document(lengths), pageWidth: PageWidth, pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 2,
                "the fixture must span enough pages for one to be frozen and reopened");

            var claimed = ClaimedWords(container);
            var missing = WordsIn(root).Where(w => !claimed.Contains(w)).ToList();

            Assert.True(missing.Count == 0,
                $"{missing.Count} words are in no fragmentainer at all, starting with "
                + string.Join(" ", missing.Take(8).Select(w => $"'{w.Text}'")));
        }

        /// <summary>
        /// Sections of a heading and a paragraph. Every word is distinct, so a missing one names
        /// itself.
        /// </summary>
        private static string Document(int[] lengths) =>
            LayoutHarness.Wrap(
                "<div>"
                + string.Concat(lengths.Select((words, i) =>
                    $"<h3 style='font-size:12pt;margin:24pt 0 12pt 0'>Heading{i}</h3>"
                    + $"<p style='margin:0 0 16pt 0;font-size:10pt'>"
                    + string.Join(" ", Enumerable.Range(0, words).Select(j => $"s{i}w{j}"))
                    + "</p>"))
                + "</div>");

        private static List<CssRect> WordsIn(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

        private static HashSet<CssRect> ClaimedWords(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToHashSet<CssRect>(ReferenceEqualityComparer.Instance);

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }
    }
}
