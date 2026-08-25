namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class TextOverflowPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("clip")]
        [InlineData("ellipsis")]
        public void TextOverflowKeywordLegal(string keyword)
        {
            var snippet = $"text-overflow: {keyword}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-overflow", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TextOverflowProperty>(property);
            var concrete = (TextOverflowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(keyword, concrete.Value);
        }

        [Fact]
        public void TextOverflowInvalidKeywordIllegal()
        {
            var snippet = "text-overflow: banana";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-overflow", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TextOverflowProperty>(property);
            var concrete = (TextOverflowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }
    }
}
