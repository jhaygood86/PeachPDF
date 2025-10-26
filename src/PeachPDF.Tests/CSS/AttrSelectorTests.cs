using PeachPDF.CSS;

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






