namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class QUnitTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("margin-left: 10q")]
        [InlineData("margin-left: 10Q")]
        [InlineData("width: calc(1q + 1px)")]
        [InlineData("transform: scale(50%)")]
        [InlineData("transform: scale(50%, 25%)")]
        [InlineData("transform: scaleX(50%)")]
        [InlineData("transform: scale3d(50%, 1, 200%)")]
        public void IsLegal(string declaration)
        {
            Assert.True(ParseDeclaration(declaration).HasValue);
        }

        [Fact]
        public void AQuarterMillimetre_IsAFortiethOfACentimetre()
        {
            Assert.True(Length.TryParse("40q", out var q));
            Assert.True(Length.TryParse("1cm", out var cm));
            Assert.True(q.IsAbsolute);
            Assert.Equal("q", q.UnitString);
            Assert.Equal(cm.ToPixel(), q.ToPixel(), 4);
            Assert.Equal(1f, q.To(Length.Unit.Cm), 4);
        }
    }
}
