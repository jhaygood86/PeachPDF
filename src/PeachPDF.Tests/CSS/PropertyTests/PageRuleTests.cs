using PeachPDF.CSS;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class PageRuleTests : CssConstructionFunctions
    {
        // ── @page { size: ... } ────────────────────────────────────────────────

        [Fact]
        public void AtPage_SizeA4_IsStoredOnStyleDeclaration()
        {
            var sheet = ParseStyleSheet("@page { size: A4; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("A4", rule.Style.Size);
        }

        [Fact]
        public void AtPage_SizeLetter_IsStoredOnStyleDeclaration()
        {
            var sheet = ParseStyleSheet("@page { size: letter; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("letter", rule.Style.Size);
        }

        [Fact]
        public void AtPage_SizeA4Landscape_IsStoredOnStyleDeclaration()
        {
            var sheet = ParseStyleSheet("@page { size: A4 landscape; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("A4 landscape", rule.Style.Size);
        }

        [Fact]
        public void AtPage_SizeExplicitDimensions_IsStoredOnStyleDeclaration()
        {
            var sheet = ParseStyleSheet("@page { size: 210mm 297mm; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("210mm 297mm", rule.Style.Size);
        }

        // ── @page pseudo-selectors ─────────────────────────────────────────────

        [Fact]
        public void AtPage_NoSelector_HasNullSelector()
        {
            var sheet = ParseStyleSheet("@page { margin: 20mm; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Null(rule.Selector);
        }

        [Fact]
        public void AtPage_FirstPseudoSelector_SelectorTextIsFirst()
        {
            var sheet = ParseStyleSheet("@page :first { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(":first", rule.SelectorText);
        }

        [Fact]
        public void AtPage_LeftPseudoSelector_SelectorTextIsLeft()
        {
            var sheet = ParseStyleSheet("@page :left { margin-left: 30mm; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(":left", rule.SelectorText);
        }

        [Fact]
        public void AtPage_RightPseudoSelector_SelectorTextIsRight()
        {
            var sheet = ParseStyleSheet("@page :right { margin-right: 30mm; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(":right", rule.SelectorText);
        }

        // ── @page named selectors ──────────────────────────────────────────────
        // Regression coverage: a bare identifier selector used to make the whole rule fail to parse
        // (CreatePageSelector only handled the leading-colon pseudo-class case), so `@page chapter { }`
        // silently vanished instead of producing a PageRule at all.

        [Fact]
        public void AtPage_NamedSelector_SelectorTextIsName()
        {
            var sheet = ParseStyleSheet("@page chapter { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("chapter", rule.SelectorText);
        }

        [Fact]
        public void AtPage_NamedSelector_IsNotColonPrefixed()
        {
            // Distinguishes a named-page selector from a pseudo-class one: SelectPageRule's matching
            // logic branches on whether the selector text starts with ':'.
            var sheet = ParseStyleSheet("@page chapter { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.False(rule.SelectorText.StartsWith(':'));
        }

        [Fact]
        public void AtPage_CommaSeparatedNamedSelectors_AllNamesPresent()
        {
            var sheet = ParseStyleSheet("@page chapter1, chapter2, chapter3 { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("chapter1, chapter2, chapter3", rule.SelectorText);
        }

        [Fact]
        public void AtPage_NamedSelector_PreservesCase()
        {
            var sheet = ParseStyleSheet("@page Chapter { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("Chapter", rule.SelectorText);
        }

        // ── @page compound name:pseudo selectors ───────────────────────────────
        // Regression coverage: "@page chapter1:left { }" used to fail to parse entirely
        // (CreatePageSelector stopped consuming tokens at the first non-comma token after an ident),
        // silently dropping the whole rule - discovered because css4.pub's real dictionary CSS uses
        // exactly this compound form for page numbers.

        [Fact]
        public void AtPage_CompoundNamePseudoSelector_ParsesAsOneRule()
        {
            var sheet = ParseStyleSheet("@page chapter1:left { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("chapter1:left", rule.SelectorText);
        }

        [Fact]
        public void AtPage_CompoundCommaSeparatedSelectors_AllEntriesPresent()
        {
            var sheet = ParseStyleSheet("@page chapter1:left, chapter2:left { margin-top: 0; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("chapter1:left, chapter2:left", rule.SelectorText);
        }

        // ── @page margin boxes ─────────────────────────────────────────────────

        [Fact]
        public void AtPage_TopCenterMarginBox_IsParsedIntoMargins()
        {
            var sheet = ParseStyleSheet(
                "@page { @top-center { content: \"Page\"; } }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var margin = rule.Margins.FirstOrDefault();
            Assert.NotNull(margin);
        }

        [Fact]
        public void AtPage_TopCenterMarginBox_HasTopCenterSelector()
        {
            var sheet = ParseStyleSheet(
                "@page { @top-center { content: counter(page); } }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var margin = rule.Margins
                .FirstOrDefault(m => m.Selector?.Text?.Contains("top-center") == true);
            Assert.NotNull(margin);
        }

        [Fact]
        public void AtPage_TopCenterMarginBox_ContentIsNonEmpty()
        {
            var sheet = ParseStyleSheet(
                "@page { @top-center { content: \"Header\"; } }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var margin = rule.Margins.FirstOrDefault();
            Assert.NotNull(margin);
            Assert.False(string.IsNullOrEmpty(margin.Style.Content));
        }

        [Fact]
        public void AtPage_MultipleMarginBoxes_AllParsed()
        {
            var sheet = ParseStyleSheet(@"
                @page {
                    @top-left   { content: ""Left""; }
                    @top-center { content: ""Center""; }
                    @top-right  { content: ""Right""; }
                }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(3, rule.Margins.Count());
        }

        // ── per-page margin variation ──────────────────────────────────────────

        [Fact]
        public void AtPage_FirstPseudoSelector_MarginTopIsParseable()
        {
            var sheet = ParseStyleSheet("@page :first { margin-top: 40mm; }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(":first", rule.SelectorText);

            var pt = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints(rule.Style.MarginTop);
            Assert.NotNull(pt);
            // 40mm = 40 * 72 / 25.4 ≈ 113.4pt
            Assert.Equal(113.4, pt!.Value, 0);
        }

        // ── named pages ────────────────────────────────────────────────────────

        [Fact]
        public void PageNameProperty_Identifier_ParsesCorrectly()
        {
            var sheet = ParseStyleSheet("div { page: chapter; }");
            var block = sheet.Rules.OfType<StyleRule>().FirstOrDefault();
            Assert.NotNull(block);
            Assert.Equal("chapter", block.Style.PageName);
        }

        [Fact]
        public void PageNameProperty_Auto_ParsesCorrectly()
        {
            var sheet = ParseStyleSheet("div { page: auto; }");
            var block = sheet.Rules.OfType<StyleRule>().FirstOrDefault();
            Assert.NotNull(block);
            Assert.Equal("auto", block.Style.PageName);
        }

        // ── margin box explicit width ───────────────────────────────────────────

        [Fact]
        public void AtPage_MarginBox_ExplicitWidthIsParsedFromStyle()
        {
            var sheet = ParseStyleSheet(
                "@page { @top-left { content: \"L\"; width: 100pt; } }");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var margin = rule.Margins.FirstOrDefault(m =>
                m.Selector?.Text?.Contains("top-left") == true);
            Assert.NotNull(margin);
            Assert.Equal("100pt", margin.Style.Width);
        }

        // ── ParseLengthToPdfPoints ─────────────────────────────────────────────

        [Theory]
        [InlineData("72pt",  72.0)]
        [InlineData("1in",   72.0)]
        [InlineData("2.54cm", 72.0)]
        [InlineData("25.4mm", 72.0)]
        [InlineData("96px",  72.0)]
        [InlineData("0",     0.0)]  // unitless zero — the CSS-OM serializes every zero length this way (#125)
        [InlineData("0mm",   0.0)]
        [InlineData("0.5in", 36.0)]
        [InlineData("6pc",   72.0)]
        [InlineData("72PT",  72.0)] // units are ASCII case-insensitive per CSS Syntax
        public void ParseLengthToPdfPoints_ConvertsUnitsCorrectly(string input, double expectedPt)
        {
            var result = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints(input);
            Assert.NotNull(result);
            Assert.Equal(expectedPt, result!.Value, 1);
        }

        [Fact]
        public void ParseLengthToPdfPoints_ReturnsNull_ForInvalidInput()
        {
            var result = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints("not-a-length");
            Assert.Null(result);
        }

        [Fact]
        public void ParseLengthToPdfPoints_UnitlessNonZero_ReturnsNull()
        {
            // CSS Values & Units §5.1: only zero may omit its unit — and the CSS-OM's
            // Length.ToString() only ever serializes zero unitless, so "5" can't occur
            // in CSS-OM-sourced input anyway.
            var result = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints("5");
            Assert.Null(result);
        }

        [Theory]
        [InlineData("2em")]
        [InlineData("50%")]
        public void ParseLengthToPdfPoints_RelativeUnits_ReturnNull(string input)
        {
            // Relative units have no resolution context at the page-geometry layer.
            var result = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints(input);
            Assert.Null(result);
        }

        [Theory]
        [InlineData("margin: 0")]
        [InlineData("margin: 0mm")]
        public void AtPage_FirstPseudoSelector_ZeroMargin_RoundTripsToZeroPoints(string declaration)
        {
            // #125: the CSS-OM serializes every zero length unitless, so the page-margin
            // resolver receives the string "0" and must resolve it to 0pt, not "unset".
            var sheet = ParseStyleSheet($"@page :first {{ {declaration}; }}");
            var rule = sheet.Rules.OfType<PageRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal("0", rule.Style.MarginTop);

            var pt = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints(rule.Style.MarginTop);
            Assert.NotNull(pt);
            Assert.Equal(0.0, pt!.Value);
        }
    }
}
