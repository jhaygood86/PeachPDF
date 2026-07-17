namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class WhiteSpacePropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("normal")]
        [InlineData("pre")]
        [InlineData("nowrap")]
        [InlineData("pre-wrap")]
        [InlineData("pre-line")]
        public void WhiteSpaceKeywordLegal(string keyword)
        {
            var snippet = $"white-space: {keyword}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("white-space", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WhiteSpaceProperty>(property);
            var concrete = (WhiteSpaceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(keyword, concrete.Value);
        }

        [Fact]
        public void WhiteSpaceInvalidKeywordIllegal()
        {
            var snippet = "white-space: wavy";
            var property = ParseDeclaration(snippet);
            Assert.Equal("white-space", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WhiteSpaceProperty>(property);
            var concrete = (WhiteSpaceProperty)property;
            // white-space carries PropertyFlags.Inherited, so a failed parse (DeclaredValue stays
            // null) reports IsInherited=true here - same pattern as BorderSpacingProperty's illegal
            // case in BorderProperty.cs, whose property is also flagged Inherited.
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }
    }
}
