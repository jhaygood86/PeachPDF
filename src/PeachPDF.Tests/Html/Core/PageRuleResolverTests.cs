using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Tests for <see cref="PageRuleResolver"/>'s two named-page attribution policies. The cascade
    /// itself (rule matching/specificity/ordering) is covered exhaustively by
    /// <c>PdfGeneratorSelectPageRuleTests</c> through the delegating shims; these pin the policy
    /// split introduced when geometry moved to layout time: paint-time selection keeps the
    /// "active by page END" semantics, while the geometry table uses "active at slot START" so a
    /// slot's band height can never depend on registrations inside the slot itself.
    /// </summary>
    public class PageRuleResolverTests
    {
        [Fact]
        public void ActiveNameAtPageEnd_ElementRegisteringMidPage_NamesTheWholePage()
        {
            var elements = new List<NamedPageElement> { new("chapter", 400) };

            // Page [0, 800): the element's Y falls inside, so the page-end policy adopts it.
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 0, pageHeight: 800));
        }

        [Fact]
        public void ActiveNameAtSlotStart_ElementRegisteringMidSlot_DoesNotNameTheSlot()
        {
            var elements = new List<NamedPageElement> { new("chapter", 400) };

            // Slot starting at 0: the element registered at 400, after the slot start - the
            // slot-start policy must NOT see it (its band was already fixed when layout crossed 0).
            Assert.Null(PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 0));
        }

        [Fact]
        public void ActiveNameAtSlotStart_ElementFlushAtSlotTop_NamesTheSlot()
        {
            // A name change forces a break, so the named element lands exactly at a slot top - the
            // epsilon makes that flush registration count as this slot's name.
            var elements = new List<NamedPageElement> { new("chapter", 800) };

            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 800));
            Assert.Null(PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 799));
        }

        [Fact]
        public void BothPolicies_TakeTheHighestApplicableY()
        {
            var elements = new List<NamedPageElement> { new("one", 100), new("two", 500) };

            Assert.Equal("two", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 0, pageHeight: 800));
            Assert.Equal("two", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 600));
            Assert.Equal("one", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 300));
        }

        [Fact]
        public void ReversionEntry_ShadowsEarlierNamedPage_ForLaterSlots()
        {
            // The used value of `page` reverts to the default when content leaves a named page's
            // subtree - that reversion is registered as an empty-name entry (issue #126). Both policies
            // must adopt it for slots/pages at or after it, so a named page's margins/margin boxes stop
            // applying once content reverts, instead of leaking forward indefinitely.
            var elements = new List<NamedPageElement> { new("chapter", 800), new(string.Empty, 1600) };

            // Before the reversion: still "chapter".
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 800));
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 800, pageHeight: 800));

            // At/after the reversion Y: reverted to the empty (default) name, NOT "chapter".
            Assert.Equal(string.Empty, PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 1600));
            Assert.Equal(string.Empty, PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 1600, pageHeight: 800));
        }

        // ── ResolvePageSize ──────────────────────────────────────────────────────

        private static readonly XSize BasePortrait = new(612, 792);
        private static readonly PageLengthContext DefaultContext = new(EmPt: 12, RemPt: 12, HundredPercentPt: 612);

        private static PageRule ParsePageRule(string css) =>
            new StylesheetParser().Parse(css).Rules.OfType<PageRule>().First();

        [Fact]
        public void ResolvePageSize_NullRule_ReturnsBaseSize()
        {
            Assert.Equal(BasePortrait, PageRuleResolver.ResolvePageSize(null, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_RuleWithNoSizeDeclared_ReturnsBaseSize()
        {
            var rule = ParsePageRule("@page chapter { margin: 10mm; }");

            Assert.Equal(BasePortrait, PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_NamedKeywordWithOrientation_Resolves()
        {
            var rule = ParsePageRule("@page landscape-table { size: a4 landscape; }");

            var size = PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext);

            Assert.True(size.Width > size.Height);
            Assert.Equal(841.89, size.Width, 2);
            Assert.Equal(595.28, size.Height, 2);
        }

        [Fact]
        public void ResolvePageSize_ExplicitLengths_Resolve()
        {
            var rule = ParsePageRule("@page wide { size: 400pt 300pt; }");

            Assert.Equal(new XSize(400, 300), PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_BareOrientationKeyword_RotatesBaseSizePt()
        {
            var rule = ParsePageRule("@page landscape-table { size: landscape; }");

            Assert.Equal(new XSize(792, 612), PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_BareOrientationKeyword_MatchingBaseOrientation_IsANoOp()
        {
            var rule = ParsePageRule("@page portrait-table { size: portrait; }");

            Assert.Equal(BasePortrait, PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_PercentageValue_FallsBackToBaseSize()
        {
            // Not a <length> for `size` (sheet geometry has no percentage basis, css-page-3 §7.1) -
            // ParsePageSizeToPdfPoints returns null, so the base size is kept.
            var rule = ParsePageRule("@page wide { size: 50%; }");

            Assert.Equal(BasePortrait, PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext));
        }

        [Fact]
        public void ResolvePageSize_NoContext_FallsBackToBaseSize()
        {
            var rule = ParsePageRule("@page wide { size: a4 landscape; }");

            Assert.Equal(BasePortrait, PageRuleResolver.ResolvePageSize(rule, BasePortrait, context: null));
        }

        [Fact]
        public void ResolvePageSize_EmDimension_RebasesOnTheWinningRulesOwnFontSize()
        {
            var rule = ParsePageRule("@page wide { font-size: 30pt; size: 10em; }");

            var size = PageRuleResolver.ResolvePageSize(rule, BasePortrait, DefaultContext);

            Assert.Equal(new XSize(300, 300), size);
        }

        // ── ResolvePageBorderAndPadding (issue #1147) ───────────────────────────

        private static StyleDeclaration ParsePageStyle(string css) => ParsePageRule(css).Style;

        [Fact]
        public void ResolvePageBorderAndPadding_NullStyle_ReturnsAllZero()
        {
            var result = PageRuleResolver.ResolvePageBorderAndPadding(null, 595, 842, remPt: 12);

            Assert.Equal(default, result);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_NoBorderOrPaddingDeclared_ReturnsAllZero()
        {
            var style = ParsePageStyle("@page { margin: 1in; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, 595, 842, remPt: 12);

            Assert.Equal(default, result);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_UniformBorderAndPadding_ResolvesAllFourSides()
        {
            var style = ParsePageStyle("@page { border: 5pt solid black; padding: 10pt; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, 595, 842, remPt: 12);

            Assert.Equal(5, result.BorderLeftPt);
            Assert.Equal(5, result.BorderTopPt);
            Assert.Equal(5, result.BorderRightPt);
            Assert.Equal(5, result.BorderBottomPt);
            Assert.Equal(10, result.PaddingLeftPt);
            Assert.Equal(10, result.PaddingTopPt);
            Assert.Equal(10, result.PaddingRightPt);
            Assert.Equal(10, result.PaddingBottomPt);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_BorderWidthWithNoBorderStyle_ResolvesToZero()
        {
            // border-style's own initial value is `none` (CSS 2.1 §8.5.3), which forces width to zero
            // regardless of a declared border-width - MarginBoxRenderer.ResolveBorderWidthPt's existing
            // rule, reused as-is for the page box's own border.
            var style = ParsePageStyle("@page { border-width: 5pt; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, 595, 842, remPt: 12);

            Assert.Equal(0, result.BorderLeftPt);
            Assert.Equal(0, result.BorderTopPt);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_PercentagePadding_ResolvesAgainstSheetWidthAndHeightPerAxis()
        {
            // css-page-3 §6's wording (written for page-margin-box properties, reused here absent
            // page-box-specific guidance - see this method's own remarks): left/right percentages
            // against the containing block's WIDTH, top/bottom against its HEIGHT - here, the page
            // box's own overall sheet dimensions.
            var style = ParsePageStyle("@page { padding-left: 10%; padding-top: 10%; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, sheetWidthPt: 600, sheetHeightPt: 800, remPt: 12);

            Assert.Equal(60, result.PaddingLeftPt);
            Assert.Equal(80, result.PaddingTopPt);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_NegativePadding_ClampsToZero()
        {
            // CSS 2.1 §8.4: padding may never be negative (unlike margin).
            var style = ParsePageStyle("@page { padding: -5pt; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, 595, 842, remPt: 12);

            Assert.Equal(0, result.PaddingLeftPt);
            Assert.Equal(0, result.PaddingTopPt);
        }

        [Fact]
        public void ResolvePageBorderAndPadding_EmBorderWidth_ResolvesAgainstTheStylesOwnFontSize()
        {
            var style = ParsePageStyle("@page { font-size: 20pt; border: 0.5em solid black; }");

            var result = PageRuleResolver.ResolvePageBorderAndPadding(style, 595, 842, remPt: 12);

            Assert.Equal(10, result.BorderLeftPt);
        }
    }
}
