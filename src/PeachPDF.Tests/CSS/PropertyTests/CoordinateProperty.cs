namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    public class CssCoordinatePropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssHeightLegalPercentage()
        {
            var snippet = "height:   28% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("28%", concrete.Value);
            //Assert.IsType<Percent>(value);
        }

        [Fact]
        public void CssHeightLegalLengthInEm()
        {
            var snippet = "height:   0.3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("0.3em", concrete.Value);
            //Assert.IsType<Length>(value);
        }

        [Fact]
        public void CssHeightLegalLengthInPx()
        {
            var snippet = "height:   144px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("144px", concrete.Value);
            //Assert.IsType<Length>(value);
        }

        [Fact]
        public void CssHeightLegalAutoUppercase()
        {
            var snippet = "height: AUTO ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void CssWidthLegalLengthInCm()
        {
            var snippet = "width:0.5cm";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("0.5cm", concrete.Value);
            //Assert.IsType<Length>(value);
        }

        [Fact]
        public void CssWidthLegalLengthInMm()
        {
            var snippet = "width:1.5mm";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("1.5mm", concrete.Value);
            //Assert.IsType<Length>(value);
        }

        [Fact]
        public void CssWidthIllegalLength()
        {
            var snippet = "width:1.5 meter";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.NotNull(concrete);
        }

        [Fact]
        public void CssLeftLegalPixel()
        {
            var snippet = "left: 25px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("left", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<LeftProperty>(property);
            var concrete = (LeftProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssTopLegalEm()
        {
            var snippet = "top:  0.7em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("top", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TopProperty>(property);
            var concrete = (TopProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssRightLegalMm()
        {
            var snippet = "right:  1.5mm";
            var property = ParseDeclaration(snippet);
            Assert.Equal("right", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<RightProperty>(property);
            var concrete = (RightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssBottomFoundInStyleDeclaration()
        {
            var snippet = "bottom:  50%";
            var style = ParseDeclarations(snippet);
            Assert.Equal(1, style.Length);
            var bottom = style.Declarations.First();
            Assert.Equal("bottom", bottom.Name);
            Assert.Equal("50%", ((StyleDeclaration)style).Bottom);
        }

        [Fact]
        public void CssBottomLegalPercent()
        {
            var snippet = "bottom:  50%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BottomProperty>(property);
            var concrete = (BottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssHeightZeroLegal()
        {
            var snippet = "height:0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssWidthZeroLegal()
        {
            var snippet = "width  :  0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssWidthPercentLegal()
        {
            var snippet = "width  :  20.5%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssWidthPercentInLegal()
        {
            var snippet = "width  :  3in";
            var property = ParseDeclaration(snippet);
            Assert.Equal("width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.False(concrete.IsInherited);
        }

        [Fact]
        public void CssHeightAngleIllegal()
        {
            var snippet = "height  :  3deg";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.HasValue);
            Assert.False(concrete.IsInherited);
        }

        [Fact]
        public void CssHeightResolutionIllegal()
        {
            var snippet = "height  :  3dpi";
            var property = ParseDeclaration(snippet);
            Assert.Equal("height", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<HeightProperty>(property);
            var concrete = (HeightProperty)property;
            Assert.False(concrete.HasValue);
            Assert.False(concrete.IsInherited);
        }

        [Fact]
        public void CssTopLegalRem()
        {
            var snippet = "top:  1.2rem ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("top", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TopProperty>(property);
            var concrete = (TopProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssRightLegalCm()
        {
            var snippet = "right:  0.5cm";
            var property = ParseDeclaration(snippet);
            Assert.Equal("right", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<RightProperty>(property);
            var concrete = (RightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssBottomLegalPercentTwo()
        {
            var snippet = "bottom:  0.50%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BottomProperty>(property);
            var concrete = (BottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssBottomLegalZero()
        {
            var snippet = "bottom:  0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BottomProperty>(property);
            var concrete = (BottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssBottomIllegalNumber()
        {
            var snippet = "bottom:  20";
            var property = ParseDeclaration(snippet);
            Assert.Equal("bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BottomProperty>(property);
            var concrete = (BottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssMinHeightLegalZero()
        {
            var snippet = "min-height:  0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("min-height", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MinHeightProperty>(property);
            var concrete = (MinHeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
        }

        [Fact]
        public void CssMaxHeightIllegalAuto()
        {
            var snippet = "max-height:  auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("max-height", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MaxHeightProperty>(property);
            var concrete = (MaxHeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssMaxWidthLegalNone()
        {
            var snippet = "max-width:  none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("max-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MaxWidthProperty>(property);
            var concrete = (MaxWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssMaxWidthLegalLength()
        {
            var snippet = "max-width:  15px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("max-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MaxWidthProperty>(property);
            var concrete = (MaxWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px", concrete.Value);
        }

        [Fact]
        public void CssMinWidthLegalPercent()
        {
            var snippet = "min-width:  15%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("min-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MinWidthProperty>(property);
            var concrete = (MinWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15%", concrete.Value);
        }
    }
}








