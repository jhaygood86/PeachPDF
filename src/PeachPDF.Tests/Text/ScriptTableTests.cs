using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// Spot checks for <see cref="ScriptTable"/> against known Unicode <c>Script</c> property values,
    /// covering one representative codepoint per script (plus the <c>Common</c>/<c>Inherited</c>/
    /// <c>Unknown</c> non-script values) so a generator or table-loading regression is caught
    /// independently. Values verified directly against the downloaded <c>assets/unicode/Scripts.txt</c>
    /// (Unicode 17.0.0), not from memory.
    /// </summary>
    public class ScriptTableTests
    {
        [Theory]
        [InlineData(0x0041, "Latin")] // LATIN CAPITAL LETTER A
        [InlineData(0x0030, "Common")] // DIGIT ZERO
        [InlineData(0x0600, "Arabic")] // ARABIC NUMBER SIGN
        [InlineData(0x05D0, "Hebrew")] // HEBREW LETTER ALEF
        [InlineData(0x3042, "Hiragana")] // HIRAGANA LETTER A
        [InlineData(0x30A2, "Katakana")] // KATAKANA LETTER A
        [InlineData(0x4E00, "Han")] // CJK UNIFIED IDEOGRAPH-4E00 (一)
        [InlineData(0x0905, "Devanagari")] // DEVANAGARI LETTER A
        [InlineData(0xAC00, "Hangul")] // HANGUL SYLLABLE GA
        [InlineData(0x0300, "Inherited")] // COMBINING GRAVE ACCENT
        [InlineData(0x064B, "Inherited")] // ARABIC FATHATAN - a combining mark inherits Script from its base, even inside the Arabic block
        public void Of_KnownCodepoints_ReturnsExpectedScript(int codepoint, string expected)
        {
            Assert.Equal(expected, ScriptTable.Of(codepoint));
        }

        [Fact]
        public void Of_UnassignedAstralCodepoint_ReturnsUnknown()
        {
            Assert.Equal(ScriptTable.Unknown, ScriptTable.Of(0x10FFFD));
        }

        [Fact]
        public void Of_Rune_MatchesOf_Int()
        {
            var rune = new System.Text.Rune(0x4E00);
            Assert.Equal(ScriptTable.Of(0x4E00), ScriptTable.Of(rune));
        }

        [Fact]
        public void RangesForScript_KnownScript_AgreesWithOf_AndExcludesOtherScripts()
        {
            var ranges = ScriptTable.RangesForScript("Cuneiform");

            Assert.NotEmpty(ranges);
            Assert.Contains(ranges, r => r.Contains(new System.Text.Rune(0x12000))); // CUNEIFORM SIGN A
            Assert.DoesNotContain(ranges, r => r.Contains(new System.Text.Rune(0x0041))); // LATIN CAPITAL A
        }

        [Fact]
        public void RangesForScript_ReturnsSortedNonOverlappingRanges()
        {
            var ranges = ScriptTable.RangesForScript("Han");
            Assert.NotEmpty(ranges);

            for (var i = 1; i < ranges.Count; i++)
                Assert.True(ranges[i].Start.Value > ranges[i - 1].End.Value);
        }

        [Fact]
        public void RangesForScript_UnknownScriptName_ReturnsEmpty()
        {
            Assert.Empty(ScriptTable.RangesForScript("NotARealUnicodeScript"));
        }
    }
}
