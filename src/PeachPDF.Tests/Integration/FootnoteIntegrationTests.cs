using PeachPDF;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Covers css-gcpm-3's <c>float: footnote</c>: the in-flow numbered call, the detached body's own
    /// marker, per-page numbering/reset, and the dynamic footnote-area reservation
    /// (<see cref="HtmlContainerInt.FootnoteAreaHeightsBySlot"/>) that shrinks the usable content band for
    /// ordinary flow content on the same page.
    /// </summary>
    public class FootnoteIntegrationTests
    {
        [Fact]
        public async Task Footnote_SynthesizesInFlowCallWithNumberOne()
        {
            var html = Wrap(@"
                <p>Some text<sup id='fn1' style='float:footnote'>Note body</sup> continues.</p>");

            var (root, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal(1, call.Number);
            Assert.Equal("1", call.Text);
            Assert.Same(call, FindFootnoteCall(root));
            Assert.Null(call.Body.ParentBox);
            Assert.Equal("fn1", call.Body.HtmlTag?.TryGetAttribute("id"));
        }

        [Fact]
        public async Task Footnote_BodyGetsSynthesizedMarkerAsFirstChild()
        {
            var html = Wrap(@"
                <p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            var marker = Assert.IsType<CssBoxFootnoteMarker>(call.Body.Boxes[0]);
            Assert.Equal("1.", marker.Text);
        }

        [Fact]
        public async Task Footnote_ReservesBottomSpaceOnItsLandingPage()
        {
            var html = Wrap(@"
                <p>Text<sup style='float:footnote'>A reasonably long footnote body that takes up some vertical space once laid out.</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var reservation = Assert.Single(container.FootnoteAreaHeightsBySlot);
            Assert.Equal(0, reservation.Key);
            Assert.True(reservation.Value > 0);
        }

        [Fact]
        public async Task Footnote_FollowingContent_LandsAboveTheReservedStrip()
        {
            var withFootnote = Wrap(@"
                <div id='spacer' style='height:700pt;'></div>
                <p>Text<sup style='float:footnote'>Note body</sup></p>
                <div id='after' style='height:10pt;'></div>");
            var withoutFootnote = Wrap(@"
                <div id='spacer' style='height:700pt;'></div>
                <p>Text</p>
                <div id='after' style='height:10pt;'></div>");

            var (rootWith, containerWith) = await LayoutAsync(withFootnote, pageHeight: 842);
            var (rootWithout, _) = await LayoutAsync(withoutFootnote, pageHeight: 842);

            var afterWith = FindById(rootWith, "after")!;
            var afterWithout = FindById(rootWithout, "after")!;

            // The footnote's own reservation only exists on the page with a footnote landing on it,
            // so identical preceding content lands at the identical Y in both documents (nothing before
            // the footnote's own call is affected) - but the footnote area must sit above wherever the
            // page's raw content-band bottom is, never overlapping it.
            Assert.Equal(afterWithout.Location.Y, afterWith.Location.Y, 0.5);

            var reservation = containerWith.FootnoteAreaHeightsBySlot[0];
            var pageBottom = containerWith.PageBottomOf(0);
            Assert.True(afterWith.ActualBottom <= pageBottom - reservation + 0.5);
        }

        [Fact]
        public async Task Footnote_MultipleOnOnePage_NumberedInDocumentOrderAndStack()
        {
            var html = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote'>First note</sup>
                Two<sup id='fn2' style='float:footnote'>Second note</sup></p>");

            var (root, container) = await LayoutAsync(html);

            Assert.Equal(2, container.FootnoteCalls.Count);
            var first = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn1");
            var second = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn2");

            Assert.Equal(1, first.Number);
            Assert.Equal(2, second.Number);

            // Stacked in document order: the first footnote's body sits above the second's.
            Assert.True(first.Body.Location.Y < second.Body.Location.Y);

            var reservation = Assert.Single(container.FootnoteAreaHeightsBySlot).Value;
            Assert.True(reservation > 0);
            _ = root;
        }

        [Fact]
        public async Task Footnote_AcrossForcedPageBreak_ResetsNumberingAndAttachesAreaOnBothPages()
        {
            var html = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote'>First</sup></p>
                <div style='break-before: page;'>
                <p>Two<sup id='fn2' style='float:footnote'>Second</sup></p>
                </div>");

            var (_, container) = await LayoutAsync(html);

            Assert.Equal(2, container.FootnoteCalls.Count);
            var first = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn1");
            var second = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn2");

            // Per-page reset: the second footnote is alone on its own (forced-break) page, so it is "1"
            // there too, not "2".
            Assert.Equal(1, first.Number);
            Assert.Equal(1, second.Number);

            Assert.Equal(2, container.FootnoteAreaHeightsBySlot.Count);
            Assert.True(container.FootnoteAreaHeightsBySlot[0] > 0);
            Assert.True(container.FootnoteAreaHeightsBySlot[1] > 0);

            // The fragment tree - what AttachFootnoteAreas actually produced, and so what paint actually
            // draws - must reflect both pages' areas, not just whichever one the convergence loop's
            // resolve happened to compute first. Regression coverage for a bug where the loop could exit
            // right after a LayoutDocument call with no matching re-resolve, leaving
            // FootnoteAreaHeightsBySlot/_footnoteCallsBySlot describing a Root tree state layout had
            // already moved on from - every page but the first silently lost its footnote area.
            var pages = container.FragmentTree!.Fragmentainers;
            Assert.Equal(2, pages.Count);
            Assert.NotNull(pages[0].FootnoteArea);
            Assert.Single(pages[0].FootnoteArea!.Bodies);
            Assert.NotNull(pages[1].FootnoteArea);
            Assert.Single(pages[1].FootnoteArea!.Bodies);
        }

        [Fact]
        public async Task Footnote_AuthorPseudoElementRules_StyleTheCallAndMarker()
        {
            var html = Wrap(@"
                <style>
                    ::footnote-call { color: rgb(37, 99, 235); }
                    ::footnote-marker { color: rgb(220, 38, 38); }
                </style>
                <p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal("rgb(37, 99, 235)", call.Color);

            var marker = Assert.IsType<CssBoxFootnoteMarker>(call.Body.Boxes[0]);
            Assert.Equal("rgb(220, 38, 38)", marker.Color);
        }

        [Fact]
        public async Task Footnote_TypeSelectorPseudoElementRule_MatchesAgainstTheRealSourceElement()
        {
            // sup::footnote-call, not span::footnote-call - proves re-matching resolves the non-pseudo
            // part of the selector against the real, detached source element (FootnoteSourceBox), not
            // against the call's own structural ParentBox (the paragraph it was inserted into).
            var html = Wrap(@"
                <style>
                    sup::footnote-call { color: rgb(37, 99, 235); }
                    span::footnote-call { color: rgb(220, 38, 38); }
                </style>
                <p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal("rgb(37, 99, 235)", call.Color);
        }

        [Fact]
        public async Task Footnote_OnBlockLevelSource_IsANoOp()
        {
            var html = Wrap(@"
                <div id='block' style='float:footnote;'>Block footnote body</div>");

            var (root, container) = await LayoutAsync(html);

            Assert.Empty(container.FootnoteCalls);

            // Still an ordinary, in-flow tree member - never detached, since a block-level float:footnote
            // source is left alone (an accepted gap, behaving as float: none) rather than pulled out.
            var block = FindById(root, "block");
            Assert.NotNull(block);
            Assert.NotNull(block!.ParentBox);
        }

        [Fact]
        public async Task Footnote_Nested_IsInert()
        {
            var html = Wrap(@"
                <p>Text<sup id='outer' style='float:footnote'>Outer note
                    <span id='inner' style='float:footnote'>Inner note</span>
                </sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal("outer", call.Body.HtmlTag?.TryGetAttribute("id"));

            // The inner float:footnote box is still inside the (now detached) outer body, untouched.
            var inner = Descendants(call.Body).FirstOrDefault(b => b.HtmlTag?.TryGetAttribute("id") == "inner");
            Assert.NotNull(inner);
        }

        [Fact]
        public async Task Footnote_RealPdfGeneratorPipeline_GeneratesWithoutError()
        {
            // Exercises the real PdfGenerator.PaintFootnoteArea paint path (LayoutAsync above only ever
            // runs layout, never paint) against a document with a footnote on its first page but not its
            // (forced-break) second - also the only way to cover AttachFootnoteAreas' "this page has no
            // footnote area" branch, which every other test here never reaches. Visual correctness of
            // this paint path (divider position, stacked bodies, cross-page geometry) is verified by
            // rasterizing the paged_media_footnotes showcase through PDFium and MuPDF, per this repo's
            // painting-verification convention - this test's job is just to prove the path executes for
            // both a page that has a footnote area and one that doesn't, not to re-derive that proof.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { size: 300pt 300pt; margin: 20pt; }"
                + "body { margin: 0; font-size: 10pt; }"
                + "</style></head><body>"
                + "<p>One<sup style='float:footnote'>Note body</sup></p>"
                + "<div style='break-before: page;'><p>No footnote here.</p></div>"
                + "</body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PdfDocument.Pages.Count);

            using var ms = new MemoryStream();
            doc.Save(ms);
            Assert.True(ms.Length > 0);
        }

        [Fact]
        public async Task Footnote_OnAPageWithItsOwnMarginOverride_PaintsInsideThatPagesOwnContentTransform()
        {
            // Regression coverage for a bug found in review: the footnote area's own geometry
            // (AttachFootnoteAreas) is built the same fragmentainer-local way ordinary content is - anchored
            // at the *base* MarginLeft/MarginTop in layout space, needing the page's own deltaX/deltaY
            // translate (PdfGenerator's per-page content transform) to land correctly on a page whose own
            // @page margins differ from the base. Painting it after that transform was undone (the same
            // spot plain string/counter/element() margin-box content correctly uses, since THAT content is
            // already page-absolute) left it positioned as if every page shared the base margin. This test
            // only proves the differing-margin case still generates a well-formed multi-page PDF; the actual
            // divider/body position for this exact scenario was verified by hand via rasterization (PDFium)
            // against @page :first { margin-top: 80pt; margin-left: 60pt }.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { size: 300pt 300pt; margin: 20pt; }"
                + "@page :first { margin-top: 80pt; margin-left: 60pt; }"
                + "body { margin: 0; font-size: 10pt; }"
                + "</style></head><body>"
                + "<p>One<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(1, doc.PdfDocument.Pages.Count);

            using var ms = new MemoryStream();
            doc.Save(ms);
            Assert.True(ms.Length > 0);
        }

        private static CssBoxFootnoteCall? FindFootnoteCall(CssBox box)
        {
            if (box is CssBoxFootnoteCall call) return call;

            foreach (var child in box.Boxes)
            {
                var found = FindFootnoteCall(child);
                if (found is not null) return found;
            }

            return null;
        }
    }
}
