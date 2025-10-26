namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssPaddingPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssPaddingLeftLengthLegal()
        {
            var snippet = "padding-left: 15px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding-left", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingLeftProperty>(property);
            var concrete = (PaddingLeftProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px", concrete.Value);
        }

        [Fact]
        public void CssPaddingRightLengthImportantLegal()
        {
            var snippet = "padding-right: 3em!important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding-right", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<PaddingRightProperty>(property);
            var concrete = (PaddingRightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3em", concrete.Value);
        }

        [Fact]
        public void CssPaddingTopPercentLegal()
        {
            var snippet = "padding-top: 4% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding-top", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingTopProperty>(property);
            var concrete = (PaddingTopProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("4%", concrete.Value);
        }

        [Fact]
        public void CssPaddingBottomZeroLegal()
        {
            var snippet = "padding-bottom: 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding-bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingBottomProperty>(property);
            var concrete = (PaddingBottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssPaddingAllZeroLegal()
        {
            var snippet = "padding: 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssPaddingAllPercentLegal()
        {
            var snippet = "padding: 25% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25%", concrete.Value);
        }

        [Fact]
        public void CssPaddingSidesLengthLegal()
        {
            var snippet = "padding: 10px 3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 3em", concrete.Value);
        }

        [Fact]
        public void CssPaddingAutoIllegal()
        {
            var snippet = "padding: auto ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssPaddingThreeValuesLegal()
        {
            var snippet = "padding: 10px 3em 5px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 3em 5px", concrete.Value);
        }

        [Fact]
        public void CssPaddingAllValuesWithPercentLegal()
        {
            var snippet = "padding: 10px 5% 8px 2% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 5% 8px 2%", concrete.Value);
        }

        [Fact]
        public void CssPaddingTooManyValuesIllegal()
        {
            var snippet = "padding: 10px 5% 8px 2% 3px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("padding", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PaddingProperty>(property);
            var concrete = (PaddingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssPaddingShouldBeRecombinedCorrectly()
        {
            var snippet = ".centered {padding-bottom: 2em; padding-top: 2.5em; padding-left: 0; padding-right: 0}";
            var expected = ".centered { padding: 2.5em 0 2em }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }
    }
}








