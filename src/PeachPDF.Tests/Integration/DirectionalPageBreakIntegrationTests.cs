using PeachPDF;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-break-3 §3.1's directional forced breaks: <c>break-before</c>/<c>break-after</c> with
    /// <c>left</c>, <c>right</c>, <c>recto</c> or <c>verso</c> force one <i>or two</i> page breaks, so
    /// the content after the break begins on a page of the requested side. Pages alternate from a
    /// right-hand first page, the same parity <c>@page :left</c>/<c>:right</c> select on, and the slot
    /// stepped over becomes a real blank page.
    /// </summary>
    public class DirectionalPageBreakIntegrationTests
    {
        private const double PageHeight = 300;

        // A narrow page (A5's width) so a paragraph wraps to enough lines for a chapter to span pages
        // without an enormous fixture; the wide default page makes the same shape pass by luck of where
        // the last line of the first page falls.
        private const double A5Width = 419.53;

        // Two short blocks: without a break they share slot 0, so whatever slot the second one ends up
        // in is entirely the doing of the declaration under test.
        private static string Document(string declaration, string firstStyle = "") =>
            LayoutHarness.Wrap(
                $"<div id='first' style='height:50pt;{firstStyle}'>first</div>"
                + $"<div id='second' style='height:50pt;{declaration}'>second</div>");

        private static int SlotOf(HtmlContainerInt container, CssBox box) =>
            container.PageIndexOf(box.Location.Y + 0.5);

        // From slot 0 (a right page), a break onto a left page is the plain next slot, while a break
        // onto a right page has to step over slot 1 - which is what manufactures the blank page.
        [Theory]
        [InlineData("break-before: left", 1)]
        [InlineData("break-before: verso", 1)]
        [InlineData("break-before: right", 2)]
        [InlineData("break-before: recto", 2)]
        [InlineData("page-break-before: left", 1)]
        [InlineData("page-break-before: right", 2)]
        public async Task DirectionalBreakBefore_LandsOnTheRequiredSide(string declaration, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document(declaration), pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);

            var slot = SlotOf(container, second!);
            Assert.Equal(expectedSlot, slot);

            // ... and the page it actually landed on is one the matching @page selector would style.
            var wantsRight = declaration.Contains("right") || declaration.Contains("recto");
            Assert.Equal(wantsRight, PageRuleResolver.IsRightPage(slot + 1));
        }

        // The same break stated on the trailing side of the earlier box.
        [Theory]
        [InlineData("break-after: left", 1)]
        [InlineData("break-after: recto", 2)]
        public async Task DirectionalBreakAfter_OnThePrecedingSibling_TakesTheSameBreak(string declaration, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='first' style='height:50pt;{declaration}'>first</div>"
                                   + "<div id='second' style='height:50pt'>second</div>"),
                pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(expectedSlot, SlotOf(container, second!));
        }

        [Fact]
        public async Task SkippedSlot_IsMaterializedAsAPage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Document("break-before: right"), pageHeight: PageHeight);

            Assert.Equal([0, 1, 2], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The manufactured page has to be genuinely empty. The predecessor carries a background and a
        // border precisely because the mechanism this replaced expressed the break by inflating that
        // box's height to the break target - which would have painted it straight across this page, and
        // would also have made the slot look printable and so skipped the reservation entirely.
        [Fact]
        public async Task SkippedSlot_IsGenuinelyBlank()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("break-before: right", firstStyle: "background:#0f0;border:2pt solid #000;"),
                pageHeight: PageHeight);

            var blank = container.FragmentTree!.Fragmentainers.Single(f => f.SlotIndex == 1);
            Assert.Empty(WordsIn(blank.Root));

            // The predecessor keeps its own height rather than being stretched to the break.
            var first = LayoutHarness.FindById(root, "first");
            Assert.NotNull(first);
            Assert.Equal(0, container.PageIndexOf(first!.ActualBottom - 0.5));
        }

        [Fact]
        public async Task PredecessorOfAForcedBreak_IsNotStretchedToThePageBottom()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document("break-before: page"), pageHeight: PageHeight);

            var first = LayoutHarness.FindById(root, "first");
            Assert.NotNull(first);

            // 50pt tall, so its bottom sits 50pt below the first band's top - not at the band's end.
            Assert.Equal(container.PageTopOf(0) + 50, first!.ActualBottom, 0.5);
        }

        // Already on the required side: one break, no manufactured page.
        [Fact]
        public async Task MatchingSide_TakesASinglePageBreak_AndManufacturesNoPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document("break-before: left"), pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(1, SlotOf(container, second!));
            Assert.Equal([0, 1], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The negative that keeps the rest honest: an undirected forced break asks for no side, so it
        // never manufactures a page - CSS Paged Media §3.2's content-empty-page rule still applies.
        [Fact]
        public async Task UndirectedForcedBreak_ManufacturesNoBlankPage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Document("break-before: page"), pageHeight: PageHeight);

            Assert.Equal([0, 1], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // css-break-3 §5.2 truncates a margin adjoining an *unforced* break only, so a margin after a
        // directional break survives - including across the manufactured page.
        [Fact]
        public async Task MarginAfterADirectionalBreak_IsPreserved()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("break-before: right; margin-top: 30pt"), pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(container.PageTopOf(2) + 30, second!.Location.Y, 0.5);
        }

        // The skip is one *slot*, not one PageSize.Height: under per-page band overrides the page being
        // stepped over has its own height, and the box has to land on the next band's own top.
        [Fact]
        public async Task DirectionalBreak_UnderPerPageBandOverrides_LandsOnTheNextBandsTop()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 20pt; }
                @page :left { margin-top: 60pt; }
                body { margin: 0; }
                div { margin: 0; }
                </style></head><body>
                <div id='first' style='height:50pt'>first</div>
                <div id='second' style='height:50pt; break-before: right'>second</div>
                </body></html>
                """;

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(container.PageTopOf(2), second!.Location.Y, 0.5);
        }

        // CSS 2.1 §9.4.3: a relative offset never moves following content, so it must not decide which
        // slot the break lands in either. The predecessor is deliberately tall enough that a 40pt
        // visual offset would carry its bottom over a slot boundary and change the answer - with a
        // short one the whole document stays inside slot 0 and the assertion proves nothing.
        [Theory]
        [InlineData("")]
        [InlineData("position:relative;top:40pt;")]
        public async Task RelativelyPositionedPreviousSibling_DoesNotSkewTheTarget(string offset)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='first' style='height:240pt;{offset}'>first</div>"
                                   + "<div id='second' style='height:50pt;break-before: right'>second</div>"),
                pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(2, SlotOf(container, second!));
            Assert.Equal([0, 1, 2], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // A box that never gets placed never takes the break, so it must not reserve a page for one.
        // The prologue runs for every box, including these; only PlaceBlockBox knows the break was
        // actually taken.
        [Theory]
        [InlineData("display:none")]
        [InlineData("position:absolute")]
        public async Task UnplacedBoxCarryingADirectionalBreak_ManufacturesNoPage(string declaration)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='height:50pt'>first</div>"
                                   + $"<div style='{declaration}; break-before: right'>hidden</div>"
                                   + "<div style='height:50pt'>second</div>"),
                pageHeight: PageHeight);

            Assert.Equal([0], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The step to the required side happens after the preserved margin is applied, so the box both
        // keeps its margin and opens a page of the right side - the two requirements are not traded
        // off against each other.
        [Fact]
        public async Task MarginAndSide_AreBothHonoured()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("break-before: right; margin-top: 30pt"), pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(container.PageTopOf(2) + 30, second!.Location.Y, 0.5);
            Assert.True(PageRuleResolver.IsRightPage(SlotOf(container, second) + 1));
        }

        // A margin big enough to carry the box past the slot the break reached must not leave it on
        // the wrong side: the side is checked against where the box actually lands.
        [Fact]
        public async Task MarginTallerThanABand_StillLandsOnTheRequiredSide()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("break-before: right; margin-top: 300pt"), pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.True(PageRuleResolver.IsRightPage(SlotOf(container, second!) + 1),
                $"box landed on slot {SlotOf(container, second)}, which is not a right page");
        }

        // Both reservations belong to the same box here - a break-before that steps over a page and a
        // trailing break-after that pads one. Keyed per box, the second would silently replace the first.
        [Fact]
        public async Task BreakBeforeAndTrailingBreakAfter_OnOneBox_BothReservePages()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='height:50pt'>first</div>"
                                   + "<div style='height:50pt; break-before: right; break-after: recto'>second</div>"),
                pageHeight: PageHeight);

            Assert.Equal([0, 1, 2, 3], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The empty-marker idiom: "<div class='page-break'></div>" carries the break and nothing else.
        // Such a marker is self-collapsing, so margin collapsing would ordinarily resolve the section
        // after it against the section before it and undo the break - which is what the old mechanism's
        // stretching of the predecessor accidentally papered over. A box placed by a forced break
        // anchors what follows it instead.
        [Theory]
        [InlineData("break-before: page", 1)]
        [InlineData("break-before: recto", 2)]
        public async Task EmptyMarkerCarryingTheBreak_StillPushesTheFollowingSection(string declaration, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='first' style='height:50pt'>first</div>"
                                   + $"<div style='{declaration}'></div>"
                                   + "<div id='second' style='height:50pt'>second</div>"),
                pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(expectedSlot, SlotOf(container, second!));
        }

        // And it lands at the new page's content top plus its own collapsed top margin: §5.2 truncates
        // a margin adjoining an *unforced* break only, so the margin after a forced one is kept.
        [Fact]
        public async Task SectionAfterAnEmptyMarker_KeepsItsOwnTopMargin()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='height:50pt'>first</div>"
                                   + "<div style='break-before: page'></div>"
                                   + "<div id='second' style='margin-top:18pt;height:50pt'>second</div>"),
                pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(container.PageTopOf(1) + 18, second!.Location.Y, 0.5);
        }

        // A directional break-after on the last box of the flow pads the document, so the page that
        // would follow it falls on the requested side - the book idiom, where a volume ends such that
        // the next one opens recto. Content ends on slot 0 (a right page), so the next page is slot 1,
        // a left page: asking for recto needs a blank page, asking for verso already has it.
        [Theory]
        [InlineData("break-after: recto", new[] { 0, 1 })]
        [InlineData("break-after: right", new[] { 0, 1 })]
        [InlineData("break-after: verso", new[] { 0 })]
        [InlineData("break-after: left", new[] { 0 })]
        [InlineData("break-after: page", new[] { 0 })]
        public async Task TrailingDirectionalBreakAfter_PadsTheDocument(string declaration, int[] expectedSlots)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='only' style='height:50pt;{declaration}'>only</div>"),
                pageHeight: PageHeight);

            Assert.Equal(expectedSlots, container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The padding page is real in the PDF, and the trailing reservation sits past everything the
        // document laid out - so the builder's slot walk has to look beyond the document height.
        [Fact]
        public async Task TrailingDirectionalBreakAfter_ProducesARealFinalPage()
        {
            var pdf = await RenderAsync(
                """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <div style='height:50pt; break-after: recto'>only</div>
                </body></html>
                """);

            Assert.Equal(2, pdf.PdfDocument.PageCount);

            var streams = ContentStreams(pdf);
            Assert.Contains("Tj", streams[0]);
            Assert.DoesNotContain("Tj", streams[1]);
        }

        // The value is taken from the last in-flow descendant chain, so a break-after declared on a
        // wrapper whose last child carries none still pads.
        [Fact]
        public async Task TrailingDirectionalBreakAfter_OnAnAncestor_StillPads()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='break-after: recto'><p style='height:50pt'>only</p></div>"),
                pageHeight: PageHeight);

            Assert.Equal([0, 1], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // A container's first in-flow child has no predecessor for the break to be resolved against, so
        // the target comes from where the box itself would land. The wrapper is pushed off its page's
        // top by a preceding block, so the first child starts mid-band and the break has somewhere to go
        // - which is what distinguishes this from the flush case below.
        private static string FirstChildDocument(string declaration) =>
            LayoutHarness.Wrap(
                "<div id='first' style='height:50pt'>first</div>"
                + $"<div id='wrap'><div id='only' style='height:50pt;{declaration}'>only</div></div>");

        [Theory]
        [InlineData("break-before: page", 1)]
        [InlineData("page-break-before: always", 1)]
        [InlineData("break-before: left", 1)]
        [InlineData("break-before: verso", 1)]
        [InlineData("break-before: right", 2)]
        [InlineData("break-before: recto", 2)]
        public async Task ForcedBreakBefore_OnAFirstChild_IsTaken(string declaration, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                FirstChildDocument(declaration), pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);
            Assert.Equal(expectedSlot, SlotOf(container, only!));
        }

        // Nothing at all precedes this box in the flow - the climb out through its containers reaches the
        // root - so the break has nothing to break from and is not taken. That is what keeps a break on
        // the first thing in a document from manufacturing a blank page in front of it, and it holds
        // whichever side the value names, since no break means no side to satisfy.
        [Theory]
        [InlineData("break-before: page")]
        [InlineData("break-before: right")]
        [InlineData("break-before: left")]
        [InlineData("page-break-before: always")]
        public async Task ForcedBreakBefore_OnTheFirstBoxInTheFlow_TakesNoBreak(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='wrap'><div id='only' style='{declaration}'>only</div></div>"),
                pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);
            Assert.Equal(0, SlotOf(container, only!));
            Assert.Equal([0], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // §4.4's "no empty fragmentainer for a single forced break at a boundary", reached through the
        // climb: the predecessor ends flush on a slot top, so the break is already satisfied there and
        // must not manufacture a page. The filler is exactly one band tall, so it ends on PageTopOf(1).
        [Fact]
        public async Task ForcedBreakBefore_OnAFirstChildWhosePredecessorEndsOnASlotTop_ManufacturesNoPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div id='first' style='height:{PageHeight - 40}pt'>first</div>"
                    + "<div id='wrap'><div id='only' style='break-before: page'>only</div></div>"),
                pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);
            Assert.Equal(1, SlotOf(container, only!));
            Assert.Equal([0, 1], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
        }

        // The same flush predecessor, but the slot it lands in is the wrong side, so the parity walk
        // steps over it - proving the climbed anchor reaches the directional machinery and not just the
        // plain-page path.
        [Fact]
        public async Task DirectionalBreakBefore_OnAFirstChildLandingOnTheWrongSide_StepsOverTheSlot()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div id='first' style='height:{PageHeight - 40}pt'>first</div>"
                    + "<div id='wrap'><div id='only' style='break-before: right'>only</div></div>"),
                pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);

            // Slot 1 is page 2, a left page; `right` therefore steps to slot 2 and leaves slot 1 blank.
            Assert.Equal(2, SlotOf(container, only!));
            Assert.Equal([0, 1, 2], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
            Assert.Empty(WordsIn(container.FragmentTree.Fragmentainers[1].Root));
        }

        // The slot a first child's directional break steps over is reserved and materialized, exactly as
        // it is for the sibling case - the reservation is keyed by the box that takes the break, and a
        // first child is no different there.
        [Fact]
        public async Task DirectionalBreakBefore_OnAFirstChild_MaterializesTheSteppedOverSlot()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                FirstChildDocument("break-before: recto"), pageHeight: PageHeight);

            Assert.Equal([0, 1, 2], container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex));
            Assert.Empty(WordsIn(container.FragmentTree.Fragmentainers[1].Root));
        }

        // §5.2 preserves a margin adjoining a *forced* break, and a first child's own margin is no
        // exception - it is this box's margin collapsed with its adjoining first-child chain, which is
        // what MarginTopCollapse(null) computes.
        [Fact]
        public async Task MarginAfterAFirstChildsForcedBreak_IsPreserved()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                FirstChildDocument("break-before: page; margin-top: 30pt"), pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);
            Assert.Equal(container.PageTopOf(1) + 30, only!.Location.Y, 3);
        }

        // Another characterized boundary. A multi-column container fills fragmentainers of its own, so
        // a box inside one is being placed into a *column* - and which side of the sheet a page falls on
        // is not a question a column can answer. The parity step and its reservation are therefore not
        // taken, and the break degrades to a column break: the box moves to the next column, and no
        // blank page is manufactured for a side that was never resolved.
        [Fact]
        public async Task DirectionalBreakInsideMulticol_DegradesToAColumnBreak_KnownBoundary()
        {
            // A bundled font, not a system default: the fixture's own boundary is close enough that
            // which fallback font the host resolves (Arial on Windows, Liberation/DejaVu on Linux/macOS)
            // can change a word's exact metrics enough to shift which column claims it - unrelated to
            // what this test is actually about (a directional break degrading to a column break).
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<style>{BundledFonts.FontFaceRule(BundledFonts.Ttf, "TestFont", "font/truetype")}</style>" +
                    "<div style='column-count:2;font-family:TestFont'>"
                    + "<div style='height:50pt'>first</div>"
                    + "<div style='height:50pt; break-before: right'>second</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            Assert.DoesNotContain(container.FragmentTree!.Fragmentainers, f => WordsIn(f.Root).Count == 0);
        }

        [Fact]
        public async Task DirectionalBreak_ProducesARealBlankPdfPage()
        {
            var pdf = await RenderAsync(
                """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <div style='height:50pt'>first</div>
                <div style='height:50pt; break-before: recto'>second</div>
                </body></html>
                """);

            Assert.Equal(3, pdf.PdfDocument.PageCount);

            var streams = ContentStreams(pdf);
            Assert.Contains("Tj", streams[0]);
            Assert.DoesNotContain("Tj", streams[1]);
            Assert.Contains("Tj", streams[2]);
        }

        // The manufactured page is an ordinary page: it takes its @page context's margin boxes and it
        // counts toward counter(pages), which is what makes "3" appear at all.
        [Fact]
        public async Task BlankPage_StillRendersItsMarginBoxes()
        {
            var pdf = await RenderAsync(
                """
                <!DOCTYPE html><html><head><style>
                @page { @bottom-center { content: counter(page) " of " counter(pages); } }
                body { margin: 0; }
                </style></head><body>
                <div style='height:50pt'>first</div>
                <div style='height:50pt; break-before: recto'>second</div>
                </body></html>
                """);

            Assert.Equal(3, pdf.PdfDocument.PageCount);

            // The folio is drawn on the manufactured page. Text is emitted through a CID font, so the
            // glyphs are hex-encoded rather than literal digits - the show operator is the assertable
            // fact, and DirectionalBreak_ProducesARealBlankPdfPage pins that the same page carries no
            // text at all when the document declares no margin box.
            Assert.Contains("Tj", ContentStreams(pdf)[1]);
        }

        // The regression the fixtures above cannot reach: every one of them uses fixed-height blocks that
        // finish inside the page they land on, so the layout pass never ends inside the sweep that also
        // contains the reserved blank slot. Here the chapter is a paragraph far taller than a page, so the
        // pass that lays out slot 0's cover, the blank slot 1 and the chapter's first page ends with the
        // chapter still open - and its last line on that page straddles the page bottom (the half-leading
        // shift), so the pass ends with a break token naming <html>/<body>/the chapter. A box that continues
        // into the next pass has no settled bottom yet (its height is only applied in its epilogue), so the
        // emitter must not treat <html>/<body> as done for good after the empty slot 1: doing so pruned the
        // whole document from slot 2 on, silently dropping everything the chapter's first page held.
        //
        // `line-height:20pt` and `1.4` are the values that failed before the fix (the chapter's first pages
        // were dropped, slots 2 and 3 never materialized); `normal` happens to land its last line clear of
        // the page bottom on this band and passes either way - it is here as the unaffected control, not as
        // the reproduction.
        [Theory]
        [InlineData("line-height:20pt")]
        [InlineData("line-height:normal")]
        [InlineData("line-height:1.4")]
        public async Task LongChapterAfterARectoBreak_LosesNoWords(string lineHeight)
        {
            const int wordCount = 800;
            var paragraph = string.Join(' ', Enumerable.Range(1, wordCount).Select(i => $"w{i}"));

            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:50pt'>cover</div>"
                    + $"<div style='break-before: recto; font-size:10pt; margin:0; {lineHeight}'><p style='margin:0'>{paragraph}</p></div>"),
                pageHeight: PageHeight);

            var fragmentainers = container.FragmentTree!.Fragmentainers;

            // Slots stay contiguous: slot 1 is the reserved blank page and the chapter begins on slot 2, a
            // right page, and then runs on for as many pages as it needs.
            Assert.Equal(Enumerable.Range(0, fragmentainers.Count), fragmentainers.Select(f => f.SlotIndex));
            Assert.True(fragmentainers.Count >= 4, $"the chapter should span several pages, got {fragmentainers.Count} fragmentainers");
            Assert.Empty(WordsIn(fragmentainers[1].Root));

            var chapterWords = fragmentainers
                .SelectMany(f => WordsIn(f.Root))
                .Select(TextOf)
                .Where(t => t != "cover")
                .ToList();

            Assert.Equal(Enumerable.Range(1, wordCount).Select(i => $"w{i}"), chapterWords);

            var firstWordSlot = fragmentainers.Single(f => WordsIn(f.Root).Any(w => TextOf(w) == "w1")).SlotIndex;
            Assert.Equal(2, firstWordSlot);
        }

        // The showcase shape (paged_media_directional_breaks): several `break-before: recto` chapters that
        // each run past one page, closed by a trailing `break-after: recto` section. The first chapter is
        // the one that vanished (its heading and first five paragraphs), and every later chapter started on
        // the wrong parity, so this pins where each heading lands as well as that no word is missing.
        [Fact]
        public async Task RectoChapters_EachSpanningSeveralPages_StartOnRightPagesAndLoseNothing()
        {
            const int paragraphsPerChapter = 8;
            const int wordsPerParagraph = 60;

            var body = new StringBuilder("<div style='height:60pt'>cover</div>");
            var expected = new List<string> { "cover" };

            for (var chapter = 1; chapter <= 3; chapter++)
            {
                body.Append($"<section style='break-before: recto'><h2 style='margin:0 0 14pt'>Chapter{chapter}</h2>");
                expected.Add($"Chapter{chapter}");

                for (var paragraph = 1; paragraph <= paragraphsPerChapter; paragraph++)
                {
                    var words = Enumerable.Range(1, wordsPerParagraph).Select(i => $"c{chapter}p{paragraph}w{i}").ToList();
                    expected.AddRange(words);
                    body.Append($"<p style='margin:0 0 9pt'>{string.Join(' ', words)}</p>");
                }

                body.Append("</section>");
            }

            body.Append("<section style='break-after: recto'><p style='margin:0'>colophon</p></section>");
            expected.Add("colophon");

            var html =
                "<!DOCTYPE html><html><head><style>"
                + "body { font-size: 10.5pt; line-height: 1.55; margin: 0; }"
                + "</style></head><body>" + body + "</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: A5Width, pageHeight: PageHeight);
            var fragmentainers = container.FragmentTree!.Fragmentainers;

            Assert.Equal(Enumerable.Range(0, fragmentainers.Count), fragmentainers.Select(f => f.SlotIndex));

            var actual = fragmentainers.SelectMany(f => WordsIn(f.Root)).Select(TextOf).ToList();
            Assert.Equal(expected, actual);

            foreach (var heading in new[] { "Chapter1", "Chapter2", "Chapter3" })
            {
                var slot = fragmentainers.Single(f => WordsIn(f.Root).Any(w => TextOf(w) == heading)).SlotIndex;
                Assert.True(slot % 2 == 0, $"{heading} landed on slot {slot}, which is a left page");
                Assert.True(PageRuleResolver.IsRightPage(slot + 1), $"{heading} landed on slot {slot}, not a right page");
            }

            // Each chapter really does span more than one page (otherwise the fixture reproduces nothing) ...
            var lastChapterSlot = fragmentainers.Single(f => WordsIn(f.Root).Any(w => TextOf(w) == "Chapter3")).SlotIndex;
            Assert.True(lastChapterSlot >= 6, $"the chapters should each span several pages, Chapter3 is on slot {lastChapterSlot}");

            // ... and the trailing `break-after: recto` pads the document so the next page would be a right one.
            Assert.True(PageRuleResolver.IsRightPage(fragmentainers[^1].SlotIndex + 2),
                "the trailing break-after: recto should leave the following page on the right");
        }

        // The end-to-end check the two layout-level tests above stand in for: the rendered PDF carries the
        // first chapter's text at all. Before the fix the chapter's whole first page was dropped, so the
        // document had a page fewer and no glyphs for the heading.
        [Fact]
        public async Task LongChapterAfterARectoBreak_RendersItsFirstPage()
        {
            var paragraphs = string.Concat(Enumerable.Range(1, 8).Select(p =>
                $"<p>{string.Join(' ', Enumerable.Range(1, 90).Select(i => $"p{p}w{i}"))}</p>"));

            var pdf = await RenderAsync(
                "<!DOCTYPE html><html><head><style>"
                + "@page { size: A5; margin: 18mm; }"
                + "body { font-size: 10.5pt; line-height: 1.55; margin: 0; }"
                + "p { margin: 0 0 9pt; }"
                + "section { break-before: recto; }"
                + "</style></head><body>"
                + "<div style='height:60pt'>cover</div>"
                + $"<section><h2>Chapter1</h2>{paragraphs}</section>"
                + "</body></html>");

            var streams = ContentStreams(pdf);

            // Slot 0 is the cover, slot 1 the reserved blank page, and the chapter runs on from slot 2.
            Assert.True(pdf.PdfDocument.PageCount >= 4, $"expected the chapter to span several pages, got {pdf.PdfDocument.PageCount}");
            Assert.Contains("Tj", streams[0]);
            Assert.DoesNotContain("Tj", streams[1]);
            Assert.Contains("Tj", streams[2]);
            Assert.Contains("Tj", streams[3]);
        }

        private static async Task<PeachPdfDocument> RenderAsync(string html)
        {
            var generator = new PdfGenerator();
            return await generator.GeneratePdf(
                html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
        }

        private static string[] ContentStreams(PeachPdfDocument document)
        {
            var streams = new string[document.PdfDocument.PageCount];

            for (var i = 0; i < streams.Length; i++)
            {
                var contents = document.PdfDocument.Pages[i].Contents;
                var builder = new StringBuilder();

                foreach (var item in contents.Elements)
                {
                    if (item is PdfReference { Value: PdfDictionary { Stream: { } stream } })
                        builder.Append(Encoding.Latin1.GetString(stream.Value));
                }

                streams[i] = builder.ToString();
            }

            return streams;
        }

        private static string TextOf(TextFragment word) => (word.Word.Text ?? "").Trim();

        private static List<TextFragment> WordsIn(BoxFragment fragment)
        {
            var words = new List<TextFragment>(fragment.Words);

            foreach (var child in fragment.Children)
                words.AddRange(WordsIn(child));

            return words;
        }
    }
}
