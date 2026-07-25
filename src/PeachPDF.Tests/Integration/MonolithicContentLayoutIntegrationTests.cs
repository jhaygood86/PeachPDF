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

        // Characterization of a boundary the headline test's own fixture hides. Where the box was already
        // split by a *word-level* break before the epilogue ran, the mover shifts it uniformly - but the
        // resumed lines were laid out at the next band's top rather than continuing from the previous
        // page's last line, so the shift carries that fragmentainer gap along as a hole inside the box,
        // and leaves its height inflated by the same amount. 140pt of filler happens to be the one
        // alignment in this range where the gap is zero; 130 and 150 are not.
        //
        // Pre-existing, and not introduced here: `break-inside: avoid` reproduces it identically, and did
        // so before monolithic content reached this mover. Recorded as an accepted gap rather than fixed,
        // since fixing it means re-flowing the relocated box rather than translating it.
        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedBox_MatchesWhatBreakInsideAvoidAlreadyDoes(double fillerHeight)
        {
            var (monolithic, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "overflow:hidden"), pageHeight: PageHeight, margin: Margin);
            var (avoid, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(monolithic, "card")!;
            var b = LayoutHarness.FindById(avoid, "card")!;

            Assert.Equal(b.Location.Y, a.Location.Y, 6);
            Assert.Equal(b.ActualBottom, a.ActualBottom, 6);
            Assert.Equal(
                b.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top),
                a.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top));
        }

        private static string GapDocument(double fillerHeight, string cardCss) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>" +
                $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:20pt;font-size:10pt;width:60pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

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

        // A display:none box is never placed: LayoutContents copies its *previous sibling's* Location and
        // ActualBottom instead. Measuring "its" height therefore measures the sibling, and moving it moves
        // coordinates that belong to something else - which inflated the document by a whole page, since
        // ActualSize.Height bounds the fragment builder's slot walk. A hidden panel with overflow: hidden
        // is an ordinary modal or accordion body, not an exotic shape.
        [Fact]
        public async Task HiddenScrollContainer_IsNotRelocated()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:20pt'>s</div>" +
                "<div id='tall' style='height:150pt'>tall</div>" +
                "<div id='ghost' style='display:none;overflow:hidden'></div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var tall = LayoutHarness.FindById(root, "tall")!;
            var ghost = LayoutHarness.FindById(root, "ghost")!;

            // The fixture's point: #tall really does straddle, so the mover is live on this document.
            Assert.True(
                container.PageIndexOf(tall.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon)
                > container.PageIndexOf(tall.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));

            // Untouched: the else branch's copy of its previous sibling's coordinates, exactly as before.
            Assert.Equal(tall.Location.Y, ghost.Location.Y, 6);
            Assert.Equal(tall.ActualBottom, ghost.ActualBottom, 6);

            // And the document is not a page taller than its content, which is what the relocation cost.
            Assert.Equal(tall.ActualBottom - Margin, container.ActualSize.Height, 6);
        }

        // A box exactly as tall as the content band fits a page perfectly, so there is somewhere to move it
        // to. The fits-nowhere exclusion asked ">= the nominal page height" against the wrong band and
        // wrong boundary, and left such a box straddling.
        [Fact]
        public async Task ScrollContainerExactlyAsTallAsTheBand_StillMovesWhole()
        {
            var band = PageHeight - 2 * Margin;
            var html = LayoutHarness.Wrap(
                "<div style='height:60pt'>filler</div>" +
                $"<div id='card' style='overflow:hidden;height:{band}pt'>card</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon));
        }

        // The replaced half of §2 reaches the same outcome by a different route: an <img> is forced inline
        // (DomParser.CorrectReplacedElementBoxes), so it never runs the epilogue's mover at all - its whole
        // word is relocated by CssRect.BreakPage instead. Worth pinning, because the predicate's
        // CssBoxImage/CssBoxSvg arms are unreachable from the mover and it would be easy to read that as
        // the rule not being delivered.
        [Fact]
        public async Task StraddlingImage_MovesWholeThroughTheWordPath()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                "<p id='p' style='margin:0;orphans:1;widows:1'><img src='data:image/gif;base64," +
                "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==' " +
                "style='width:20pt;height:60pt'></p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            // The word belongs to the <img>'s own box, not to the paragraph that flows it.
            var word = LayoutHarness.Descendants(root).SelectMany(b => b.Words).Single(w => w.IsImage);

            Assert.Equal(
                container.PageIndexOf(word.Top + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(word.Bottom - HtmlContainerInt.PageBoundaryEpsilon));
        }

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
