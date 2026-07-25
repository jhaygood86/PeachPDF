using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A box relocated by one of <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">CSS
    /// Fragmentation Level 3 §4.3</see>'s corrections is laid out again at its new position rather than
    /// translated to it.
    /// </summary>
    /// <remarks>
    /// The distinction is invisible for a box that had not yet reached the page boundary, and decisive
    /// for one that had: its later lines were laid out against the next band's top, so translating the
    /// box carries that fragmentainer gap along inside it — as blank space between two lines, and as
    /// height the box does not use.
    /// <para>
    /// Fixtures use a 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>. <c>orphans</c>/<c>widows</c> are pinned to 1 wherever the box
    /// under test is not the one being tested for them, since the default of 2 relocates a straddling
    /// two-line box on its own and would mask what the other arms do.
    /// </para>
    /// </remarks>
    public class EarlyBreakLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;
        private const double LineHeight = 20;

        // The headline: a box that had already flowed lines onto the next page before the decision was
        // taken. 130 and 150 straddle; 140 is the alignment where the carried gap happened to be zero,
        // which is exactly why it must not be the only case tested.
        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedBox_HasNoInteriorGap(double fillerHeight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedMonolithicBox_HasNoInteriorGap(double fillerHeight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "overflow:hidden"), pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        // A translated box keeps the gap as height it does not use, so its height depends on where it
        // happened to straddle. Laid out again, it is the height of its own content wherever it lands.
        [Fact]
        public async Task RelocatedBox_IsNoTallerThanTheSameBoxThatNeverMoved()
        {
            var (undisturbed, _) = await LayoutHarness.LayoutAsync(
                GapDocument(0, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);
            var settled = HeightOfCard(undisturbed);

            foreach (var filler in new double[] { 130, 140, 150 })
            {
                var (root, _) = await LayoutHarness.LayoutAsync(
                    GapDocument(filler, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

                Assert.Equal(settled, HeightOfCard(root), 6);
            }
        }

        // Relocation must not cost a fragmentainer pass: the decision is taken and acted on inside the
        // pass that discovered it, which is what §4.3 sanctions.
        [Fact]
        public async Task RelocatingABox_TakesNoExtraFragmentainerPass()
        {
            var (_, moved) = await LayoutHarness.LayoutAsync(
                GapDocument(130, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);
            var (_, unmoved) = await LayoutHarness.LayoutAsync(
                GapDocument(130, ""), pageHeight: PageHeight, margin: Margin);

            Assert.Equal(unmoved.FragmentainerPasses, moved.FragmentainerPasses);
        }

        // The latch. Without it the relocated box's own epilogue asks the same question of the same
        // geometry, and - since an unsatisfiable avoid is relaxed rather than skipped (§5.3) - answers
        // "still does not fit", walking the box down the document one page per pass.
        [Fact]
        public async Task BoxTallerThanTheBand_MovesAtMostOnce()
        {
            var lines = string.Join("", Enumerable.Range(0, 14).Select(i => $"Line {i}<br>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:130pt'>filler</div>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt'>{lines}</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            // Taller than a band, so it cannot be made to fit; it must still land within one page of
            // where it started rather than being pushed indefinitely.
            Assert.True(card.ActualBottom - card.Location.Y > container.PageBandHeightOf(0),
                "fixture must be taller than one band for this to test relaxation");
            Assert.True(container.PageIndexOf(card.Location.Y) <= 1,
                $"a box that fits nowhere must not walk down the document, but landed at y={card.Location.Y:F1}");
        }

        // orphans/widows reaches the same mechanism, so it gets the same guarantee.
        [Fact]
        public async Task OrphansPushedParagraph_HasNoInteriorGap()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:145pt'>filler</div>"
                + $"<div id='card' style='orphans:3;widows:3;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        // The keep-with-next pull is the one correction a box cannot carry out for itself: the break
        // falls before a sibling placed before it, so only the parent's child loop can re-run it.
        // Whichever way it is carried out, the heading comes along and lands at the destination band's
        // top with the box below it.
        [Theory]
        [InlineData(105)]
        [InlineData(115)]
        [InlineData(125)]
        public async Task PulledRun_MovesTogetherToTheDestinationBandTop(double fillerHeight)
        {
            var (heading, card, container) = await PulledRunAsync(fillerHeight);

            // The UA print stylesheet's h1-h6 { page-break-after: avoid } is what chains the two.
            var headingPage = container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(headingPage, container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
            Assert.True(heading.ActualBottom <= card.Location.Y + 1.0,
                "the heading must still sit above the box it is chained to");
            Assert.Equal(container.PageTopOf(headingPage), heading.Location.Y, 6);
        }

        // Where the run is still part of the pass being laid out, it is re-run rather than moved, so
        // the box that pulled it re-flows at its new position like any other relocated box.
        [Theory]
        [InlineData(105)]
        [InlineData(125)]
        public async Task PulledRun_IsLaidOutAgain_SoTheBoxHasNoInteriorGap(double fillerHeight)
        {
            var (_, card, _) = await PulledRunAsync(fillerHeight);

            AssertLinesAreEvenlySpaced(card);
        }

        /// <summary>
        /// The boundary of the run pull: once the pass that placed the run has been left behind, the run
        /// belongs to a fragmentainer that is already filled and cannot be re-run, so the correction
        /// degrades to moving the group instead — carrying the page gap into the box, as every one of
        /// these corrections used to.
        /// </summary>
        /// <remarks>
        /// A structural condition, not a geometric one: the run head's index lies below the index this
        /// pass resumed at. At 115pt of filler the box's own first line fits on the page and the rest
        /// does not, so its content breaks and it completes on a later pass — by which time the heading
        /// above it is settled. Characterized rather than fixed: re-running it needs the driver to
        /// withdraw a fragmentainer it has already filled.
        /// </remarks>
        [Fact]
        public async Task PulledRun_FromAResumedPass_MovesTheGroupInsteadOfReLayingItOut()
        {
            var (heading, card, container) = await PulledRunAsync(115);

            Assert.Equal(
                container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));

            var tops = card.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top).Distinct().Order().ToList();
            var steps = tops.Zip(tops.Skip(1), (previous, next) => next - previous).ToList();

            Assert.Contains(steps, step => step > LineHeight + 0.5);
        }

        private static async Task<(CssBox Heading, CssBox Card, HtmlContainerInt Container)> PulledRunAsync(double fillerHeight)
        {
            var html = LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<h2 id='heading' style='margin:0;font-size:10pt;line-height:20pt'>Heading</h2>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            return (LayoutHarness.FindById(root, "heading")!, LayoutHarness.FindById(root, "card")!, container);
        }

        /// <summary>
        /// The gap a translation carries shows up as one line sitting further below its predecessor than
        /// the line height accounts for.
        /// </summary>
        private static void AssertLinesAreEvenlySpaced(CssBox card)
        {
            var tops = card.LineBoxes
                .SelectMany(l => l.Words)
                .Select(w => Math.Round(w.Top, 3))
                .Distinct()
                .Order()
                .ToList();

            Assert.True(tops.Count > 1, "fixture must produce more than one line for spacing to mean anything");

            for (var i = 1; i < tops.Count; i++)
            {
                Assert.True(tops[i] - tops[i - 1] <= LineHeight + 0.5,
                    $"line {i} sits {tops[i] - tops[i - 1]:F1}pt below its predecessor, "
                    + $"more than the {LineHeight}pt line height - a fragmentainer gap carried inside the box");
            }
        }

        private static double HeightOfCard(CssBox root)
        {
            var card = LayoutHarness.FindById(root, "card")!;
            return Math.Round(card.ActualBottom - card.Location.Y, 6);
        }

        private static string GapDocument(double fillerHeight, string cardCss) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");
    }
}
