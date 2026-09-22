using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
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

        [Fact]
        public async Task AtFootnote_NoRuleDeclared_ResolvesTheUaDefaultBoxModel()
        {
            var html = Wrap("<p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var (topPadding, dividerThickness, dividerToBodyGap, maxHeight, dividerColor) =
                container.ResolveFootnoteAreaBoxModel(0, 400);

            Assert.Equal(4, topPadding);
            Assert.Equal(1, dividerThickness);
            Assert.Equal(4, dividerToBodyGap);
            Assert.Null(maxHeight);
            Assert.Null(dividerColor);
        }

        [Fact]
        public async Task AtFootnote_DeclaredInsidePage_OverridesTheDeclaredLonghandsOnly()
        {
            // Only border-top and padding-top are declared - margin-top (this method's "TopPadding")
            // must still fall back to the UA default, proving the cascade-style per-longhand fallback
            // (not "any @footnote rule replaces the whole box model").
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { border-top: 3pt solid rgb(255, 0, 0); padding-top: 12pt; max-height: 90pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            var (topPadding, dividerThickness, dividerToBodyGap, maxHeight, dividerColor) =
                container.ResolveFootnoteAreaBoxModel(0, 400);

            Assert.Equal(4, topPadding); // undeclared margin-top: still the UA default
            Assert.Equal(3, dividerThickness);
            Assert.Equal(12, dividerToBodyGap);
            Assert.Equal(90, maxHeight);
            Assert.Equal("rgb(255, 0, 0)", dividerColor);
        }

        [Fact]
        public async Task AtFootnote_BorderTopNone_ZeroesTheDividerAndReturnsNoColor()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { border-top: none; } }"
                + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            var (_, dividerThickness, _, _, dividerColor) = container.ResolveFootnoteAreaBoxModel(0, 400);

            Assert.Equal(0, dividerThickness);
            // The raw declared color slot ("initial" - the border-top shorthand's own unfilled-color
            // sentinel, see MarginBoxRenderer.PaintBorder's remarks) is carried through as-is; it's
            // PdfGenerator.ResolveFootnoteDividerColor, not this method, that turns it into black -
            // moot here anyway, since a zero-thickness divider never paints regardless of its color.
            Assert.Equal(RColor.Black, PdfGenerator.ResolveFootnoteDividerColor(dividerColor, new PdfSharpAdapter()));
        }

        [Fact]
        public async Task AtFootnote_StyledBoxModel_ChangesTheReservedHeightByExactlyTheDelta()
        {
            var styled = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { border-top: 5pt solid black; padding-top: 10pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";
            var plain = Wrap("<p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, styledContainer) = await LayoutAsync(styled);
            var (_, plainContainer) = await LayoutAsync(plain);

            var styledReservation = styledContainer.FootnoteAreaHeightsBySlot[0];
            var plainReservation = plainContainer.FootnoteAreaHeightsBySlot[0];

            // UA default is 1pt divider + 4pt gap = 5pt; styled is 5pt divider + 10pt gap = 15pt - a
            // 10pt delta, independent of the (identical) body content height both share.
            Assert.Equal(plainReservation + 10, styledReservation, 0.01);
        }

        [Fact]
        public async Task AtFootnote_DividerRectMirrorsResolvedBoxModel_AcrossResolveAndAttach()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { border-top: 6pt solid rgb(0, 128, 0); } }"
                + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            var page = Assert.Single(container.FragmentTree!.Fragmentainers);
            Assert.NotNull(page.FootnoteArea);
            var footnoteArea = page.FootnoteArea!;
            Assert.Equal(6, footnoteArea.DividerRect.Height, 0.01);
            Assert.Equal("rgb(0, 128, 0)", footnoteArea.DividerColor);
        }

        [Theory]
        [InlineData(null, 0, 0, 0)]
        [InlineData("currentcolor", 0, 0, 0)]
        [InlineData("initial", 0, 0, 0)]
        [InlineData("rgb(255, 0, 0)", 255, 0, 0)]
        public void ResolveFootnoteDividerColor_FallsBackToBlack_ForNoRealColor(string? declared, byte r, byte g, byte b)
        {
            var adapter = new PdfSharpAdapter();

            var resolved = PdfGenerator.ResolveFootnoteDividerColor(declared, adapter);

            Assert.Equal(RColor.FromArgb(r, g, b), resolved);
        }

        [Fact]
        public void PaintFootnoteArea_DrawsTheDividerAtItsResolvedRectBeforeTheBodies()
        {
            // The RGraphics-level overload, driven directly with a recording mock - per this repo's own
            // testing conventions, a page-count/stream-length check (as the full-pipeline tests above
            // use) cannot tell a real divider draw call apart from a silently no-op one.
            var container = new HtmlContainerInt(new PdfSharpAdapter());
            var g = new RecordingGraphics(new PdfSharpAdapter());
            var dividerRect = new RRect(10, 20, 300, 3);
            var footnoteArea = new FootnoteAreaFragment(dividerRect, [], "rgb(0, 128, 0)");

            PdfGenerator.PaintFootnoteArea(g, new PdfSharpAdapter(), container, footnoteArea);

            var opKinds = g.Log.Select(op => op.Kind).ToList();
            Assert.Equal(
                [PaintOpKind.PushClip, PaintOpKind.FillRect, PaintOpKind.PopClip],
                opKinds);
            Assert.Equal(dividerRect, g.Log.Single(op => op.Kind == PaintOpKind.FillRect).Bounds);
        }

        [Fact]
        public void PaintFootnoteArea_SkipsTheDividerDrawCall_WhenThicknessIsZero()
        {
            // border-top: none/hidden (or no @footnote rule declaring one at all, on a page where the
            // resolved thickness happens to be zero) must not draw a phantom zero-height rectangle.
            var container = new HtmlContainerInt(new PdfSharpAdapter());
            var g = new RecordingGraphics(new PdfSharpAdapter());
            var footnoteArea = new FootnoteAreaFragment(new RRect(10, 20, 300, 0), [], null);

            PdfGenerator.PaintFootnoteArea(g, new PdfSharpAdapter(), container, footnoteArea);

            Assert.DoesNotContain(g.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Fact]
        public async Task FootnoteDisplayBlock_MultipleBodies_EachStartsItsOwnRow()
        {
            var html = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote; footnote-display: block;'>First</sup>
                Two<sup id='fn2' style='float:footnote; footnote-display: block;'>Second</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var first = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn1");
            var second = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn2");

            Assert.True(second.Body.Location.Y > first.Body.Location.Y);
            Assert.Equal(first.Body.Location.X, second.Body.Location.X, 0.01);
        }

        [Fact]
        public async Task FootnoteDisplayInline_TwoShortBodies_PackOntoTheSameRow()
        {
            var html = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote; footnote-display: inline;'>First</sup>
                Two<sup id='fn2' style='float:footnote; footnote-display: inline;'>Second</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var first = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn1");
            var second = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn2");

            // Same row: identical Y, second body positioned strictly to the right of the first.
            Assert.Equal(first.Body.Location.Y, second.Body.Location.Y, 0.01);
            Assert.True(second.Body.Location.X > first.Body.Location.X);

            // Packed onto one row is shorter overall than the block default's two stacked rows.
            var reservation = container.FootnoteAreaHeightsBySlot[0];
            Assert.True(reservation > 0);
        }

        [Fact]
        public async Task FootnoteDisplayInline_NarrowerThanBlockDefault_ForTheSameContent()
        {
            var inlineHtml = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote; footnote-display: inline;'>First</sup>
                Two<sup id='fn2' style='float:footnote; footnote-display: inline;'>Second</sup></p>");
            var blockHtml = Wrap(@"
                <p>One<sup id='fn1' style='float:footnote;'>First</sup>
                Two<sup id='fn2' style='float:footnote;'>Second</sup></p>");

            var (_, inlineContainer) = await LayoutAsync(inlineHtml);
            var (_, blockContainer) = await LayoutAsync(blockHtml);

            // Two short notes packed onto one row reserve less height than the same two notes
            // stacked as two full-width block rows.
            Assert.True(inlineContainer.FootnoteAreaHeightsBySlot[0] < blockContainer.FootnoteAreaHeightsBySlot[0]);
        }

        [Fact]
        public async Task FootnoteDisplayCompact_ShortBodyPacksInline_LongBodyTakesItsOwnRow()
        {
            var longNote = string.Join(" ", Enumerable.Repeat("word", 60));
            var html = Wrap($@"
                <p>One<sup id='fn1' style='float:footnote; footnote-display: compact;'>Short</sup>
                Two<sup id='fn2' style='float:footnote; footnote-display: compact;'>{longNote}</sup>
                Three<sup id='fn3' style='float:footnote; footnote-display: compact;'>Also short</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var first = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn1");
            var second = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn2");
            var third = container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == "fn3");

            // fn2 wraps at full content width (many words), so compact falls back to a full-width row
            // for it - a new row below fn1's, and fn3 (short again) starts yet another row rather than
            // packing beside the wrapped fn2.
            Assert.True(second.Body.Location.Y > first.Body.Location.Y);
            Assert.Equal(0, second.Body.Location.X - container.MarginLeft, 0.01);
            Assert.True(third.Body.Location.Y > second.Body.Location.Y);
        }

        [Fact]
        public async Task FootnotePolicyBlock_NoteAreaExceedsMaxHeight_MovesTheParagraphToTheNextPage()
        {
            // A forced break needs a predecessor in the flow to break away from (css-break-3 §4.4 - the
            // same reason "the first element of a document" never manufactures a blank leading page for
            // an author break-before either), so p0 exists purely to give p1 somewhere to break from.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 20pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: block;'>This note body is "
                + "long enough that its own height at the page's content width exceeds the tiny 20pt "
                + "max-height declared on @footnote, so footnote-policy: block should force this whole "
                + "paragraph onto the next page instead of overflowing here.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            // Not container.FootnoteCalls[0].OwnGeometryTop() - ResolveFootnotesForThisAttempt's own last
            // act on the last convergence pass is re-parsing each call's word (ApplyNumber/ParseToWords),
            // which leaves that fresh CssRect at its unset default Top until a later pass's real inline
            // layout would reposition it - one never comes once the loop has exited. Rectangles (updated
            // by relocation itself, not by re-parsing) and an ordinary block box's own Location stay
            // reliable read after the fact; only the call's own transient Words don't.
            var p0 = FindById(root, "p0");
            var p1 = FindById(root, "p1");
            Assert.NotNull(p0);
            Assert.NotNull(p1);

            var p0Slot = container.PageIndexOf(p0!.Location.Y);
            var paragraphSlot = container.PageIndexOf(p1!.Location.Y);

            Assert.Equal(0, p0Slot);
            Assert.True(paragraphSlot > p0Slot);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
        }

        [Fact]
        public async Task FootnotePolicyBlock_CallNestedInsideAnInlineSpan_StillMovesTheParagraphNotTheSpan()
        {
            // The call's structural ParentBox is the inline <span>, not the <p> - FootnotePolicyContainingBlockOf
            // has to walk up past it to find the real "paragraph that contains the footnote reference"
            // css-gcpm-3 §2.8 asks for, rather than trying (and failing, since a plain <span> never takes
            // a forced break on its own) to move the span itself.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 20pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<p id='p1'>Text <span id='wrapper'>nested text<sup style='float:footnote; footnote-policy: block;'>"
                + "This note body is long enough that its own height at the page's content width exceeds "
                + "the tiny 20pt max-height declared on @footnote, so footnote-policy: block should force "
                + "the whole paragraph - not the inline span - onto the next page.</sup></span></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p0 = FindById(root, "p0");
            var p1 = FindById(root, "p1");
            Assert.NotNull(p0);
            Assert.NotNull(p1);

            Assert.Equal(0, container.PageIndexOf(p0!.Location.Y));
            Assert.True(container.PageIndexOf(p1!.Location.Y) > 0);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
        }

        [Fact]
        public async Task FootnotePolicyAuto_NoteAreaExceedsMaxHeight_StaysOnTheSamePage()
        {
            // Regression: the default policy is unaffected by max-height - it still just overflows,
            // exactly as before this feature.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 20pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p1'>Text<sup style='float:footnote'>This note body is long enough that its own "
                + "height at the page's content width exceeds the tiny 20pt max-height declared on "
                + "@footnote, but footnote-policy defaults to auto, which does not force a break.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p1 = FindById(root, "p1");
            Assert.NotNull(p1);
            Assert.Equal(0, container.PageIndexOf(p1!.Location.Y));
        }

        [Fact]
        public async Task FootnotePolicyBlock_NoteAreaFits_DoesNotForceABreak()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 500pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: block;'>Short note.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p1 = FindById(root, "p1");
            Assert.NotNull(p1);
            Assert.Equal(0, container.PageIndexOf(p1!.Location.Y));
            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        [Fact]
        public async Task FootnotePolicyLine_NoteAreaExceedsMaxHeight_ForcesAnExtraPageComparedToAuto()
        {
            // A raw CssBox's own Rectangles/Words are only a reliable read within the single pass that
            // set them - HtmlContainerInt's footnote-policy: line handling (like every other multi-pass
            // mechanism in this codebase) can re-lay the whole document out several times, and each pass
            // creates fresh CssLineBox keys rather than overwriting the previous pass's entries (see
            // docs/architecture.md §6: only the materialized FragmentTree is a reliable post-layout
            // source, never CssBox geometry directly). So this compares page COUNT against the identical
            // markup under the default footnote-policy: auto, rather than asserting exactly which page a
            // particular line landed on - line and auto must disagree on page count, or nothing forced
            // anything.
            // The call sits after enough of its own paragraph's text that it lands on a LATER line, not
            // the first - a forced break needs a predecessor to break away from (css-break-3 §4.4, the
            // same reason footnote-policy: block's own test needs a preceding paragraph), and for an
            // inline break that predecessor can be an earlier line of the very same paragraph. A narrow
            // page forces the wrap.
            var leading = string.Join(" ", Enumerable.Repeat("word", 10));
            static string Markup(string leading, string policyDeclaration) =>
                "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 10pt; } }"
                + "</style></head><body style='margin:0'>"
                + $"<p id='p1'>{leading} <sup style='float:footnote;{policyDeclaration}'>This footnote's "
                + "own body text is long enough that, combined with the tiny max-height declared on "
                + "@footnote, its note area does not fit.</sup> more text.</p>"
                + "</body></html>";

            var (rootLine, containerLine) = await LayoutAsync(
                Markup(leading, " footnote-policy: line;"), pageWidth: 120, margin: 10);
            var (rootAuto, containerAuto) = await LayoutAsync(
                Markup(leading, ""), pageWidth: 120, margin: 10);

            var p1Line = FindById(rootLine, "p1");
            Assert.NotNull(p1Line);

            // Unlike footnote-policy: block, the paragraph box itself never relocates - only the specific
            // line carrying the call does.
            Assert.Equal(0, containerLine.PageIndexOf(p1Line!.Location.Y));

            Assert.True(
                containerLine.FragmentTree!.Fragmentainers.Count > containerAuto.FragmentTree!.Fragmentainers.Count);
        }

        [Fact]
        public async Task FootnotePolicyLine_NoteAreaFits_DoesNotForceABreak()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 500pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: line;'>Short note.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p1 = FindById(root, "p1");
            Assert.NotNull(p1);
            Assert.Equal(0, container.PageIndexOf(p1!.Location.Y));
            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        [Theory]
        [InlineData("block", "Block")]
        [InlineData("inline", "Inline")]
        [InlineData("compact", "Compact")]
        public async Task FootnoteDisplay_ParsesAndAppliesToTheDetachedSourceElement(string value, string expected)
        {
            var html = Wrap($"<p>Text<sup style='float:footnote; footnote-display: {value};'>Note</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal(expected, call.Body.FootnoteDisplay.Value.ToString());
        }

        [Fact]
        public async Task FootnoteDisplay_UndeclaredDefaultsToBlock()
        {
            var html = Wrap("<p>Text<sup style='float:footnote'>Note</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal("Block", call.Body.FootnoteDisplay.Value.ToString());
        }

        [Theory]
        [InlineData("auto", "Auto")]
        [InlineData("line", "Line")]
        [InlineData("block", "Block")]
        public async Task FootnotePolicy_ParsesAndAppliesToTheDetachedSourceElement(string value, string expected)
        {
            var html = Wrap($"<p>Text<sup style='float:footnote; footnote-policy: {value};'>Note</sup></p>");

            var (_, container) = await LayoutAsync(html);

            var call = Assert.Single(container.FootnoteCalls);
            Assert.Equal(expected, call.Body.FootnotePolicy.Value.ToString());
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
