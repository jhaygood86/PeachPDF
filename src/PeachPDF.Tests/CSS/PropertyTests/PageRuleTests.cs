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

        // ── ParseLengthToPdfPoints ─────────────────────────────────────────────

        [Theory]
        [InlineData("72pt",  72.0)]
        [InlineData("1in",   72.0)]
        [InlineData("2.54cm", 72.0)]
        [InlineData("25.4mm", 72.0)]
        [InlineData("96px",  72.0)]
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
    }
}
