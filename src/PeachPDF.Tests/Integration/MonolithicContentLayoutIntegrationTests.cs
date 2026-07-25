using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What <see href="https://www.w3.org/TR/css-break-3/#monolithic">css-break-3 §2</see>'s monolithic set
    /// does to pagination: a box the spec forbids breaking inside is moved whole to the next fragmentainer
    /// rather than being sliced across the boundary.
    /// </summary>
    /// <remarks>
    /// The fixtures use a 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>, and set <c>orphans</c>/<c>widows</c> to 1 throughout. Without that
    /// the default of 2 pushes a straddling two-line box wholesale on its own, and every assertion here
    /// would pass whatever the monolithic rule did.
    /// </remarks>
    public class MonolithicContentLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        // The headline case: a card with overflow: hidden is a scroll container, so it may not be split.
        [Fact]
        public async Task StraddlingScrollContainer_MovesWholeToTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument("overflow: hidden"), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(top, bottom);
            Assert.Equal(container.PageTopOf(top), card.Location.Y, 6);
        }

        // The control: the identical box without the declaration still straddles, so the test above is
        // measuring the rule rather than the fixture.
        [Fact]
        public async Task StraddlingVisibleBox_IsStillSplit()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(""), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "fixture must straddle a page boundary when nothing forbids it");
        }

        [Theory]
        [InlineData("overflow: scroll")]
        [InlineData("overflow: auto")]
        public async Task EveryScrollContainerValue_MovesWhole(string css)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(css), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon));
        }

        // §2 would have content that fits in no fragmentainer overflow rather than be sliced. Overflowing
        // discards every fragmentainer past the first, so PeachPDF keeps fragmenting instead - a deliberate
        // deviation, pinned here so it reads as a decision rather than an oversight.
        [Fact]
        public async Task ScrollContainerTallerThanTheBand_KeepsFragmentingRatherThanOverflowing()
        {
            var lines = string.Join("", Enumerable.Range(0, 40).Select(i => $"Line{i}<br>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:100pt'>filler</div>" +
                "<div id='card' style='overflow:hidden;orphans:1;widows:1;line-height:20pt;font-size:10pt'>" +
                lines + "</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "a box with nowhere to fit must keep fragmenting, not overflow");

            // And nothing was dropped: every line still has a fragment somewhere in the tree.
            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text)
                .Where(t => t?.StartsWith("Line") == true)
                .Distinct()
                .Count();

            Assert.Equal(40, placed);
        }

        // A fixed box is emitted in every fragmentainer at identical coordinates, so "move it to the next
        // page" names nothing for it - the mover has to leave it alone however it is styled.
        [Fact]
        public async Task FixedScrollContainer_IsNotRelocated()
        {
            var (plain, plainContainer) = await LayoutHarness.LayoutAsync(
                FixedDocument(""), pageHeight: PageHeight, margin: Margin);
            var (clipped, _) = await LayoutHarness.LayoutAsync(
                FixedDocument("overflow:hidden"), pageHeight: PageHeight, margin: Margin);

            var plainCard = LayoutHarness.FindById(plain, "card")!;
            var clippedCard = LayoutHarness.FindById(clipped, "card")!;

            Assert.True(clippedCard.IsFixed);
            Assert.True(plainContainer.FragmentTree!.Fragmentainers.Count > 1, "fixture must paginate");

            // The fixture straddles the first band's bottom edge, so an in-flow box in this position would
            // be moved. A fixed one is not: it has no next page to be moved to.
            Assert.Equal(plainCard.Location.Y, clippedCard.Location.Y, 6);
        }

        // The trailing block carries the printable content that materializes a later page: a slot no
        // fragment lands in is never materialized (CSS Paged Media 3 §3.2), so a tall but empty filler
        // paginates to nothing.
        private static string FixedDocument(string cardCss) =>
            LayoutHarness.Wrap(
                "<div style='height:400pt'>filler</div><div>tail</div>" +
                $"<div id='card' style='position:fixed;top:140pt;left:0;width:60pt;height:60pt;{cardCss}'>x</div>");

        // ── the fact on the fragment ──────────────────────────────────────────

        [Theory]
        [InlineData("<div id='t' style='overflow:hidden'>text</div>", true)]
        [InlineData("<img id='t' src='data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==' style='width:10pt;height:10pt'>", true)]
        [InlineData("<div id='t'>text</div>", false)]
        [InlineData("<div id='t' style='display:flex'><span>text</span></div>", false)]
        public async Task Fragment_CarriesWhetherItsBoxIsMonolithic(string markup, bool expected)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "t")
                .ToList();

            Assert.NotEmpty(fragments);
            Assert.All(fragments, f => Assert.Equal(expected, f.IsMonolithic));
        }

        // Every fragment of one box agrees, since this is a property of the box rather than of the piece.
        [Fact]
        public async Task EveryFragmentOfASplitBox_AgreesOnTheFact()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(""), pageHeight: PageHeight, margin: Margin);

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "card")
                .ToList();

            Assert.True(fragments.Count > 1, "fixture must produce more than one fragment");
            Assert.All(fragments, f => Assert.False(f.IsMonolithic));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// A 60pt three-line card starting at 160pt — 20pt above the first band's 180pt bottom edge — so it
        /// straddles the boundary unless something forbids it.
        /// </summary>
        private static string StraddleDocument(string cardCss) =>
            LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:20pt;font-size:10pt;width:60pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

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
