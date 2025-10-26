namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssOutlinePropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssOutlineStyleDottedLegal()
        {
            var snippet = "outline-style   :  dotTED";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineStyleProperty>(property);
            var concrete = (OutlineStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("dotted", concrete.Value);
        }

        [Fact]
        public void CssOutlineStyleSolidLegal()
        {
            var snippet = "outline-style   :  solid";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineStyleProperty>(property);
            var concrete = (OutlineStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("solid", concrete.Value);
        }

        [Fact]
        public void CssOutlineStyleNoIllegal()
        {
            var snippet = "outline-style   :  no";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineStyleProperty>(property);
            var concrete = (OutlineStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssOutlineColorInvertLegal()
        {
            var snippet = "outline-color :  invert ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineColorProperty>(property);
            var concrete = (OutlineColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("invert", concrete.Value);
        }

        [Fact]
        public void CssOutlineColorHslLegal()
        {
            var snippet = "outline-color :  hsl(320, 80%, 50%) ";//equivalent to rgba(229, 26, 161, 1)
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineColorProperty>(property);
            var concrete = (OutlineColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("hsl(320deg, 80%, 50%)", concrete.Value);
        }

        [Fact]
        public void CssOutlineColorHexLegal()
        {
            var snippet = "outline-color :  #0000FF ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineColorProperty>(property);
            var concrete = (OutlineColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(0, 0, 255)", concrete.Value);
        }

        [Fact]
        public void CssOutlineColorRedLegal()
        {
            var snippet = "outline-color :  red ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineColorProperty>(property);
            var concrete = (OutlineColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void CssOutlineColorIllegal()
        {
            var snippet = "outline-color :  blau ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineColorProperty>(property);
            var concrete = (OutlineColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssOutlineWidthThinImportantLegal()
        {
            var snippet = "outline-width :  thin !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-width", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<OutlineWidthProperty>(property);
            var concrete = (OutlineWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px", concrete.Value);
        }

        [Fact]
        public void CssOutlineWidthNumberIllegal()
        {
            var snippet = "outline-width :  3";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineWidthProperty>(property);
            var concrete = (OutlineWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssOutlineWidthLengthLegal()
        {
            var snippet = "outline-width :  0.1em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineWidthProperty>(property);
            var concrete = (OutlineWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.1em", concrete.Value);
            //Assert.IsType<Length>(concrete.Value);
        }

        [Fact]
        public void CssOutlineSingleLegal()
        {
            var snippet = "outline :  thin";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px", concrete.Value);
        }

        [Fact]
        public void CssOutlineDualLegal()
        {
            var snippet = "outline :  thin   invert";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px invert", concrete.Value);
        }

        [Fact]
        public void CssOutlineAllDottedLegal()
        {
            var snippet = "outline :  dotted 0.3em rgb(255, 255, 255)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3em dotted rgb(255, 255, 255)", concrete.Value);
        }

        [Fact]
        public void CssOutlineDoubleColorIllegal()
        {
            var snippet = "outline :  dotted #123456 rgb(255, 255, 255)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssOutlineAllSolidLegal()
        {
            var snippet = "outline :  1px solid #000";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px solid rgb(0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void CssOutlineAllColorNamedLegal()
        {
            var snippet = "outline :  solid black 1px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("outline", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<OutlineProperty>(property);
            var concrete = (OutlineProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px solid rgb(0, 0, 0)", concrete.Value);
        }
    }
}








