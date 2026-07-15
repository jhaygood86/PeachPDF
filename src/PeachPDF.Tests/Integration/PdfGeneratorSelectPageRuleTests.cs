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

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static List<PageRule> ParsePageRules(string css) =>
            new StylesheetParser().Parse(css).Rules.OfType<PageRule>().ToList();
    }
}
