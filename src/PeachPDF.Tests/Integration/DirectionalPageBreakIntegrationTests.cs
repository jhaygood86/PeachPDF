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
            Assert.Equal(expectedSlot, SlotOf(container, second!));

            // ... and the page it landed on really is one the matching @page selector would style.
            var wantsRight = declaration.Contains("right") || declaration.Contains("recto");
            Assert.Equal(wantsRight, PageRuleResolver.IsRightPage(expectedSlot + 1));
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

        // CSS 2.1 §9.4.3: a relative offset never moves following content, so it must not move the
        // break either. The old mechanism read the predecessor's StaticBottom, which folded the offset
        // back out again by subtraction rather than never letting it in.
        [Fact]
        public async Task RelativelyPositionedPreviousSibling_DoesNotSkewTheTarget()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("break-before: right", firstStyle: "position:relative;top:20pt;"),
                pageHeight: PageHeight);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);
            Assert.Equal(container.PageTopOf(2), second!.Location.Y, 0.5);
        }

        // A known boundary, characterized rather than left silent: the break is derived from the
        // previous sibling, so a box that has none takes no break at all. Pre-existing for `page`.
        [Fact]
        public async Task DirectionalBreakBefore_OnAFirstChild_IsANoOp_KnownBoundary()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='wrap'><div id='only' style='break-before: right'>only</div></div>"),
                pageHeight: PageHeight);

            var only = LayoutHarness.FindById(root, "only");
            Assert.NotNull(only);
            Assert.Equal(0, SlotOf(container, only!));
        }

        // Another characterized boundary: inside content an engine paginates itself, this box's
        // coordinates during layout are provisional, so the parity step (and its reservation) is not
        // taken and the break degrades to a plain page break.
        [Fact]
        public async Task DirectionalBreakInsideMulticol_DegradesToAPlainPageBreak_KnownBoundary()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='column-count:2'>"
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
                        builder.Append(Encoding.Latin1.GetString(stream.UnfilteredValue));
                }

                streams[i] = builder.ToString();
            }

            return streams;
        }

        private static List<TextFragment> WordsIn(BoxFragment fragment)
        {
            var words = new List<TextFragment>(fragment.Words);

            foreach (var child in fragment.Children)
                words.AddRange(WordsIn(child));

            return words;
        }
    }
}
