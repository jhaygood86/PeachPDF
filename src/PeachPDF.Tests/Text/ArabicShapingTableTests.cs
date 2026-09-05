using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// Spot checks for <see cref="ArabicShapingTable"/> against known Unicode <c>Joining_Type</c>
    /// values, covering one representative codepoint per type so a generator or table-loading
    /// regression is caught independently. Values verified directly against the downloaded
    /// <c>assets/unicode/DerivedJoiningType.txt</c> (Unicode 17.0.0), not from memory.
    /// </summary>
    public class ArabicShapingTableTests
    {
        [Theory]
        [InlineData(0x0041, (int)ArabicJoiningType.U)] // LATIN CAPITAL LETTER A - the @missing default
        [InlineData(0x0640, (int)ArabicJoiningType.C)] // ARABIC TATWEEL
        [InlineData(0x0622, (int)ArabicJoiningType.R)] // ARABIC LETTER ALEF WITH MADDA ABOVE
        [InlineData(0x0628, (int)ArabicJoiningType.D)] // ARABIC LETTER BEH
        [InlineData(0x064B, (int)ArabicJoiningType.T)] // ARABIC FATHATAN (combining mark - transparent to joining)
        [InlineData(0x200D, (int)ArabicJoiningType.C)] // ZERO WIDTH JOINER
        [InlineData(0x0710, (int)ArabicJoiningType.R)] // SYRIAC LETTER ALAPH
        public void Of_KnownCodepoints_ReturnsExpectedJoiningType(int codepoint, int expected)
        {
            Assert.Equal((ArabicJoiningType)expected, ArabicShapingTable.Of(codepoint));
        }

        [Fact]
        public void Of_UnassignedAstralCodepoint_ReturnsNonJoining()
        {
            Assert.Equal(ArabicJoiningType.U, ArabicShapingTable.Of(0x10FFFD));
        }

        [Fact]
        public void Of_Rune_MatchesOf_Int()
        {
            var rune = new System.Text.Rune(0x0628);
            Assert.Equal(ArabicShapingTable.Of(0x0628), ArabicShapingTable.Of(rune));
        }
    }
}
