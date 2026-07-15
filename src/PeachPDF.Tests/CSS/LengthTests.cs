namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class LengthTests
    {
        [Fact]
        public void Constructor_SetsValueAndType()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal(10f, length.Value);
            Assert.Equal(Length.Unit.Px, length.Type);
        }

        [Theory]
        [InlineData((int)Length.Unit.Px, true)]
        [InlineData((int)Length.Unit.Pt, true)]
        [InlineData((int)Length.Unit.In, true)]
        [InlineData((int)Length.Unit.Cm, true)]
        [InlineData((int)Length.Unit.Mm, true)]
        [InlineData((int)Length.Unit.Pc, true)]
        [InlineData((int)Length.Unit.Em, false)]
        [InlineData((int)Length.Unit.Ex, false)]
        [InlineData((int)Length.Unit.Rem, false)]
        [InlineData((int)Length.Unit.Percent, false)]
        [InlineData((int)Length.Unit.Vw, false)]
        [InlineData((int)Length.Unit.Vh, false)]
        [InlineData((int)Length.Unit.Vmin, false)]
        [InlineData((int)Length.Unit.Vmax, false)]
        public void IsAbsolute_And_IsRelative_MatchUnitCategory(int unitValue, bool expectedAbsolute)
        {
            var length = new Length(1f, (Length.Unit)unitValue);

            Assert.Equal(expectedAbsolute, length.IsAbsolute);
            Assert.Equal(!expectedAbsolute, length.IsRelative);
        }

        [Theory]
        [InlineData((int)Length.Unit.Px, "px")]
        [InlineData((int)Length.Unit.Em, "em")]
        [InlineData((int)Length.Unit.Ex, "ex")]
        [InlineData((int)Length.Unit.Cm, "cm")]
        [InlineData((int)Length.Unit.Mm, "mm")]
        [InlineData((int)Length.Unit.In, "in")]
        [InlineData((int)Length.Unit.Pt, "pt")]
        [InlineData((int)Length.Unit.Pc, "pc")]
        [InlineData((int)Length.Unit.Ch, "ch")]
        [InlineData((int)Length.Unit.Rem, "rem")]
        [InlineData((int)Length.Unit.Vw, "vw")]
        [InlineData((int)Length.Unit.Vh, "vh")]
        [InlineData((int)Length.Unit.Vmin, "vmin")]
        [InlineData((int)Length.Unit.Vmax, "vmax")]
        [InlineData((int)Length.Unit.Percent, "%")]
        [InlineData((int)Length.Unit.None, "")]
        public void UnitString_MatchesUnitName(int unitValue, string expected)
        {
            var length = new Length(1f, (Length.Unit)unitValue);

            Assert.Equal(expected, length.UnitString);
        }

        [Theory]
        [InlineData("ch", (int)Length.Unit.Ch)]
        [InlineData("cm", (int)Length.Unit.Cm)]
        [InlineData("em", (int)Length.Unit.Em)]
        [InlineData("ex", (int)Length.Unit.Ex)]
        [InlineData("in", (int)Length.Unit.In)]
        [InlineData("mm", (int)Length.Unit.Mm)]
        [InlineData("pc", (int)Length.Unit.Pc)]
        [InlineData("pt", (int)Length.Unit.Pt)]
        [InlineData("px", (int)Length.Unit.Px)]
        [InlineData("rem", (int)Length.Unit.Rem)]
        [InlineData("vh", (int)Length.Unit.Vh)]
        [InlineData("vmax", (int)Length.Unit.Vmax)]
        [InlineData("vmin", (int)Length.Unit.Vmin)]
        [InlineData("vw", (int)Length.Unit.Vw)]
        [InlineData("%", (int)Length.Unit.Percent)]
        [InlineData("bogus", (int)Length.Unit.None)]
        public void GetUnit_ParsesKnownSuffixes(string suffix, int expectedValue)
        {
            Assert.Equal((Length.Unit)expectedValue, Length.GetUnit(suffix));
        }

        [Fact]
        public void TryParse_ValidLength_ReturnsTrue()
        {
            var success = Length.TryParse("10px", out var result);

            Assert.True(success);
            Assert.Equal(10f, result.Value);
            Assert.Equal(Length.Unit.Px, result.Type);
        }

        [Fact]
        public void TryParse_ZeroWithoutUnit_ReturnsZeroLength()
        {
            var success = Length.TryParse("0", out var result);

            Assert.True(success);
            Assert.Equal(Length.Zero, result);
        }

        [Fact]
        public void TryParse_NonZeroValueWithUnrecognizedUnit_ReturnsFalse()
        {
            // A non-numeric string like "abc" parses as value 0 with no recognized unit, which
            // TryParse treats as the valid unitless zero (matching CSS's "0" needing no unit) rather
            // than a parse failure. A non-zero magnitude with an unrecognized unit suffix is what
            // actually fails.
            var success = Length.TryParse("10bogus", out _);

            Assert.False(success);
        }

        [Fact]
        public void ToPixel_ConvertsAbsoluteUnit()
        {
            var length = new Length(1f, Length.Unit.In);

            // This engine's native unit is points (1in = 72pt), not the browser's 96dpi CSS px.
            Assert.Equal(72f, length.ToPixel());
        }

        [Fact]
        public void ToPixel_RelativeUnit_Throws()
        {
            var length = new Length(1f, Length.Unit.Em);

            Assert.Throws<InvalidOperationException>(() => length.ToPixel());
        }

        [Fact]
        public void ToPixels_Em_UsesEmFactor()
        {
            var length = new Length(2f, Length.Unit.Em);

            Assert.Equal(24d, length.ToPixels(12, 0, 0));
        }

        [Fact]
        public void ToPixels_Rem_UsesRemFactor()
        {
            var length = new Length(2f, Length.Unit.Rem);

            Assert.Equal(32d, length.ToPixels(0, 16, 0));
        }

        [Fact]
        public void ToPixels_Percent_UsesHundredPercentFactor()
        {
            var length = new Length(50f, Length.Unit.Percent);

            Assert.Equal(100d, length.ToPixels(0, 0, 200));
        }

        [Fact]
        public void ToPixels_Pc_TwelvePointsPerPica()
        {
            var length = new Length(1f, Length.Unit.Pc);

            Assert.Equal(12d, length.ToPixels(0, 0, 0));
        }

        [Fact]
        public void To_ConvertsBetweenAbsoluteUnits()
        {
            var length = new Length(1f, Length.Unit.In);

            // This engine's native unit is points (1in = 72pt), not the browser's 96dpi CSS px.
            Assert.Equal(72f, length.To(Length.Unit.Px));
        }

        [Fact]
        public void To_In_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(1f, length.To(Length.Unit.In));
        }

        [Fact]
        public void To_Mm_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(25.4f, length.To(Length.Unit.Mm), 3);
        }

        [Fact]
        public void To_Pc_ConvertsFromPoints()
        {
            var length = new Length(12f, Length.Unit.Pt);

            Assert.Equal(1f, length.To(Length.Unit.Pc));
        }

        [Fact]
        public void To_Pt_ReturnsSameValue()
        {
            var length = new Length(42f, Length.Unit.Pt);

            Assert.Equal(42f, length.To(Length.Unit.Pt));
        }

        [Fact]
        public void To_Cm_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(2.54f, length.To(Length.Unit.Cm), 3);
        }

        [Fact]
        public void To_RelativeTargetUnit_Throws()
        {
            var length = new Length(1f, Length.Unit.Px);

            Assert.Throws<InvalidOperationException>(() => length.To(Length.Unit.Em));
        }

        [Fact]
        public void Equality_ComparesValueAndType()
        {
            var a = new Length(10f, Length.Unit.Px);
            var b = new Length(10f, Length.Unit.Px);
            var c = new Length(10f, Length.Unit.Em);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a length"));
        }

        [Fact]
        public void GetHashCode_SameForEqualLengths()
        {
            var a = new Length(10f, Length.Unit.Px);
            var b = new Length(10f, Length.Unit.Px);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void CompareTo_SameUnit_ComparesValue()
        {
            var small = new Length(1f, Length.Unit.Px);
            var large = new Length(2f, Length.Unit.Px);

            Assert.True(small < large);
            Assert.True(large > small);
            Assert.True(small <= large);
            Assert.True(large >= small);
        }

        [Fact]
        public void CompareTo_DifferentAbsoluteUnits_ComparesInPixels()
        {
            var oneInch = new Length(1f, Length.Unit.In);
            var oneCm = new Length(1f, Length.Unit.Cm);

            Assert.True(oneInch > oneCm);
        }

        [Fact]
        public void ToString_Zero_OmitsUnit()
        {
            var length = new Length(0f, Length.Unit.Px);

            Assert.Equal("0", length.ToString());
        }

        [Fact]
        public void ToString_NonZero_IncludesUnit()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal("10px", length.ToString());
        }

        [Fact]
        public void ToString_WithFormatProvider()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal("10px", length.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void PredefinedConstants_HaveExpectedValues()
        {
            Assert.Equal(new Length(0f, Length.Unit.Px), Length.Zero);
            Assert.Equal(new Length(50f, Length.Unit.Percent), Length.Half);
            Assert.Equal(new Length(100f, Length.Unit.Percent), Length.Full);
            Assert.Equal(new Length(1f, Length.Unit.Px), Length.Thin);
            Assert.Equal(new Length(3f, Length.Unit.Px), Length.Medium);
            Assert.Equal(new Length(5f, Length.Unit.Px), Length.Thick);
        }
    }
}
