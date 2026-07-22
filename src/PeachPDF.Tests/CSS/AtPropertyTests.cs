namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    public class AtPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void AtProperty_ParsesNameAndDescriptors()
        {
            var src = "@property --my-color { syntax: \"<color>\"; inherits: false; initial-value: #c0ffee; }";
            var sheet = ParseStyleSheet(src);

            Assert.NotNull(sheet);
            var rule = Assert.IsType<PropertyRule>(sheet.Rules.OfType<PropertyRule>().Single());
            Assert.Equal("--my-color", rule.Name);
            Assert.Contains("color", rule.Syntax);
            Assert.Equal("false", rule.Inherits);
            Assert.Equal("#c0ffee", rule.InitialValue);
        }

        [Fact]
        public void AtProperty_UniversalSyntax_NoInitialValue()
        {
            var src = "@property --x { syntax: \"*\"; inherits: true; }";
            var sheet = ParseStyleSheet(src);

            var rule = sheet.Rules.OfType<PropertyRule>().Single();
            Assert.Equal("--x", rule.Name);
            Assert.Contains("*", rule.Syntax);
            Assert.Equal("true", rule.Inherits);
            Assert.Equal("", rule.InitialValue);
        }

        [Fact]
        public void AtProperty_DoesNotDerailFollowingRules()
        {
            // Regression: an @property rule must not swallow or drop the rules that follow it. Before real
            // @property parsing, the rule routed to CreateUnknown and (the whole point of this feature) was
            // silently dropped; the following style rule must still parse and apply.
            var src = "@property --gap { syntax: \"<length>\"; inherits: false; initial-value: 4px; } .after { color: red; }";
            var sheet = ParseStyleSheet(src);

            Assert.Single(sheet.Rules.OfType<PropertyRule>());
            var styleRule = Assert.IsType<StyleRule>(sheet.Rules.OfType<StyleRule>().Single());
            Assert.Equal(".after", styleRule.SelectorText);
            // Named colors are normalized to rgb() at parse time.
            Assert.Equal("rgb(255, 0, 0)", styleRule.Style.GetPropertyValue("color"));
        }

        [Fact]
        public void AtProperty_NoDeclarationBlock_DoesNotCrashAndFollowingRuleApplies()
        {
            // A malformed @property with no { } block must be skipped without derailing the next rule.
            var src = "@property --x; .after { color: red; }";
            var sheet = ParseStyleSheet(src);

            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
            Assert.Equal("rgb(255, 0, 0)", styleRule.Style.GetPropertyValue("color"));
        }

        [Fact]
        public void AtProperty_Setters_And_ToCss_RoundTrip()
        {
            // Start from a rule that only declares `syntax`, so setting initial-value/inherits exercises the
            // create-new-descriptor path, and re-setting syntax exercises the replace-existing path.
            var src = "@property --p { syntax: \"<length>\"; }";
            var rule = (PropertyRule)ParseStyleSheet(src).Rules.OfType<PropertyRule>().Single();

            rule.InitialValue = "1px";              // new descriptor
            rule.Inherits = "true";                 // new descriptor
            rule.Syntax = "\"<color>\"";            // replace existing descriptor
            Assert.Contains("color", rule.Syntax);
            Assert.Equal("1px", rule.InitialValue);
            Assert.Equal("true", rule.Inherits);

            var css = rule.ToCss();
            Assert.Contains("@property --p", css);
            Assert.Contains("initial-value", css);
        }

        [Fact]
        public void AtProperty_MultipleRules_AllRegister()
        {
            var src = "@property --a { syntax: \"<number>\"; inherits: false; initial-value: 0; }" +
                      "@property --b { syntax: \"<percentage>\"; inherits: true; initial-value: 50%; }";
            var sheet = ParseStyleSheet(src);

            var rules = sheet.Rules.OfType<PropertyRule>().ToList();
            Assert.Equal(2, rules.Count);
            Assert.Equal("--a", rules[0].Name);
            Assert.Equal("--b", rules[1].Name);
        }
    }
}
