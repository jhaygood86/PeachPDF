using PeachPDF.Text.Bidi;
using Xunit;

namespace PeachPDF.Tests.Text.Bidi
{
    /// <summary>
    /// Spot checks for <see cref="BidiClassTable"/> against known Unicode <c>Bidi_Class</c> values, covering
    /// one representative codepoint per class family so a generator or table-loading regression is caught
    /// independently of the full conformance suite (<see cref="BidiResolverConformanceTests"/>).
    /// </summary>
    public class BidiClassTableTests
    {
        [Theory]
        [InlineData(0x41, (int)BidiClass.L)] // LATIN CAPITAL LETTER A
        [InlineData(0x5D0, (int)BidiClass.R)] // HEBREW LETTER ALEF
        [InlineData(0x627, (int)BidiClass.AL)] // ARABIC LETTER ALEF
        [InlineData(0x30, (int)BidiClass.EN)] // DIGIT ZERO
        [InlineData(0x2B, (int)BidiClass.ES)] // PLUS SIGN
        [InlineData(0x24, (int)BidiClass.ET)] // DOLLAR SIGN
        [InlineData(0x660, (int)BidiClass.AN)] // ARABIC-INDIC DIGIT ZERO
        [InlineData(0x2C, (int)BidiClass.CS)] // COMMA
        [InlineData(0x300, (int)BidiClass.NSM)] // COMBINING GRAVE ACCENT
        [InlineData(0x0, (int)BidiClass.BN)] // NULL
        [InlineData(0xA, (int)BidiClass.B)] // LINE FEED
        [InlineData(0x9, (int)BidiClass.S)] // TAB
        [InlineData(0x20, (int)BidiClass.WS)] // SPACE
        [InlineData(0x28, (int)BidiClass.ON)] // LEFT PARENTHESIS
        [InlineData(0x202A, (int)BidiClass.LRE)]
        [InlineData(0x202B, (int)BidiClass.RLE)]
        [InlineData(0x202D, (int)BidiClass.LRO)]
        [InlineData(0x202E, (int)BidiClass.RLO)]
        [InlineData(0x202C, (int)BidiClass.PDF)]
        [InlineData(0x2066, (int)BidiClass.LRI)]
        [InlineData(0x2067, (int)BidiClass.RLI)]
        [InlineData(0x2068, (int)BidiClass.FSI)]
        [InlineData(0x2069, (int)BidiClass.PDI)]
        public void Of_KnownCodepoints_ReturnsExpectedClass(int codepoint, int expected)
        {
            Assert.Equal((BidiClass)expected, BidiClassTable.Of(codepoint));
        }

        [Fact]
        public void Of_UnassignedAstralCodepoint_DoesNotThrow()
        {
            // A private-use / far supplementary-plane codepoint - exercises the table's upper boundary.
            var result = BidiClassTable.Of(0x10FFFD);
            Assert.Equal(BidiClass.L, result); // PUA defaults to L per DerivedBidiClass.txt's own data
        }
    }
}
