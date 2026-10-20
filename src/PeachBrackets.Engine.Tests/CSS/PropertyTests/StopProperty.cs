namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachBrackets.Css;
    using Xunit;

    public class StopPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("stop-color: red", "rgb(255, 0, 0)")]
        [InlineData("stop-color: #0F0", "rgb(0, 255, 0)")]
        [InlineData("stop-color: currentcolor", "currentColor")]
        [InlineData("stop-color: transparent", "rgba(0, 0, 0, 0)")]
        public void StopColorLegal(string snippet, string expected)
        {
            var property = ParseDeclaration(snippet);
            Assert.Equal("stop-color", property.Name);
            Assert.IsType<StopColorProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal(expected, property.Value);
        }

        [Theory]
        [InlineData("stop-color: none")]
        [InlineData("stop-color: 5px")]
        [InlineData("stop-color: red blue")]
        public void StopColorIllegal(string snippet)
        {
            var property = ParseDeclaration(snippet);
            Assert.Equal("stop-color", property.Name);
            Assert.IsType<StopColorProperty>(property);
            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("stop-opacity: 0.5", "0.5")]
        [InlineData("stop-opacity: 1", "1")]
        [InlineData("stop-opacity: 0", "0")]
        public void StopOpacityLegal(string snippet, string expected)
        {
            var property = ParseDeclaration(snippet);
            Assert.Equal("stop-opacity", property.Name);
            Assert.IsType<StopOpacityProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal(expected, property.Value);
        }

        [Fact]
        public void StopOpacityRedIllegal()
        {
            var property = ParseDeclaration("stop-opacity: red");
            Assert.IsType<StopOpacityProperty>(property);
            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("stroke-width: 6")]
        [InlineData("stroke-width: 0")]
        [InlineData("stroke-width: 2.5")]
        [InlineData("stroke-dashoffset: 7")]
        [InlineData("stroke-dashoffset: -3")]
        [InlineData("stroke-dashoffset: 1.5")]
        public void AUnitlessNumberIsALegalStrokeWidthOrDashoffset(string snippet)
        {
            var property = ParseDeclaration(snippet);
            Assert.True(property.HasValue);
        }

        [Theory]
        [InlineData("stroke-width: -2")]
        [InlineData("stroke-width: -2px")]
        [InlineData("stroke-width: -5%")]
        public void ANegativeStrokeWidthIsIllegal(string snippet)
        {
            var property = ParseDeclaration(snippet);
            Assert.IsType<StrokeWidthProperty>(property);
            Assert.False(property.HasValue);
        }
    }
}
