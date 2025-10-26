namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class BorderImagePropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void BorderImageSourceNoneLegal()
        {
            var snippet = "border-image-source: none    ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-source", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSourceProperty>(property);
            var concrete = (BorderImageSourceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void BorderImageSourceUrlLegal()
        {
            var snippet = "border-image-source: url(image.jpg)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-source", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSourceProperty>(property);
            var concrete = (BorderImageSourceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.jpg\")", concrete.Value);
        }

        [Fact]
        public void BorderImageSourceLinearGradientLegal()
        {
            var snippet = "border-image-source: linear-gradient(to top, red, yellow)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-source", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSourceProperty>(property);
            var concrete = (BorderImageSourceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("linear-gradient(to top, rgb(255, 0, 0), rgb(255, 255, 0))", concrete.Value);
        }

        [Fact]
        public void BorderImageOutsetZeroLegal()
        {
            var snippet = "border-image-outset: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-outset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageOutsetProperty>(property);
            var concrete = (BorderImageOutsetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderImageOutsetLengthPercentLegal()
        {
            var snippet = "border-image-outset: 10px   25%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-outset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageOutsetProperty>(property);
            var concrete = (BorderImageOutsetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25%", concrete.Value);
        }

        [Fact]
        public void BorderImageOutsetLengthPercentZeroLegal()
        {
            var snippet = "border-image-outset: 10px   25% 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-outset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageOutsetProperty>(property);
            var concrete = (BorderImageOutsetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25% 0", concrete.Value);
        }

        [Fact]
        public void BorderImageOutsetLengthPercentZeroPercentLegal()
        {
            var snippet = "border-image-outset: 10px   25% 0 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-outset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageOutsetProperty>(property);
            var concrete = (BorderImageOutsetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25% 0 10%", concrete.Value);
        }

        [Fact]
        public void BorderImageOutsetZerosIllegal()
        {
            var snippet = "border-image-outset: 0 0 0 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-outset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageOutsetProperty>(property);
            var concrete = (BorderImageOutsetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderImageWidthZeroLegal()
        {
            var snippet = "border-image-width: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthAutoLegal()
        {
            var snippet = "border-image-width: auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthMultipleLegal()
        {
            var snippet = "border-image-width: 5";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("5", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthLengthPercentLegal()
        {
            var snippet = "border-image-width: 10px   25%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25%", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthLengthPercentZeroLegal()
        {
            var snippet = "border-image-width: 10px   25% 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25% 0", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthLengthPercentAutoPercentLegal()
        {
            var snippet = "border-image-width: 10px   25% auto 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 25% auto 10%", concrete.Value);
        }

        [Fact]
        public void BorderImageWidthZerosIllegal()
        {
            var snippet = "border-image-width: 0 0 0 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageWidthProperty>(property);
            var concrete = (BorderImageWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderImageRepeatStretchUppercaseLegal()
        {
            var snippet = "border-image-repeat:   StRETCH";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageRepeatProperty>(property);
            var concrete = (BorderImageRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("stretch", concrete.Value);
        }

        [Fact]
        public void BorderImageRepeatRepeatLegal()
        {
            var snippet = "border-image-repeat:   repeat";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageRepeatProperty>(property);
            var concrete = (BorderImageRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat", concrete.Value);
        }

        [Fact]
        public void BorderImageRepeatRoundLegal()
        {
            var snippet = "border-image-repeat:   round";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageRepeatProperty>(property);
            var concrete = (BorderImageRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("round", concrete.Value);
        }

        [Fact]
        public void BorderImageRepeatStretchRoundLegal()
        {
            var snippet = "border-image-repeat: stretch round";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageRepeatProperty>(property);
            var concrete = (BorderImageRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("stretch round", concrete.Value);
        }

        [Fact]
        public void BorderImageRepeatNoRepeatIllegal()
        {
            var snippet = "border-image-repeat: no-repeat";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageRepeatProperty>(property);
            var concrete = (BorderImageRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderImageSlicePixelsLegal()
        {
            var snippet = "border-image-slice: 3";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3", concrete.Value);
        }

        [Fact]
        public void BorderImageSlicePercentLegal()
        {
            var snippet = "border-image-slice: 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10%", concrete.Value);
        }

        [Fact]
        public void BorderImageSliceFillLegal()
        {
            var snippet = "border-image-slice: fill";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(true, concrete.IsFilled);
            //Assert.Equal(Length.Full, concrete.SliceLeft);
            //Assert.Equal(Length.Full, concrete.SliceRight);
            //Assert.Equal(Length.Full, concrete.SliceTop);
            //Assert.Equal(Length.Full, concrete.SliceBottom);
        }

        [Fact]
        public void BorderImageSlicePercentFillLegal()
        {
            var snippet = "border-image-slice: 10% fill";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10% fill", concrete.Value);
        }

        [Fact]
        public void BorderImageSlicePercentPixelsFillLegal()
        {
            var snippet = "border-image-slice: 10% 30 fill";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10% 30 fill", concrete.Value);
        }

        [Fact]
        public void BorderImageSlicePercentPixelsFillZerosLegal()
        {
            var snippet = "border-image-slice: 10% 30 fill 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10% 30 0 0 fill", concrete.Value);
        }

        [Fact]
        public void BorderImageSlicePercentPixelsFillZerosIllegal()
        {
            var snippet = "border-image-slice: 10% 30 fill 0 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderImageSlicePercentPixelsZerosFillIllegal()
        {
            var snippet = "border-image-slice: 10% 30  0 0 0 fill";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image-slice", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageSliceProperty>(property);
            var concrete = (BorderImageSliceProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderImageNoneLegal()
        {
            var snippet = "border-image: none    ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlOffsetLegal()
        {
            var snippet = "border-image: url(image.png) 50 50";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") 50 50", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlOffsetRepeatLegal()
        {
            var snippet = "border-image: url(image.png) 30 30 repeat";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") 30 30 repeat", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlStretchUppercaseLegal()
        {
            var snippet = "border-image: url(image.png) STRETCH";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") stretch", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlOffsetWidthTwoLegal()
        {
            var snippet = "border-image: url(image.png) 30 30 / 15px 15px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") 30 30 / 15px", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlOffsetWidthFourLegal()
        {
            var snippet = "border-image: url(image.png) 30 30 0 10 / 15px 0 15px 2em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") 30 30 0 10 / 15px 0 15px 2em", concrete.Value);
        }

        [Fact]
        public void BorderImageUrlOffsetWidthOutsetLegal()
        {
            var snippet = "border-image: url(image.png) 30 30 / 15px 15px / 5% 2% 0 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderImageProperty>(property);
            var concrete = (BorderImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\") 30 30 / 15px / 5% 2% 0 10%", concrete.Value);
        }
    }
}








