namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class VerticalAlignPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("baseline")]
        [InlineData("sub")]
        [InlineData("super")]
        [InlineData("top")]
        [InlineData("text-top")]
        [InlineData("middle")]
        [InlineData("bottom")]
        [InlineData("text-bottom")]
        public void VerticalAlignKeywordLegal(string keyword)
        {
            var snippet = $"vertical-align: {keyword}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("vertical-align", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<VerticalAlignProperty>(property);
            var concrete = (VerticalAlignProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(keyword, concrete.Value);
        }

        [Fact]
        public void VerticalAlignLengthLegal()
        {
            var snippet = "vertical-align: 3px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("vertical-align", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<VerticalAlignProperty>(property);
            var concrete = (VerticalAlignProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px", concrete.Value);
        }

        [Fact]
        public void VerticalAlignPercentLegal()
        {
            var snippet = "vertical-align: 25%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("vertical-align", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<VerticalAlignProperty>(property);
            var concrete = (VerticalAlignProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25%", concrete.Value);
        }

        [Fact]
        public void VerticalAlignNegativeLengthLegal()
        {
            var snippet = "vertical-align: -3px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("vertical-align", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<VerticalAlignProperty>(property);
            var concrete = (VerticalAlignProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("-3px", concrete.Value);
        }

        [Fact]
        public void VerticalAlignInvalidKeywordIllegal()
        {
            var snippet = "vertical-align: wavy";
            var property = ParseDeclaration(snippet);
            Assert.Equal("vertical-align", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<VerticalAlignProperty>(property);
            var concrete = (VerticalAlignProperty)property;
            Assert.False(concrete.HasValue);
        }
    }
}
