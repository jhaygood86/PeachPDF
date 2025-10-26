namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class BorderRadiusPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void BorderBottomLeftRadiusPxPxLegal()
        {
            var snippet = "border-bottom-left-radius: 40px  40px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-left-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomLeftRadiusProperty>(property);
            var concrete = (BorderBottomLeftRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("40px 40px", concrete.Value);
        }

        [Fact]
        public void BorderBottomLeftRadiusPxEmLegal()
        {
            var snippet = "border-bottom-left-radius  : 40px 20em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-left-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomLeftRadiusProperty>(property);
            var concrete = (BorderBottomLeftRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("40px 20em", concrete.Value);
        }

        [Fact]
        public void BorderBottomLeftRadiusPxPercentLegal()
        {
            var snippet = "border-bottom-left-radius: 10px 5%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-left-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomLeftRadiusProperty>(property);
            var concrete = (BorderBottomLeftRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 5%", concrete.Value);
        }

        [Fact]
        public void BorderBottomLeftRadiusPercentLegal()
        {
            var snippet = "border-bottom-left-radius: 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-left-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomLeftRadiusProperty>(property);
            var concrete = (BorderBottomLeftRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10%", concrete.Value);
        }

        [Fact]
        public void BorderBottomRightRadiusZeroLegal()
        {
            var snippet = "border-bottom-right-radius: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-right-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomRightRadiusProperty>(property);
            var concrete = (BorderBottomRightRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderBottomRightRadiusPxLegal()
        {
            var snippet = "border-bottom-right-radius: 20px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-right-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomRightRadiusProperty>(property);
            var concrete = (BorderBottomRightRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("20px", concrete.Value);
        }

        [Fact]
        public void BorderTopLeftRadiusCmLegal()
        {
            var snippet = "border-top-left-radius: 3.5cm";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top-left-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopLeftRadiusProperty>(property);
            var concrete = (BorderTopLeftRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3.5cm", concrete.Value);
        }

        [Fact]
        public void BorderTopRightRadiusPercentPercentLegal()
        {
            var snippet = "border-top-right-radius: 15% 3.5%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top-right-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopRightRadiusProperty>(property);
            var concrete = (BorderTopRightRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15% 3.5%", concrete.Value);
        }

        [Fact]
        public void BorderRadiusPercentPercentLegal()
        {
            var snippet = "border-radius: 15% 3.5%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15% 3.5%", concrete.Value);
        }

        [Fact]
        public void BorderRadiusZeroLegal()
        {
            var snippet = "border-radius: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(Length.Zero, concrete.HorizontalBottomLeft);
            //Assert.Equal(Length.Zero, concrete.HorizontalTopLeft);
            //Assert.Equal(Length.Zero, concrete.HorizontalBottomRight);
            //Assert.Equal(Length.Zero, concrete.HorizontalTopRight);
            //Assert.Equal(Length.Zero, concrete.VerticalBottomLeft);
            //Assert.Equal(Length.Zero, concrete.VerticalBottomRight);
            //Assert.Equal(Length.Zero, concrete.VerticalTopLeft);
            //Assert.Equal(Length.Zero, concrete.VerticalTopRight);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderRadiusThreeLengthsLegal()
        {
            var snippet = "border-radius: 2px 4px 3px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.HorizontalTopLeft);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.VerticalTopLeft);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.HorizontalTopRight);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.VerticalTopRight);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.HorizontalBottomRight);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.VerticalBottomRight);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.HorizontalBottomLeft);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.VerticalBottomLeft);
            Assert.Equal("2px 4px 3px", concrete.Value);
        }

        [Fact]
        public void BorderRadiusFourLengthsLegal()
        {
            var snippet = "border-radius: 2px 4px 3px 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.HorizontalTopLeft);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.VerticalTopLeft);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.HorizontalTopRight);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.VerticalTopRight);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.HorizontalBottomRight);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.VerticalBottomRight);
            //Assert.Equal(new Length(0f, Length.Unit.Px), concrete.HorizontalBottomLeft);
            //Assert.Equal(new Length(0f, Length.Unit.Px), concrete.VerticalBottomLeft);
            Assert.Equal("2px 4px 3px 0", concrete.Value);
        }

        [Fact]
        public void BorderRadiusFiveLengthsIllegal()
        {
            var snippet = "border-radius: 2px 4px 3px 0 1px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderRadiusLengthFractionLegal()
        {
            var snippet = "border-radius: 1em/5em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(new Length(1f, Length.Unit.Em), concrete.HorizontalTopLeft);
            //Assert.Equal(new Length(5f, Length.Unit.Em), concrete.VerticalTopLeft);
            //Assert.Equal(new Length(1f, Length.Unit.Em), concrete.HorizontalTopRight);
            //Assert.Equal(new Length(5f, Length.Unit.Em), concrete.VerticalTopRight);
            //Assert.Equal(new Length(1f, Length.Unit.Em), concrete.HorizontalBottomRight);
            //Assert.Equal(new Length(5f, Length.Unit.Em), concrete.VerticalBottomRight);
            //Assert.Equal(new Length(1f, Length.Unit.Em), concrete.HorizontalBottomLeft);
            //Assert.Equal(new Length(5f, Length.Unit.Em), concrete.VerticalBottomLeft);
            Assert.Equal("1em / 5em", concrete.Value);
        }

        [Fact]
        public void BorderRadiusLengthFractionInbalancedLegal()
        {
            var snippet = "border-radius: 4px 3px 6px / 2px 4px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.HorizontalTopLeft);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.VerticalTopLeft);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.HorizontalTopRight);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.VerticalTopRight);
            //Assert.Equal(new Length(6f, Length.Unit.Px), concrete.HorizontalBottomRight);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.VerticalBottomRight);
            //Assert.Equal(new Length(3f, Length.Unit.Px), concrete.HorizontalBottomLeft);
            //Assert.Equal(new Length(4f, Length.Unit.Px), concrete.VerticalBottomLeft);
            Assert.Equal("4px 3px 6px / 2px 4px", concrete.Value);
        }

        [Fact]
        public void BorderRadiusFullFractionLegal()
        {
            var snippet = "border-radius: 4px 3px 6px 1em / 2px 4px 0 20%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("4px 3px 6px 1em / 2px 4px 0 20%", concrete.Value);
        }

        [Fact]
        public void BorderRadiusFiveTailFractionIllegal()
        {
            var snippet = "border-radius: 4px 3px 6px 1em / 2px 4px 0 20% 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderRadiusFiveHeadFractionIllegal()
        {
            var snippet = "border-radius: 4px 3px 6px 1em 0 / 2px 4px 0 20%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-radius", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRadiusProperty>(property);
            var concrete = (BorderRadiusProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderRadiusCircleShouldBeExpandedAndRecombinedCorrectly()
        {
            var snippet = ".centered { border-radius: 5px; }";
            var expected = ".centered { border-radius: 5px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BorderRadiusEllipseShouldBeExpandedAndRecombinedCorrectly()
        {
            var snippet = ".centered { border-radius: 5px/3px; }";
            var expected = ".centered { border-radius: 5px / 3px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BorderRadiusSimplificationShouldWork()
        {
            var snippet = ".centered { border-top-left-radius: 0 1px; border-bottom-left-radius: 1px 2px; border-top-right-radius: 0 3px; border-bottom-right-radius: 1px 4px; }";
            var expected = ".centered { border-radius: 0 0 1px 1px / 1px 3px 4px 2px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BorderRadiusRecombinationAndReductionCheck()
        {
            var snippet = ".centered { border-top-left-radius: 0 1px; border-bottom-left-radius: 0 1px; border-top-right-radius: 1px 1px; border-bottom-right-radius: 0 1px; }";
            var expected = ".centered { border-radius: 0 1px 0 0 / 1px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BorderRadiusPureCircleRecombination()
        {
            var snippet = ".test { border-top-left-radius:15px;border-bottom-left-radius:15px;border-bottom-right-radius:0;border-top-right-radius:0;}";
            var expected = ".test { border-radius: 15px 0 0 15px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }
    }
}








