using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class BoxSizingPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void BoxSizingContentBoxLegal()
        {
            var snippet = "box-sizing: content-box";
            var property = ParseDeclaration(snippet);
            Assert.Equal("box-sizing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BoxSizingProperty>(property);
            var concrete = (BoxSizingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("content-box", concrete.Value);
        }

        [Fact]
        public void BoxSizingBorderBoxLegal()
        {
            var snippet = "box-sizing: border-box";
            var property = ParseDeclaration(snippet);
            Assert.Equal("box-sizing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BoxSizingProperty>(property);
            var concrete = (BoxSizingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("border-box", concrete.Value);
        }
    }
}








