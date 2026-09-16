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

        [Fact]
        public void BoxSizingTextIllegal()
        {
            // "text" is background-clip's own keyword (issue #1117), deliberately not added to the
            // shared BoxModel/Map.BoxModels box-sizing (and background-origin) also use.
            var snippet = "box-sizing: text";
            var property = ParseDeclaration(snippet);
            Assert.Equal("box-sizing", property.Name);
            Assert.IsType<BoxSizingProperty>(property);
            var concrete = (BoxSizingProperty)property;
            Assert.False(concrete.HasValue);
        }
    }
}








