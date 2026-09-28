using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.CSS
{
    public class AttrSelectorTests
    {
        [Fact]
        public async Task FindAllAttrMatchSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("");

            // Act
            var list = GetAttributeStyleRules<AttrMatchSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrInListSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("~");

            // Act
            var list = GetAttributeStyleRules<AttrListSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrHyphenSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("|");

            // Act
            var list = GetAttributeStyleRules<AttrHyphenSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrBeginsSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("^");

            // Act
            var list = GetAttributeStyleRules<AttrBeginsSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrEndsSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("$");

            // Act
            var list = GetAttributeStyleRules<AttrEndsSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrContainsSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("*");

            // Act
            var list = GetAttributeStyleRules<AttrContainsSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task FindAllAttrNotMatchSelectorsThatMatchAttributeName()
        {
            // Arrange
            var sheet = await GetAttributeStylesheetAsync("!");

            // Act
            var list = GetAttributeStyleRules<AttrNotMatchSelector>(sheet);

            // Assert
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task BareAttributePresenceSelector_ResolvesToAttrAvailableSelector()
        {
            // A `[attr]` with no combinator/value falls through the factory's dispatch to the presence
            // selector (the switch `_` arm) - the one branch the combinator theory above doesn't cover.
            var sheet = await new StylesheetParser().ParseAsync(
                "[type] { background-color: #101010 } .sample-class[type] { background-color: #121212 }",
                TestContext.Current.CancellationToken);

            var list = GetAttributeStyleRules<AttrAvailableSelector>(sheet);

            Assert.Equal(2, list.Count());
        }

        [Theory]
        [InlineData("[type=button i]", "Insensitive", "[type=\"button\" i]")]
        [InlineData("[type=button I]", "Insensitive", "[type=\"button\" i]")]
        [InlineData("[type=\"button\"i]", "Insensitive", "[type=\"button\" i]")]
        [InlineData("[type = button   s ]", "Sensitive", "[type=\"button\" s]")]
        [InlineData("[type=button S]", "Sensitive", "[type=\"button\" s]")]
        [InlineData("[type=button]", "Default", "[type=\"button\"]")]
        public async Task CaseSensitivityModifier_IsParsedAndSerialized(string selector, string expected, string text)
        {
            var sheet = await new StylesheetParser().ParseAsync(
                selector + " { color: red }", TestContext.Current.CancellationToken);

            var rule = Assert.Single(sheet.StyleRules);
            var attr = Assert.IsType<AttrMatchSelector>(rule.Selector);

            Assert.Equal(expected, attr.CaseSensitivity.ToString());
            Assert.Equal("button", attr.Value);
            Assert.Equal(text, attr.Text);
        }

        [Theory]
        [InlineData("~=", typeof(AttrListSelector))]
        [InlineData("|=", typeof(AttrHyphenSelector))]
        [InlineData("^=", typeof(AttrBeginsSelector))]
        [InlineData("$=", typeof(AttrEndsSelector))]
        [InlineData("*=", typeof(AttrContainsSelector))]
        public async Task CaseSensitivityModifier_IsAcceptedOnEveryValueOperator(string op, System.Type type)
        {
            var sheet = await new StylesheetParser().ParseAsync(
                "[type" + op + "button i] { color: red }", TestContext.Current.CancellationToken);

            var rule = Assert.Single(sheet.StyleRules);

            Assert.IsType(type, rule.Selector);
            Assert.Equal(AttrCaseSensitivity.Insensitive, ((IAttrSelector)rule.Selector).CaseSensitivity);
        }

        [Theory]
        [InlineData("[type i]")]               // a modifier needs a value to modify
        [InlineData("[type=button x]")]        // only i and s are modifiers
        [InlineData("[type=button i i]")]      // one modifier at most
        [InlineData("[type=button i s]")]
        [InlineData("[type=button i extra]")]
        public async Task CaseSensitivityModifier_InvalidForms_DropTheRule(string selector)
        {
            var sheet = await new StylesheetParser().ParseAsync(
                selector + " { color: red } p { color: blue }", TestContext.Current.CancellationToken);

            var rule = Assert.Single(sheet.StyleRules);

            Assert.Equal("p", rule.SelectorText);
        }

        [Theory]
        // `data-x` isn't on the HTML Standard's fixed legacy-attribute list (issue #1384), so the
        // unmodified default is case-sensitive - only the explicit `i` modifier rows below match.
        [InlineData("[data-x=abc]", false)]
        [InlineData("[data-x=abc i]", true)]
        [InlineData("[data-x=abc s]", false)]
        [InlineData("[data-x=ABC s]", true)]
        [InlineData("[data-x~=abc i]", true)]
        [InlineData("[data-x~=abc s]", false)]
        [InlineData("[data-x~=ABC s]", true)]
        [InlineData("[data-x^=ab i]", true)]
        [InlineData("[data-x^=ab s]", false)]
        [InlineData("[data-x^=AB s]", true)]
        [InlineData("[data-x$=bc i]", true)]
        [InlineData("[data-x$=bc s]", false)]
        [InlineData("[data-x$=BC s]", true)]
        [InlineData("[data-x*=b i]", true)]
        [InlineData("[data-x*=b s]", false)]
        [InlineData("[data-x*=B s]", true)]
        public async Task CaseSensitivityModifier_DecidesHowTheValueIsCompared(string selector, bool matches)
        {
            // The attribute is written in upper case; the selectors spell their value in lower case
            // except where the `s` row spells it in upper case to prove a sensitive match still matches.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<style>{selector} {{ color: rgb(255, 0, 0) }}</style><p id='x' data-x='ABC'>x</p>"));

            var color = LayoutHarness.FindById(root, "x")!.Color;

            Assert.Equal(matches, color == "rgb(255, 0, 0)");
        }

        [Theory]
        [InlineData("[data-x|=en i]", true)]
        [InlineData("[data-x|=en s]", false)]
        [InlineData("[data-x|=EN s]", true)]
        public async Task CaseSensitivityModifier_AppliesToTheHyphenOperator(string selector, bool matches)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<style>{selector} {{ color: rgb(255, 0, 0) }}</style><p id='x' data-x='EN-us'>x</p>"));

            var color = LayoutHarness.FindById(root, "x")!.Color;

            Assert.Equal(matches, color == "rgb(255, 0, 0)");
        }

        [Theory]
        // `type` is on the HTML Standard's fixed legacy-attribute list (§4.16.2) - the unmodified
        // default stays ASCII case-insensitive for it, same as the engine's behavior before #1384.
        [InlineData("[type=BUTTON]", true)]
        [InlineData("[type~=BUTTON]", true)]
        [InlineData("[type^=BUT]", true)]
        [InlineData("[type$=TON]", true)]
        [InlineData("[type*=UTT]", true)]
        [InlineData("[type=BUTTON s]", false)] // an explicit `s` still overrides the list either way
        // `data-x` is not on the list - #1384's fix: the unmodified default is now case-sensitive.
        [InlineData("[data-x=ABC]", false)]
        [InlineData("[data-x~=ABC]", false)]
        [InlineData("[data-x^=AB]", false)]
        [InlineData("[data-x$=BC]", false)]
        [InlineData("[data-x*=B]", false)]
        [InlineData("[data-x=ABC i]", true)] // an explicit `i` still opts back into the lenient match
        public async Task DefaultCaseSensitivity_FollowsTheHtmlLegacyAttributeList(string selector, bool matches)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<style>{selector} {{ color: rgb(255, 0, 0) }}</style><p id='x' type='button' data-x='abc'>x</p>"));

            var color = LayoutHarness.FindById(root, "x")!.Color;

            Assert.Equal(matches, color == "rgb(255, 0, 0)");
        }

        private async Task<Stylesheet> GetAttributeStylesheetAsync(string combinator)
        {
            var css = @"[type" + combinator + "='button'] { background-color: #101010 } .sample-class[type"
                      + combinator + "='input'] { background-color: #121212 }";

            return await new StylesheetParser().ParseAsync(css);
        }

        private IEnumerable<IStyleRule> GetAttributeStyleRules<T>(Stylesheet sheet) where T : IAttrSelector
        {
            return sheet.StyleRules
                .Where(x =>
                    (x.Selector is CompoundSelector selector &&
                     selector.Any(y => y is T { Attribute: "type" }))
                    || x.Selector is T { Attribute: "type" }
                );
        }
    }
}






