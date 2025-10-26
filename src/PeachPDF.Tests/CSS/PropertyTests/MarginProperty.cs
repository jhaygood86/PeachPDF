namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class MarginPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void MarginLeftLengthLegal()
        {
            var snippet = "margin-left: 15px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-left", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginLeftProperty>(property);
            var concrete = (MarginLeftProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px", concrete.Value);
        }

        [Fact]
        public void MarginLeftInitialLegal()
        {
            var snippet = "margin-left: initial ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-left", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginLeftProperty>(property);
            var concrete = (MarginLeftProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("initial", concrete.Value);
        }

        [Fact]
        public void MarginRightLengthImportantLegal()
        {
            var snippet = "margin-right: 3em!important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-right", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<MarginRightProperty>(property);
            var concrete = (MarginRightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3em", concrete.Value);
        }

        [Fact]
        public void MarginRightPercentLegal()
        {
            var snippet = "margin-right: 10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-right", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginRightProperty>(property);
            var concrete = (MarginRightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10%", concrete.Value);
        }

        [Fact]
        public void MarginTopPercentLegal()
        {
            var snippet = "margin-top: 4% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-top", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginTopProperty>(property);
            var concrete = (MarginTopProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("4%", concrete.Value);
        }

        [Fact]
        public void MarginBottomZeroLegal()
        {
            var snippet = "margin-bottom: 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginBottomProperty>(property);
            var concrete = (MarginBottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void MarginBottomNegativeLegal()
        {
            var snippet = "margin-bottom: -3px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginBottomProperty>(property);
            var concrete = (MarginBottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("-3px", concrete.Value);
        }

        [Fact]
        public void MarginBottomAutoLegal()
        {
            var snippet = "margin-bottom: auto ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin-bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginBottomProperty>(property);
            var concrete = (MarginBottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void MarginAllZeroLegal()
        {
            var snippet = "margin: 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void MarginAllPercentLegal()
        {
            var snippet = "margin: 25% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25%", concrete.Value);
        }

        [Fact]
        public void MarginSidesLengthLegal()
        {
            var snippet = "margin: 10px 3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 3em", concrete.Value);
        }

        [Fact]
        public void MarginSidesLengthAndAutoLegal()
        {
            var snippet = "margin: 10px auto ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px auto", concrete.Value);
        }

        [Fact]
        public void MarginAutoLegal()
        {
            var snippet = "margin: auto ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void MarginThreeValuesLegal()
        {
            var snippet = "margin: 10px 3em 5px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 3em 5px", concrete.Value);
        }

        [Fact]
        public void MarginAllValuesWithPercentAndAutoLegal()
        {
            var snippet = "margin: 10px 5% auto 2% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 5% auto 2%", concrete.Value);
        }

        [Fact]
        public void MarginTooManyValuesIllegal()
        {
            var snippet = "margin: 10px 5% 8px 2% 3px auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("margin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<MarginProperty>(property);
            var concrete = (MarginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void MarginShouldBeRecombinedCorrectly()
        {
            var snippet = ".centered {margin-bottom: 1px; margin-top: 2px; margin-left: 3px; margin-right: 4px}";
            var expected = ".centered { margin: 2px 4px 1px 3px }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MarginShouldBeSimplifiedCorrectly()
        {
            var snippet = ".centered {margin:0;margin-left:auto;margin-right:auto;text-align:left;}";
            var expected = ".centered { margin: 0 auto; text-align: left }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MarginShouldBeReducedCompletely()
        {
            var snippet = ".centered {margin-bottom: 0px; margin-top: 0; margin-left: 0px; margin-right: 0}";
            var expected = ".centered { margin: 0 }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MarginReductionForPeriodicExpansion()
        {
            var snippet = "p { margin: 0 auto; }";
            var expected = "p { margin: 0 auto }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }
    }
}








