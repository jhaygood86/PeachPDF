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

        // The two arms share a mover, so they must agree exactly however the box came to straddle -
        // including where it was already split by a *word-level* break before the epilogue ran, which
        // the headline test's own fixture hides. 140pt of filler is the one alignment in this range
        // where the two ways of relocating a box happened to agree anyway, which is why it must not be
        // the only case here.
        //
        // This was a characterization while the mover translated the box: the shift carried the
        // fragmentainer gap along inside it. The box is now laid out again at its new position
        // (EarlyBreak), so the equality below is the invariant rather than a shared defect - see
        // EarlyBreakLayoutIntegrationTests for what each arm now produces.
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

            // Both arms relocate the box whole, and neither carries a page gap into it: every line sits
            // one line-height below the last, and the box is exactly as tall as its own content.
            var lineTops = b.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top).Distinct().Order().ToList();

            Assert.All(
                lineTops.Zip(lineTops.Skip(1), (previous, next) => next - previous),
                spacing => Assert.True(spacing <= 20 + 0.5, $"a {spacing:F1}pt step between lines exceeds the 20pt line height"));
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

        /// <summary>
        /// A scroll container too tall for any band, with nothing inside it to fragment, overflows in
        /// place exactly as the word-level case does (<c>MonolithicContent.FitsNoFragmentainer</c>'s
        /// block-axis counterpart) - and content after it has to see a truthful pass cursor, or a later
        /// fragmentation question is answered against a stale band (issue #435). Measured as
        /// <see cref="HtmlContainerInt.CursorSpills"/> staying zero: <c>CssBox.LayoutBlockChildren</c>
        /// steps the cursor once such a child finishes, gated on
        /// <see cref="MonolithicContent.IsMonolithic"/> specifically - an earlier, broader version that
        /// matched every finished block child regressed
        /// <c>FragmentEmitterTests.MaterializedPages_MatchThePrePagedFragmentTreeBehaviour</c>'s
        /// margin-truncation fixture, since an ordinary container (html/body/div) can land deep in the
        /// document through no decision of its own.
        /// </summary>
        [Fact]
        public async Task ContentAfterAScrollContainerTallerThanTheBand_SeesATruthfulCursor()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:100pt'>filler</div>" +
                "<div id='card' style='overflow:hidden;height:900pt'></div>" +
                "<p>content after the oversized scroll container</p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.True(card.ActualBottom - card.Location.Y > PageHeight - 2 * Margin,
                "the fixture must be taller than a whole band, or this asserts nothing");
            Assert.Equal(0, container.CursorSpills);
        }

        // The replaced half of §2 reaches the same outcome by a different route: an <img> is forced inline
        // (DomParser.CorrectReplacedElementBoxes), so it never runs the epilogue's mover at all - its whole
        // word moves through the ordinary per-word fragmentainer check instead
        // (CssRect.WouldStraddleFragmentainer/InlineBreakToken, in CssLayoutEngine.FlowBox), the same path
        // any other word takes. Worth pinning, because the predicate's CssBoxImage/CssBoxSvg arms are
        // unreachable from the mover and it would be easy to read that as the rule not being delivered.
        [Fact]
        public async Task StraddlingImage_MovesWholeThroughTheWordPath()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<p id='p' style='margin:0;orphans:1;widows:1'><img src='{RasterPngFixture.OnePixelDataUri}' " +
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
        [InlineData("<img id='t' src='" + RasterPngFixture.OnePixelDataUri + "' style='width:10pt;height:10pt'>", true)]
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
