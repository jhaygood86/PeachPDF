using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="PdfGenerator.SelectPageRule"/> — the pure function that picks
    /// which parsed <c>@page</c> rule (base / named / pseudo-selector) applies to a given PDF page.
    /// Exercised directly (rather than through full HTML→PDF generation + rendered-text extraction)
    /// because PeachPDF embeds subsetted fonts, so a decoded PDF content stream's <c>Tj</c> operands are
    /// typically glyph indices, not literal ASCII text — asserting on them would be exactly the kind of
    /// hollow "passing test" CLAUDE.md's testing conventions warn against. These tests are the direct
    /// regression coverage for two real bugs: named-page elements used to always register at Y=0 (see
    /// CssBox.PerformLayoutImp), and named-page matching used to be broken by asymmetric case-lowering.
    /// </summary>
    public class PdfGeneratorSelectPageRuleTests
    {
        [Fact]
        public void BaseRule_WinsWhenNothingElseMatches()
        {
            var rules = ParsePageRules("@page { margin: 10mm; }");

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, [], pageY: 0, pageHeight: 800);

            Assert.Same(rules[0], result);
        }

        [Fact]
        public void NoRules_ReturnsNull()
        {
            var result = PdfGenerator.SelectPageRule([], pageNumber: 1, [], pageY: 0, pageHeight: 800);

            Assert.Null(result);
        }

        [Fact]
        public void NamedRule_SelectedWhenElementYFallsOnThisPage()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter { margin: 20mm; }
                """);
            var namedRule = rules[1];
            var elements = new List<NamedPageElement> { new("chapter", 850) }; // belongs to page 2 of [0,800),[800,1600)

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 2, elements, pageY: 800, pageHeight: 800);

            Assert.Same(namedRule, result);
        }

        [Fact]
        public void NamedRule_NotSelectedWhenElementYFallsOnADifferentPage()
        {
            // Regression for the primary bug: before the fix, every named-page element registered at
            // Y=0 regardless of its true position, making it indistinguishable from "on page 1." A
            // same-named element whose Y clearly belongs to a later page must not leak its rule onto
            // an earlier page it was never actually on.
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter { margin: 20mm; }
                """);
            var baseRule = rules[0];
            var elements = new List<NamedPageElement> { new("chapter", 850) }; // belongs to page 2

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);

            Assert.Same(baseRule, result);
        }

        [Fact]
        public void NamedRule_MatchingIsCaseSensitive()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page Chapter { margin: 20mm; }
                """);
            var baseRule = rules[0];
            var elements = new List<NamedPageElement> { new("chapter", 0) }; // lowercase; rule is "Chapter"

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);

            Assert.Same(baseRule, result);
        }

        [Fact]
        public void NamedRule_MatchingSameCase_Matches()
        {
            // Regression for the secondary bug: SelectPageRule used to lowercase only the @page rule's
            // selector before comparing it against the case-preserved element name, so even an
            // identically-cased pair ("Chapter" / "Chapter") failed to match.
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page Chapter { margin: 20mm; }
                """);
            var namedRule = rules[1];
            var elements = new List<NamedPageElement> { new("Chapter", 0) };

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);

            Assert.Same(namedRule, result);
        }

        [Fact]
        public void NamedRule_CommaSeparatedList_MatchesAnyListedName()
        {
            // Regression for a related parser bug found alongside the primary one: "@page <ident>"
            // used to fail to parse at all (CreatePageSelector only handled the leading-colon
            // pseudo-class case, silently dropping the whole rule for a bare identifier) - discovered
            // because css4.pub's real dictionary CSS groups 8 chapter names onto one shared rule via
            // exactly this comma-separated form: "@page chapter1, chapter2, ..., chapter8 { ... }".
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter1, chapter2, chapter3 { margin: 20mm; }
                """);
            var namedRule = rules[1];

            foreach (var name in new[] { "chapter1", "chapter2", "chapter3" })
            {
                var elements = new List<NamedPageElement> { new(name, 0) };
                var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);
                Assert.Same(namedRule, result);
            }
        }

        [Fact]
        public void NamedRule_CommaSeparatedList_DoesNotMatchUnlistedName()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter1, chapter2 { margin: 20mm; }
                """);
            var baseRule = rules[0];
            var elements = new List<NamedPageElement> { new("chapter9", 0) };

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);

            Assert.Same(baseRule, result);
        }

        [Fact]
        public void NamedPage_CarriesForwardOntoLaterPagesWithNoNewPageNameElement()
        {
            // Regression: the CSS "page" property propagates forward through the normal flow until a
            // later element sets a different one - it isn't a one-page-only tag. A multi-page chapter
            // whose heading (the only element carrying page: chapter2) lands on page 2 must still use
            // the "chapter2" rule on page 3, 4, etc., even though no element with that name is
            // registered there specifically. This is exactly the shape of css4.pub's real dictionary,
            // where a single letter's chapter can span many pages after its one <header> element.
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter2 { margin: 20mm; }
                """);
            var namedRule = rules[1];
            // chapter2's heading starts on page 2 ([800,1600)); nothing else registers on page 3.
            var elements = new List<NamedPageElement> { new("chapter2", 850) };

            var page3Result = PdfGenerator.SelectPageRule(rules, pageNumber: 3, elements, pageY: 1600, pageHeight: 800);

            Assert.Same(namedRule, page3Result);
        }

        [Fact]
        public void NamedPage_SwitchesOnceANewerPageNameElementAppears()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter2 { margin: 20mm; }
                @page chapter3 { margin: 30mm; }
                """);
            var chapter2Rule = rules[1];
            var chapter3Rule = rules[2];
            var elements = new List<NamedPageElement> { new("chapter2", 100), new("chapter3", 2200) };

            // Page 3 ([1600,2400)) is still within chapter2's span (chapter3 starts later, at Y=2200,
            // which does fall inside this page - so by this page's end chapter3 is already active).
            var page3Result = PdfGenerator.SelectPageRule(rules, pageNumber: 3, elements, pageY: 1600, pageHeight: 800);
            Assert.Same(chapter3Rule, page3Result);

            // Page 2 ([800,1600)) is entirely before chapter3 starts, so chapter2 is still active there.
            var page2Result = PdfGenerator.SelectPageRule(rules, pageNumber: 2, elements, pageY: 800, pageHeight: 800);
            Assert.Same(chapter2Rule, page2Result);
        }

        [Fact]
        public void FirstPseudoSelector_OutranksBaseAndNamedRule()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter { margin: 20mm; }
                @page :first { margin: 30mm; }
                """);
            var firstRule = rules[2];
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800);

            Assert.Same(firstRule, result);
        }

        [Fact]
        public void FirstPseudoSelector_OnlyAppliesToPageOne()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page :first { margin: 30mm; }
                """);
            var baseRule = rules[0];

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 2, [], pageY: 800, pageHeight: 800);

            Assert.Same(baseRule, result);
        }

        // ─── Compound name:pseudo selectors ─────────────────────────────────────
        // Regression coverage for a parser bug found alongside the primary Y-timing one: "@page
        // chapter1:left { ... }" used to fail to parse at all (CreatePageSelector stopped consuming
        // tokens at the first non-comma token after an ident), silently dropping the entire rule -
        // discovered because css4.pub's real dictionary CSS uses exactly this compound form for page
        // numbers ("@page chapter1:left, ..., chapter8:left { @bottom-left { content: counter(page) } }").

        [Fact]
        public void CompoundSelector_MatchesOnlyWhenBothNameAndPseudoMatch()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter1:left { margin: 20mm; }
                """);
            var baseRule = rules[0];
            var compoundRule = rules[1];
            var elements = new List<NamedPageElement> { new("chapter1", 0) };

            // Name matches, page is left (even) -> compound rule applies.
            Assert.Same(compoundRule,
                PdfGenerator.SelectPageRule(rules, pageNumber: 2, elements, pageY: 800, pageHeight: 800));

            // Name matches but page is right (odd) -> pseudo half fails, base rule applies.
            Assert.Same(baseRule,
                PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800));

            // Page is left but the active name is different -> name half fails, base rule applies.
            var otherElements = new List<NamedPageElement> { new("chapter2", 0) };
            Assert.Same(baseRule,
                PdfGenerator.SelectPageRule(rules, pageNumber: 2, otherElements, pageY: 800, pageHeight: 800));
        }

        [Fact]
        public void CompoundSelector_CommaSeparatedList_EachEntryIndependentlyValid()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter1:left, chapter2:left { margin: 20mm; }
                """);
            var compoundRule = rules[1];
            var elements = new List<NamedPageElement> { new("chapter2", 0) };

            var result = PdfGenerator.SelectPageRule(rules, pageNumber: 2, elements, pageY: 800, pageHeight: 800);

            Assert.Same(compoundRule, result);
        }

        [Fact]
        public void CompoundSelector_OutranksPseudoAloneRegardlessOfOrder()
        {
            // Specificity, not source order: a compound "chapter1:left" (name+pseudo) must beat a bare
            // ":left" whichever one is declared first.
            var elements = new List<NamedPageElement> { new("chapter1", 0) };

            var compoundFirst = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter1:left { margin: 20mm; }
                @page :left { margin: 30mm; }
                """);
            Assert.Same(compoundFirst[1],
                PdfGenerator.SelectPageRule(compoundFirst, pageNumber: 2, elements, pageY: 800, pageHeight: 800));

            var pseudoFirst = ParsePageRules("""
                @page { margin: 10mm; }
                @page :left { margin: 30mm; }
                @page chapter1:left { margin: 20mm; }
                """);
            Assert.Same(pseudoFirst[2],
                PdfGenerator.SelectPageRule(pseudoFirst, pageNumber: 2, elements, pageY: 800, pageHeight: 800));
        }

        [Fact]
        public void PlainNamedRule_StillOutranksPseudoAlone_AsBefore()
        {
            // Behavior change from pure source-order: a plain named rule (specificity 2) now outranks a
            // bare :left/:right pseudo-class rule (specificity 1) regardless of which is declared later.
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var namedFirst = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter { margin: 20mm; }
                @page :left { margin: 30mm; }
                """);
            Assert.Same(namedFirst[1],
                PdfGenerator.SelectPageRule(namedFirst, pageNumber: 2, elements, pageY: 0, pageHeight: 800));

            var pseudoFirst = ParsePageRules("""
                @page { margin: 10mm; }
                @page :left { margin: 30mm; }
                @page chapter { margin: 20mm; }
                """);
            Assert.Same(pseudoFirst[2],
                PdfGenerator.SelectPageRule(pseudoFirst, pageNumber: 2, elements, pageY: 0, pageHeight: 800));
        }

        [Fact]
        public void FirstPseudoSelector_StillOutranksEverythingRegardlessOfOrder()
        {
            // Regression: :first must remain an unconditional override, not folded into the additive
            // name/pseudo specificity score (it must keep beating a plain named rule, unlike :left/:right).
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page chapter { margin: 20mm; }
                @page :first { margin: 30mm; }
                """);
            Assert.Same(rules[2],
                PdfGenerator.SelectPageRule(rules, pageNumber: 1, elements, pageY: 0, pageHeight: 800));

            var reordered = ParsePageRules("""
                @page { margin: 10mm; }
                @page :first { margin: 30mm; }
                @page chapter { margin: 20mm; }
                """);
            Assert.Same(reordered[1],
                PdfGenerator.SelectPageRule(reordered, pageNumber: 1, elements, pageY: 0, pageHeight: 800));
        }

        [Fact]
        public void LeftRightPseudoSelectors_AlternateByPageParity()
        {
            var rules = ParsePageRules("""
                @page { margin: 10mm; }
                @page :left { margin: 20mm; }
                @page :right { margin: 30mm; }
                """);
            var leftRule = rules[1];
            var rightRule = rules[2];

            Assert.Same(rightRule, PdfGenerator.SelectPageRule(rules, pageNumber: 1, [], pageY: 0, pageHeight: 800));
            Assert.Same(leftRule, PdfGenerator.SelectPageRule(rules, pageNumber: 2, [], pageY: 800, pageHeight: 800));
            Assert.Same(rightRule, PdfGenerator.SelectPageRule(rules, pageNumber: 3, [], pageY: 1600, pageHeight: 800));
        }

        // ─── SelectApplicableMarginRules: cascade merge across matching rules ───
        // Regression for a bug found while verifying the compound name:pseudo selector fix above against
        // the real css4.pub dictionary page: @page rules cascade per-declaration like any other CSS, but
        // SelectPageRule only ever returns ONE winning rule. Naively rendering only that one rule's
        // margin boxes meant a page matching both a low-specificity base named rule (defining
        // @top-left/@top-center/@top-right, e.g. running headers) AND a higher-specificity compound
        // "name:left"/"name:right" rule (defining only @bottom-left/@bottom-right) lost the base rule's
        // headers entirely — confirmed by an actual rasterized re-render of the live dictionary page,
        // where running headers vanished the moment compound selectors started matching.

        [Fact]
        public void SelectApplicableMarginRules_MergesBaseAndCompoundRuleMarginsByName()
        {
            var rules = ParsePageRules("""
                @page chapter { @top-left { content: "Term"; } @top-center { content: "Letter"; } }
                @page chapter:left { @bottom-left { content: counter(page); } }
                """);
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var margins = PdfGenerator.SelectApplicableMarginRules(rules, pageNumber: 2, elements, pageY: 0, pageHeight: 800);
            var boxNames = margins.Select(m => m.Selector?.Text?.Trim().ToLowerInvariant()).ToList();

            // Both the base rule's headers AND the compound rule's page number must be present together.
            Assert.Contains("top-left", boxNames);
            Assert.Contains("top-center", boxNames);
            Assert.Contains("bottom-left", boxNames);
        }

        [Fact]
        public void SelectApplicableMarginRules_MoreSpecificRuleWinsForTheSameBoxName()
        {
            var rules = ParsePageRules("""
                @page chapter { @top-left { content: "Base"; } }
                @page chapter:left { @top-left { content: "Compound"; } }
                """);
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var margins = PdfGenerator.SelectApplicableMarginRules(rules, pageNumber: 2, elements, pageY: 0, pageHeight: 800);
            var topLeft = Assert.Single(margins, m => m.Selector?.Text?.Trim().ToLowerInvariant() == "top-left");

            Assert.Equal("\"Compound\"", topLeft.Style.Content);
        }

        [Fact]
        public void SelectApplicableMarginRules_OnlyBaseRuleMatches_ReturnsItsMarginsAlone()
        {
            var rules = ParsePageRules("""
                @page { @top-center { content: "Base only"; } }
                """);

            var margins = PdfGenerator.SelectApplicableMarginRules(rules, pageNumber: 1, [], pageY: 0, pageHeight: 800);

            var box = Assert.Single(margins);
            Assert.Equal("top-center", box.Selector?.Text?.Trim().ToLowerInvariant());
        }

        [Fact]
        public void SelectApplicableMarginRules_NoRulesMatch_ReturnsEmpty()
        {
            var margins = PdfGenerator.SelectApplicableMarginRules([], pageNumber: 1, [], pageY: 0, pageHeight: 800);

            Assert.Empty(margins);
        }

        [Fact]
        public void SelectApplicableMarginRules_PreservesLessSpecificRulesUnredeclaredProperty()
        {
            // Regression for the per-declaration (not whole-rule) cascade gap: the base rule's @top-left
            // sets both `content` and `font-family`; the more specific compound rule's @top-left only
            // redeclares `content`. Real CSS cascade (and Prince) resolve per-declaration, so the base
            // rule's font-family must survive - whole-rule-wins semantics would have silently dropped it.
            var rules = ParsePageRules("""
                @page chapter { @top-left { content: "Base"; font-family: "Satyr10"; } }
                @page chapter:left { @top-left { content: "Compound"; } }
                """);
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var margins = PdfGenerator.SelectApplicableMarginRules(rules, pageNumber: 2, elements, pageY: 0, pageHeight: 800);
            var topLeft = Assert.Single(margins, m => m.Selector?.Text?.Trim().ToLowerInvariant() == "top-left");

            Assert.Equal("\"Compound\"", topLeft.Style.Content);
            Assert.Equal("\"Satyr10\"", topLeft.Style.FontFamily);
        }

        [Fact]
        public void SelectApplicablePageStyle_MergesAcrossMatchingRules()
        {
            var rules = ParsePageRules("""
                @page chapter { font-family: "Satyr10"; }
                @page chapter:left { margin: 20mm; }
                """);
            var elements = new List<NamedPageElement> { new("chapter", 0) };

            var style = PdfGenerator.SelectApplicablePageStyle(rules, pageNumber: 2, elements, pageY: 0, pageHeight: 800);

            Assert.NotNull(style);
            Assert.Equal("\"Satyr10\"", style!.FontFamily);
        }

        [Fact]
        public void SelectApplicablePageStyle_ReturnsNull_WhenNoRuleMatches()
        {
            var style = PdfGenerator.SelectApplicablePageStyle([], pageNumber: 1, [], pageY: 0, pageHeight: 800);

            Assert.Null(style);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static List<PageRule> ParsePageRules(string css) =>
            new StylesheetParser().Parse(css).Rules.OfType<PageRule>().ToList();
    }
}
