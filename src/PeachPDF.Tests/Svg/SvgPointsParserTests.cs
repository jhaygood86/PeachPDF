using PeachPDF.Svg;

namespace PeachPDF.Tests.Svg
{
    public class SvgPointsParserTests
    {
        [Fact]
        public void Parse_CommaSeparatedPairs_ReturnsPoints()
        {
            var points = SvgPointsParser.Parse("0,0 10,0 10,10");

            Assert.Equal(3, points.Length);
            Assert.Equal(0, points[0].X); Assert.Equal(0, points[0].Y);
            Assert.Equal(10, points[1].X); Assert.Equal(0, points[1].Y);
            Assert.Equal(10, points[2].X); Assert.Equal(10, points[2].Y);
        }

        [Fact]
        public void Parse_WhitespaceSeparatedPairs_ReturnsPoints()
        {
            var points = SvgPointsParser.Parse("1 2 3 4");

            Assert.Equal(2, points.Length);
            Assert.Equal(1, points[0].X); Assert.Equal(2, points[0].Y);
            Assert.Equal(3, points[1].X); Assert.Equal(4, points[1].Y);
        }

        [Fact]
        public void Parse_TrailingUnpairedNumber_IsIgnored()
        {
            var points = SvgPointsParser.Parse("0,0 10,10 5");

            Assert.Equal(2, points.Length);
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(SvgPointsParser.Parse(null));
            Assert.Empty(SvgPointsParser.Parse(""));
        }
    }
}
