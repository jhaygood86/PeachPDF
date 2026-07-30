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
    /// <c>MonolithicContent.FitsNoFragmentainer</c> is the one production mechanism left that leaves a
    /// line straddling by more than <see cref="HtmlContainerInt.PageBoundaryEpsilon"/>: content taller
    /// than the whole band has nowhere to go, so layout leaves it exactly where it is. It is unrelated to
    /// any specific engine — plain block flow, flex and grid all reach it the same way.
    /// </para>
    /// <para>
    /// A flex or grid item's own content used to be a second mechanism — laid out with
    /// <c>HtmlContainerInt.SuppressWordPageBreaks</c> set and never revisited once the engine translated
    /// the item into place — but every shape now has a live commit pass (issues #430/#517/#526):
    /// <c>CssLayoutEngineGrid</c> for every row, <c>CssLayoutEngineFlex</c> for every row/row-reverse line
    /// (single or wrapped into several) and for every column/column-reverse line's items in sequence. A
    /// line that would have straddled a page boundary under the old translate-only path now genuinely
    /// continues onto the next page instead of drawing twice.
    /// </para>
    /// </remarks>
    public class StraddlingLineClaimTests
    {
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
        /// Every shape whose commit pass applies — grid rows, row/row-reverse flex lines (single or
        /// wrapped), and column/column-reverse flex lines' sequential items — no longer straddles a page
        /// boundary: each engine's commit pass revisits the relevant content live and lets what would have
        /// straddled genuinely continue on the next page instead. Every word is claimed by exactly one
        /// page, and the one row/line whose content the boundary falls through is split across both pages
        /// rather than drawn twice or lost.
        /// </summary>
        [Theory]
        [InlineData("display:grid;grid-template-columns:1fr", true)]
        [InlineData("display:flex;flex-wrap:wrap", true)]
        [InlineData("display:flex;flex-direction:column", false)]
        public async Task ARowOrLineTheEngineCouldNotFit_ContinuesOnTheNextPageInstead(
            string containerStyle, bool itemsAreFullWidth)
        {
            // width:100% keeps a wrapped flex line to one item each, matching one row's worth of content
            // per grid line - harmless for grid, which lays each row out in its own single column anyway.
            // A column-direction container already stacks its items sequentially without it.
            var itemStyle = itemsAreFullWidth ? "width:100%;font-size:10pt;line-height:13pt" : "font-size:10pt;line-height:13pt";
            var items = string.Join("", Enumerable.Range(0, 120).Select(i =>
                $"<div style='{itemStyle}'>w{i * 3} w{i * 3 + 1} w{i * 3 + 2}</div>"));
            var markup = $"<div style='{containerStyle}'>{items}</div>";

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
