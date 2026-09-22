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

            var rule = container.ResolveFootnoteAreaRule(0, 400);

            Assert.Equal(FootnoteAreaRule.Ua, rule);
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

            var rule = container.ResolveFootnoteAreaRule(0, 400);

            Assert.Equal(4, rule.TopPadding); // undeclared margin-top: still the UA default
            Assert.Equal(3, rule.DividerThickness);
            Assert.Equal(12, rule.DividerToBodyGap);
            Assert.Equal(90, rule.MaxHeight);
            Assert.Null(rule.Height); // undeclared height: still content-sized
            Assert.Equal("rgb(255, 0, 0)", rule.DividerColor);
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

            var rule = container.ResolveFootnoteAreaRule(0, 400);

            Assert.Equal(0, rule.DividerThickness);
            // The raw declared color slot ("initial" - the border-top shorthand's own unfilled-color
            // sentinel, see MarginBoxRenderer.PaintBorder's remarks) is carried through as-is; it's
            // PdfGenerator.ResolveFootnoteDividerColor, not this method, that turns it into black -
            // moot here anyway, since a zero-thickness divider never paints regardless of its color.
            Assert.Equal(RColor.Black, PdfGenerator.ResolveFootnoteDividerColor(rule.DividerColor, new PdfSharpAdapter()));
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
        public async Task AtFootnoteHeight_ReservesThatContentBandRegardlessOfHowTallTheBodyIs()
        {
            // The declared height sizes the band the bodies stack in, not the area including its own
            // chrome - so the reservation is height + the UA chrome (4pt margin + 1pt divider + 4pt gap),
            // and a longer body does not change it.
            const string shortNote = "Note";
            const string longNote = "A considerably longer footnote body that would otherwise wrap onto "
                + "several lines and so reserve a good deal more room than the short one does.";

            var (_, shortContainer) = await LayoutAsync(HeightHtml("60pt", shortNote));
            var (_, longContainer) = await LayoutAsync(HeightHtml("60pt", longNote));

            Assert.Equal(60 + 9, shortContainer.FootnoteAreaHeightsBySlot[0], 0.01);
            Assert.Equal(60 + 9, longContainer.FootnoteAreaHeightsBySlot[0], 0.01);
        }

        [Fact]
        public async Task AtFootnoteHeight_TallerThanItsContent_LeavesTheSlackBelowTheLastBody()
        {
            var (_, container) = await LayoutAsync(HeightHtml("60pt", "Note"));

            var call = Assert.Single(container.FootnoteCalls);
            var areaTop = container.PageBottomOf(0) - container.FootnoteAreaHeightsBySlot[0];

            // Bodies start at the content-box top (after the 9pt of UA chrome), exactly as a fixed-height
            // block box top-aligns its content...
            Assert.Equal(areaTop + 9, call.Body.Location.Y, 0.01);
            // ...so the unused part of the band is below the last body, not above the first.
            Assert.True(call.Body.ActualBottom < container.PageBottomOf(0) - 1);
        }

        [Fact]
        public async Task AtFootnoteHeight_PushesFollowingFlowContentUpByTheWholeBand()
        {
            var withHeight = await LayoutAsync(HeightHtml("60pt", "Note"));
            var withoutHeight = await LayoutAsync(HeightHtml(null, "Note"));

            var reservedWith = withHeight.Container.FootnoteAreaHeightsBySlot[0];
            var reservedWithout = withoutHeight.Container.FootnoteAreaHeightsBySlot[0];

            // The whole declared band is reserved from the flow, not just the part the body fills.
            Assert.True(reservedWith > reservedWithout);
            Assert.Equal(60 + 9, reservedWith, 0.01);
        }

        [Fact]
        public async Task AtFootnoteHeight_Percentage_ResolvesAgainstThePageContentBand()
        {
            // height is a block-axis length, so its percentage basis is the page's own content band -
            // not the content width that margin-top/padding-top percentages resolve against.
            var (_, container) = await LayoutAsync(HeightHtml("10%", "Note"));

            var band = container.PageBottomOf(0) - container.PageTopOf(0);
            var rule = container.ResolveFootnoteAreaRule(0, 400);

            Assert.Equal(band * 0.10, rule.Height!.Value, 0.01);
            Assert.Equal(band * 0.10 + 9, container.FootnoteAreaHeightsBySlot[0], 0.01);
        }

        [Fact]
        public async Task AtFootnoteHeightAuto_IsTheSameAsNotDeclaringIt()
        {
            var auto = await LayoutAsync(HeightHtml("auto", "Note"));
            var undeclared = await LayoutAsync(HeightHtml(null, "Note"));

            Assert.Null(auto.Container.ResolveFootnoteAreaRule(0, 400).Height);
            Assert.Equal(
                undeclared.Container.FootnoteAreaHeightsBySlot[0],
                auto.Container.FootnoteAreaHeightsBySlot[0],
                0.01);
        }

        [Fact]
        public async Task AtFootnoteFloatAndColumnSpan_AreParsedAndIgnored()
        {
            // The spec's own default @footnote stylesheet declares both; PeachPDF's note area is always
            // page-bottom and full-width, so they must change nothing rather than half-apply.
            var declared = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { float: bottom; column-span: all; } }"
                + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p>"
                + "</body></html>";
            var plain = Wrap("<p>Text<sup style='float:footnote'>Note body</sup></p>");

            var (_, declaredContainer) = await LayoutAsync(declared);
            var (_, plainContainer) = await LayoutAsync(plain);

            Assert.Equal(
                plainContainer.FootnoteAreaHeightsBySlot[0],
                declaredContainer.FootnoteAreaHeightsBySlot[0],
                0.01);

            var declaredArea = Assert.Single(declaredContainer.FragmentTree!.Fragmentainers).FootnoteArea!;
            var plainArea = Assert.Single(plainContainer.FragmentTree!.Fragmentainers).FootnoteArea!;
            Assert.Equal(plainArea.DividerRect.X, declaredArea.DividerRect.X, 0.01);
            Assert.Equal(plainArea.DividerRect.Y, declaredArea.DividerRect.Y, 0.01);
            Assert.Equal(plainArea.DividerRect.Width, declaredArea.DividerRect.Width, 0.01);
        }

        [Fact]
        public async Task AtFootnoteHeight_DividerRectStillMatchesTheReservedBand()
        {
            // The byte-identical-geometry invariant, under the fixed-height path: the divider the
            // fragment tree paints has to sit inside the band the convergence loop reserved.
            var (_, container) = await LayoutAsync(HeightHtml("60pt", "Note"));

            var page = Assert.Single(container.FragmentTree!.Fragmentainers);
            var areaTop = container.PageBottomOf(0) - container.FootnoteAreaHeightsBySlot[0];

            // 4pt of UA margin-top sits above the divider.
            Assert.Equal(areaTop + 4 - page.LocalOriginY, page.FootnoteArea!.DividerRect.Y, 0.01);
        }

        [Fact]
        public async Task AtFootnoteHeight_ContentOverflowingIt_CountsAsNotFittingForFootnotePolicy()
        {
            // Overflowing a height the author declared is css-gcpm-3's own "cannot be placed on the
            // current page due to lack of space": the note area is full whatever room the page has left.
            // p0 gives p1 a predecessor to break away from (css-break-3 4.4).
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { height: 12pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: block;'>This note body is "
                + "far longer than the 12pt band declared for the note area, so it overflows that band "
                + "and footnote-policy: block should move this whole paragraph to the next page.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p0 = FindById(root, "p0");
            var p1 = FindById(root, "p1");
            Assert.NotNull(p0);
            Assert.NotNull(p1);

            Assert.True(
                container.PageIndexOf(p1!.Location.Y) > container.PageIndexOf(p0!.Location.Y),
                "footnote-policy: block should have moved the paragraph off p0's page");
        }

        [Fact]
        public async Task AtFootnoteHeight_ContentOverflowingIt_UnderPolicyAuto_JustOverflows()
        {
            // The default policy is unaffected - a too-tall stack overflows the declared band exactly as
            // an over-tall body already overflows a content-sized one.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { height: 12pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<p id='p1'>Text<sup style='float:footnote'>This note body is far longer than the 12pt "
                + "band declared for the note area, and under the default footnote-policy: auto it simply "
                + "overflows rather than forcing anything to move.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p0 = FindById(root, "p0");
            var p1 = FindById(root, "p1");

            Assert.Equal(container.PageIndexOf(p0!.Location.Y), container.PageIndexOf(p1!.Location.Y));
            // Still only the declared band is reserved, even though the body needs more.
            Assert.Equal(12 + 9, container.FootnoteAreaHeightsBySlot[0], 0.01);
        }

        [Theory]
        [InlineData(null, 1)]                       // no @footnote rule at all
        [InlineData("counter-increment: footnote", 1)]
        [InlineData("counter-increment: footnote 3", 3)]
        [InlineData("counter-increment: none", 0)]
        [InlineData("counter-increment: chapter 2", 0)]   // declared, but not for this counter
        public async Task AtFootnoteCounterIncrement_ResolvesTheFootnoteCountersStep(string? declaration, int expected)
        {
            var rule = declaration is null ? string.Empty : $"@page {{ @footnote {{ {declaration}; }} }}";
            var html = "<!DOCTYPE html><html><head><style>" + rule + "</style></head><body style='margin:0'>"
                + "<p>Text<sup style='float:footnote'>Note body</sup></p></body></html>";

            var (_, container) = await LayoutAsync(html);

            Assert.Equal(expected, container.ResolveFootnoteAreaRule(0, 400).Step);
        }

        private static string HeightHtml(string? height, string noteBody)
        {
            var rule = height is null ? string.Empty : $"@page {{ @footnote {{ height: {height}; }} }}";
            return "<!DOCTYPE html><html><head><style>" + rule + "</style></head><body style='margin:0'>"
                + $"<p>Text<sup style='float:footnote'>{noteBody}</sup></p>"
                + "<p id='after'>Following flow content.</p>"
                + "</body></html>";
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
        public async Task FootnotePolicyBlock_ContainingBlockIsItsWrappersOnlyChild_HoistsTheBreakToTheWrapper()
        {
            // css-break-3 §3.1 propagation: a forced break-before on a box that is the first in-flow
            // child of its own parent is taken by that parent instead (BreakPropagation.AnchorForBreakBefore) -
            // an author break-before on p1 here would be hoisted to #wrapper the same way. If
            // footnote-policy: block set its flag on p1 directly without going through the same anchor,
            // #wrapper would never learn a break landed inside it and would stay on the original page
            // while p1 (its only child) moved out from under it.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 20pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<div id='wrapper' style='border: 1pt solid black;'>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: block;'>This note body is "
                + "long enough that its own height at the page's content width exceeds the tiny 20pt "
                + "max-height declared on @footnote, so footnote-policy: block should force this whole "
                + "paragraph - and the wrapper it is the sole child of - onto the next page.</sup></p>"
                + "</div>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);

            var p0 = FindById(root, "p0");
            var wrapper = FindById(root, "wrapper");
            var p1 = FindById(root, "p1");
            Assert.NotNull(p0);
            Assert.NotNull(wrapper);
            Assert.NotNull(p1);

            Assert.Equal(0, container.PageIndexOf(p0!.Location.Y));
            // The wrapper itself relocates, not just its content - proves the break was hoisted to the
            // anchor rather than forced directly on p1 while #wrapper stayed behind. (Not asserting
            // wrapper.Location.Y == p1.Location.Y exactly - #wrapper's own border/p1's own margin mean
            // they're offset from each other by a few points, same as any ordinary parent/first-child.)
            Assert.True(container.PageIndexOf(wrapper!.Location.Y) > 0);
            Assert.Equal(container.PageIndexOf(wrapper.Location.Y), container.PageIndexOf(p1!.Location.Y));
        }

        [Fact]
        public async Task FootnotePolicyBlock_ThenClear_ResetsTheForcedBreakFlagAndTrackingCollections()
        {
            // Not a WeakReference/GC-based leak test - HtmlContainerInt.FragmentTree (pre-existing,
            // unrelated to footnote-policy) already keeps the whole box tree reachable after Clear()
            // regardless of this fix, so nothing here could ever actually become collectible. This
            // asserts the narrower, deterministic thing Clear() now actually does: the flag this PR
            // added is reset on every box it was set on, and the two tracking collections are emptied,
            // exactly like FootnoteCalls/FootnoteAreaHeightsBySlot right next to them already were.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { @footnote { max-height: 20pt; } }"
                + "</style></head><body style='margin:0'>"
                + "<p id='p0'>An ordinary paragraph with no footnote of its own.</p>"
                + "<p id='p1'>Text<sup style='float:footnote; footnote-policy: block;'>This note body is "
                + "long enough that its own height at the page's content width exceeds the tiny 20pt "
                + "max-height declared on @footnote, so footnote-policy: block forces this paragraph onto "
                + "the next page - and so records it as a forced-break box this test then checks Clear() "
                + "actually resets.</sup></p>"
                + "</body></html>";

            var (root, container) = await LayoutAsync(html);
            var p1 = FindById(root, "p1")!;
            Assert.True(container.PageIndexOf(p1.Location.Y) > 0); // sanity: the break was actually taken
            Assert.True(p1.FootnotePolicyForcedBreakBefore);

            container.Clear();

            Assert.False(p1.FootnotePolicyForcedBreakBefore);
            Assert.Empty(container.FootnotePolicyForcedLineCalls);
            Assert.Empty(container.FootnotePolicyLineBreaksTakenThisPass);
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
