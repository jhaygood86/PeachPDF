using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// Spot checks for <see cref="VerticalOrientationTable"/> against known Unicode
    /// <c>Vertical_Orientation</c> values, covering one representative codepoint per class so a generator
    /// or table-loading regression is caught independently.
    /// </summary>
    public class VerticalOrientationTableTests
    {
        [Theory]
        [InlineData(0x41, (int)VerticalOrientationClass.R)] // LATIN CAPITAL LETTER A - the @missing default
        [InlineData(0x30, (int)VerticalOrientationClass.R)] // DIGIT ZERO
        [InlineData(0x3042, (int)VerticalOrientationClass.U)] // HIRAGANA LETTER A
        [InlineData(0x4E00, (int)VerticalOrientationClass.U)] // CJK UNIFIED IDEOGRAPH-4E00 (一)
        [InlineData(0xAC00, (int)VerticalOrientationClass.U)] // HANGUL SYLLABLE GA
        [InlineData(0x2018, (int)VerticalOrientationClass.Tr)] // LEFT SINGLE QUOTATION MARK
        [InlineData(0x3001, (int)VerticalOrientationClass.Tu)] // IDEOGRAPHIC COMMA
        public void Of_KnownCodepoints_ReturnsExpectedClass(int codepoint, int expected)
        {
            Assert.Equal((VerticalOrientationClass)expected, VerticalOrientationTable.Of(codepoint));
        }

        [Fact]
        public void Of_UnassignedAstralCodepoint_DoesNotThrow()
        {
            // A private-use supplementary-plane codepoint - exercises the table's upper boundary.
            var result = VerticalOrientationTable.Of(0x10FFFD);
            Assert.Equal(VerticalOrientationClass.U, result); // PUA defaults to U per VerticalOrientation.txt's own data
        }

        [Fact]
        public void Of_Rune_MatchesOf_Int()
        {
            var rune = new System.Text.Rune(0x4E00);
            Assert.Equal(VerticalOrientationTable.Of(0x4E00), VerticalOrientationTable.Of(rune));
        }
    }
}
