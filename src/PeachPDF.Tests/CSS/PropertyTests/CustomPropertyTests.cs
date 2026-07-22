using PeachPDF.CSS;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    /// <summary>
    /// Unit tests verifying that the CSS module correctly parses custom properties (--foo) and
    /// tolerates the var() function inside any property's value. Cascade-level resolution of
    /// var() is tested separately in Integration/CustomPropertiesIntegrationTests.cs.
    /// </summary>
    public class CustomPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CustomProperty_SimpleValue_ParsesAndRoundTrips()
        {
            var property = ParseDeclaration("--main-color: red");
            Assert.Equal("--main-color", property.Name);
            Assert.IsType<CustomProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal("red", property.Value);
        }

        [Fact]
        public void CustomProperty_ArbitraryTokenSoup_NeverFailsToParse()
        {
            var property = ParseDeclaration("--x: 1px solid   red , foo(bar)");
            Assert.Equal("--x", property.Name);
            Assert.IsType<CustomProperty>(property);
            Assert.True(property.HasValue);
        }

        [Theory]
        [InlineData("--x: #hero", "#hero")]   // an id-shaped hash-token value (CSS Syntax accepts it)
        [InlineData("--x: #f00", "#f00")]      // a hex color hash
        public void CustomProperty_HashValue_ParsesAndRoundTrips(string snippet, string expected)
        {
            var property = ParseDeclaration(snippet);
            Assert.Equal("--x", property.Name);
            Assert.IsType<CustomProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal(expected, property.Value);
        }

        [Fact]
        public void CustomProperty_WithVarReference_RoundTripsLiterally()
        {
            var property = ParseDeclaration("--b: var(--a, blue)");
            Assert.Equal("--b", property.Name);
            Assert.True(property.HasValue);
            Assert.Contains("var(--a, blue)", property.Value);
        }

        [Fact]
        public void CustomProperty_NameIsCaseSensitive()
        {
            var lower = ParseDeclaration("--foo: 1px");
            var upper = ParseDeclaration("--Foo: 2px");
            Assert.Equal("--foo", lower.Name);
            Assert.Equal("--Foo", upper.Name);
        }

        [Fact]
        public void CustomProperty_MultiHyphenatedName_ParsesAsSingleIdentifier()
        {
            var property = ParseDeclaration("--foo-bar-baz: 1px");
            Assert.Equal("--foo-bar-baz", property.Name);
        }

        [Fact]
        public void ColorProperty_WithVarFunction_DoesNotFailToParse()
        {
            var property = ParseDeclaration("color: var(--main-color, red)");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.Contains("var(--main-color, red)", property.Value);
        }

        [Fact]
        public void MarginProperty_WithMultipleVarFunctions_DoesNotFailToParse()
        {
            var property = ParseDeclaration("margin: var(--a) var(--b)");
            Assert.Equal("margin", property.Name);
            Assert.True(property.HasValue);
        }

        [Fact]
        public void MarginShorthandInStyleSheet_WithVar_KeepsShorthandWholeUntilCascadeTime()
        {
            // Shorthand-to-longhand expansion happens at parse time (StyleDeclaration.SetShorthand);
            // a var() reference can't be split into per-longhand slices until it's resolved per-element,
            // so the shorthand must survive intact for CssUtils.SetPropertyValue to expand post-substitution.
            var sheet = ParseStyleSheet("* { margin: var(--a) var(--b); }");
            var rule = sheet.StyleRules.Single();
            var names = rule.Style.Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "margin" }, names);
        }

        [Fact]
        public void Property_WithNestedVarInsideOtherFunction_DoesNotFailToParse()
        {
            var property = ParseDeclaration("background-image: linear-gradient(var(--c1), var(--c2))");
            Assert.Equal("background-image", property.Name);
            Assert.True(property.HasValue);
        }

        // ── Lexer regression coverage for the -- identifier-start fix ──────────

        [Fact]
        public void HtmlCommentClose_StillTokenizesAsCdc_NotAffectedByDashFix()
        {
            var stylesheet = ParseStyleSheet("<!-- .a { color: red; } -->");
            Assert.NotEmpty(stylesheet.StyleRules);
        }

        [Fact]
        public void VendorPrefixedProperty_StillParsesAsUnknownSingleHyphenIdent()
        {
            var property = ParseDeclaration("-webkit-transform: none", includeUnknownDeclarations: true);
            Assert.Equal("-webkit-transform", property.Name);
        }

        [Fact]
        public void NegativeLength_StillParsesCorrectly()
        {
            var property = ParseDeclaration("margin-top: -5px");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("-5px", property.Value);
        }
    }
}
