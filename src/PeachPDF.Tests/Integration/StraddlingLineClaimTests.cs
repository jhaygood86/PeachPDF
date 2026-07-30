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
    /// A line layout <i>could not</i> move off a page boundary is claimed by both pages, and must be
    /// (issue #477). It is the counter-case to the single-claim rule
    /// <see cref="BandMembershipToleranceTests"/> covers: there layout answered "it fits" and kept the line
    /// deliberately, so the line is wholly the earlier page's; here layout never had the chance to answer,
    /// so the line genuinely spans the boundary and the later page's copy is the only thing that renders
    /// its remainder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two production mechanisms leave a line straddling by more than
    /// <see cref="HtmlContainerInt.PageBoundaryEpsilon"/>, and both are exercised here rather than
    /// simulated: a flex item's content whose commit pass does not apply — today, any
    /// <c>flex-wrap</c> container with more than one line, or a <c>flex-direction: column</c>/
    /// <c>column-reverse</c> container — is laid out with <c>HtmlContainerInt.SuppressWordPageBreaks</c>
    /// set and is never revisited when the engine translates the item into place, and
    /// <c>MonolithicContent.FitsNoFragmentainer</c> leaves anything taller than the band exactly where
    /// it is.
    /// </para>
    /// <para>
    /// A grid item's content no longer exhibits the first mechanism: <c>CssLayoutEngineGrid</c>'s own
    /// commit pass (issues #517/#526) revisits every row's content live once it sits at its final
    /// position, the same way flex's single-line commit pass already did — so a grid line that would
    /// have straddled a page boundary under the old translate-only path now genuinely continues onto
    /// the next page instead of drawing twice. Only the flex shapes above (and monolithic content,
    /// which is unrelated to either engine) still straddle.
    /// </para>
    /// <para>
    /// The fixtures assert the overhang they produce is real — comfortably past the tolerance — before
    /// asserting anything about claims, so they cannot quietly decay into the ordinary single-claim case if
    /// the geometry shifts.
    /// </para>
    /// </remarks>
    public class StraddlingLineClaimTests
    {
        /// <summary>
        /// The invariant, stated where it differs from the single-claim one: a line that overhangs its band
        /// by more than the tolerance is held by the page it starts on <b>and</b> the page it runs into.
        /// </summary>
        [Fact]
        public async Task ALineTheEngineCouldNotMove_IsClaimedByBothPagesItSpans()
        {
            var (root, container) = await LayoutAsync();

            var straddling = StraddlingWords(container, root).ToList();

            Assert.NotEmpty(straddling);

            foreach (var word in straddling)
            {
                var band = container.SlotStartingAt(word.Top);
                var overhang = word.Bottom - container.PageBottomOf(band);

                // The fixture is only meaningful if layout really did leave the line across the boundary,
                // rather than within the window where it decided the line fits.
                Assert.True(overhang > HtmlContainerInt.PageBoundaryEpsilon,
                    $"'{word.Text}' overhangs by only {overhang}, which layout would have tolerated");

                Assert.Equal([band, band + 1], SlotsClaiming(container, word));
            }
        }

        /// <summary>
        /// The same thing said about rendering rather than about the tree: the page below the boundary draws
        /// the words of the straddling line, which is where its readable remainder is. Dropping the second
        /// claim left only the clipped sliver on the page above and lost the line.
        /// </summary>
        [Fact]
        public async Task ThePageBelowTheBoundary_DrawsTheStraddlingLinesWords()
        {
            var (root, container) = await LayoutAsync();

            var straddling = StraddlingWords(container, root).ToList();
            Assert.NotEmpty(straddling);

            var word = straddling[0];
            var below = container.SlotStartingAt(word.Top) + 1;

            var drawnBelow = container.FragmentTree!.Fragmentainers
                .Single(f => f.SlotIndex == below);

            var texts = Flatten(drawnBelow.Root).SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();

            Assert.Contains(word.Text, texts);
        }

        /// <summary>
        /// A word taller than the whole band — <c>MonolithicContent.FitsNoFragmentainer</c>'s case. No
        /// fragmentainer can hold it, so breaking to a fresh one would only repeat the problem forever;
        /// layout leaves it where it is and it covers more than one band by construction.
        /// </summary>
        /// <remarks>
        /// The word's own rectangle is what straddles here, and its height comes from the font rather than
        /// from <c>line-height</c> — hence an enormous <c>font-size</c> rather than an enormous leading,
        /// which would grow the line box while leaving the word small enough to fit.
        /// </remarks>
        [Fact]
        public async Task AWordTallerThanTheBand_IsClaimedByEveryBandItCovers()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<p style='font-size:1800pt;line-height:1;margin:0'>T</p>"),
                pageHeight: 842, margin: 10);

            var word = LayoutHarness.Descendants(root).SelectMany(b => b.Words).Single(w => w.Text == "T");
            var band = container.SlotStartingAt(word.Top);

            Assert.True(word.Height > container.PageBottomOf(band) - container.PageTopOf(band),
                $"the fixture must produce a word taller than the band, not {word.Height}");

            // Every band the word covers, from the grid's own coordinates — "claimed by band + 1" alone
            // would still pass if a taller word silently lost the bands below its second.
            var covered = container.FragmentTree!.Fragmentainers
                .Select(f => f.SlotIndex)
                .Where(slot => word.Bottom > container.PageTopOf(slot)
                               && word.Top < container.PageBottomOf(slot))
                .ToList();

            Assert.True(covered.Count > 2, $"the fixture must span more than two bands, not {covered.Count}");
            Assert.Equal(covered, SlotsClaiming(container, word));
        }

        /// <summary>
        /// The grid counterpart of <see cref="ALineTheEngineCouldNotMove_IsClaimedByBothPagesItSpans"/>:
        /// the exact same fixture shape that straddles for flex (each row its own line, one row landing
        /// across a page boundary) does <b>not</b> straddle for grid, because
        /// <c>CssLayoutEngineGrid</c>'s commit pass revisits every row's content live and lets a row that
        /// would have straddled genuinely continue on the next page instead. Every word is claimed by
        /// exactly one page, and the one row whose content the boundary falls through is split across
        /// both pages rather than drawn twice or lost.
        /// </summary>
        [Fact]
        public async Task AGridRowTheEngineCouldNotFit_ContinuesOnTheNextPageInstead()
        {
            var items = string.Join("", Enumerable.Range(0, 120).Select(i =>
                $"<div style='font-size:10pt;line-height:13pt'>w{i * 3} w{i * 3 + 1} w{i * 3 + 2}</div>"));
            var markup = $"<div style='display:grid;grid-template-columns:1fr'>{items}</div>";

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup), pageHeight: 842, margin: 10);

            var words = LayoutHarness.Descendants(root).SelectMany(b => b.Words).ToList();
            Assert.NotEmpty(words);

            // The fixture is only meaningful if content genuinely reached a page boundary - otherwise
            // "nothing straddles" would pass vacuously without exercising the commit pass's live break.
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page to exercise a boundary at all");

            Assert.All(words, word =>
                Assert.True(word.Bottom <= container.PageBottomOf(container.SlotStartingAt(word.Top)) + HtmlContainerInt.PageBoundaryEpsilon,
                    $"'{word.Text}' still straddles its band by {word.Bottom - container.PageBottomOf(container.SlotStartingAt(word.Top))}"));

            Assert.All(words, word => Assert.Single(SlotsClaiming(container, word)));
        }

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync()
        {
            // width:100% on a flex item makes each item its own line, so the items stack down the page and
            // one of them lands across the boundary. line-height is pinned so the stack's pitch does not
            // depend on the platform's font metrics. flex-wrap:wrap gives the container more than one
            // line, which is outside CssLayoutEngineFlex.CommitItemContent's single-line scope.
            var items = string.Join("", Enumerable.Range(0, 120).Select(i =>
                $"<div style='width:100%;font-size:10pt;line-height:13pt'>w{i * 3} w{i * 3 + 1} w{i * 3 + 2}</div>"));

            var markup = $"<div style='display:flex;flex-wrap:wrap'>{items}</div>";

            return LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup), pageHeight: 842, margin: 10);
        }

        /// <summary>Every word left crossing the bottom of the band its own top starts in.</summary>
        private static IEnumerable<CssRect> StraddlingWords(HtmlContainerInt container, CssBox root) =>
            LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words)
                .Where(w => w.Bottom > container.PageBottomOf(container.SlotStartingAt(w.Top)));

        private static List<int> SlotsClaiming(HtmlContainerInt container, CssRect word) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).SelectMany(b => b.Words).Any(w => ReferenceEquals(w.Word, word)))
                .Select(f => f.SlotIndex)
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
