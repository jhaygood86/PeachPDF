namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class FloatClearPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        [InlineData("none")]
        public void FloatKeywordLegal(string keyword)
        {
            var snippet = $"float: {keyword}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("float", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FloatProperty>(property);
            var concrete = (FloatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(keyword, concrete.Value);
        }

        [Fact]
        public void FloatInvalidKeywordIllegal()
        {
            var snippet = "float: middle";
            var property = ParseDeclaration(snippet);
            Assert.Equal("float", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FloatProperty>(property);
            var concrete = (FloatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        [InlineData("both")]
        [InlineData("none")]
        public void ClearKeywordLegal(string keyword)
        {
            var snippet = $"clear: {keyword}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("clear", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ClearProperty>(property);
            var concrete = (ClearProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(keyword, concrete.Value);
        }

        [Fact]
        public void ClearInvalidKeywordIllegal()
        {
            var snippet = "clear: middle";
            var property = ParseDeclaration(snippet);
            Assert.Equal("clear", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ClearProperty>(property);
            var concrete = (ClearProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }
    }
}
