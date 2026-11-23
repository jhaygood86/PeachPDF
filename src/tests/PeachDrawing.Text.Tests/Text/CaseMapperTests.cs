using PeachDrawing.Text.Unicode;
using Xunit;

namespace PeachDrawing.Text.Tests.Text
{
    /// <summary>Unicode's default case conversion: the full mappings, the final sigma and the Turkic tailoring.</summary>
    public class CaseMapperTests
    {
        [Theory]
        [InlineData("abc", "ABC")]
        [InlineData("straße", "STRASSE")]
        [InlineData("ﬁ", "FI")]                         // the fi ligature
        [InlineData("ŉ", "ʼN")]                    // n preceded by an apostrophe
        [InlineData("ᾀ", "ἈΙ")]               // Greek with a prosgegrammeni
        [InlineData("ı", "I")]                               // dotless i
        [InlineData("\U00010428", "\U00010400")]             // Deseret, outside the BMP
        [InlineData("i", "I")]
        public void ToUpper_UsesTheFullMappings(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToUpper(text));

        [Theory]
        [InlineData("ABC", "abc")]
        [InlineData("İ", "i̇")]                    // capital I with a dot above becomes i and a combining dot
        [InlineData("\U00010400", "\U00010428")]
        public void ToLower_UsesTheFullMappings(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToLower(text));

        [Theory]
        [InlineData("ΑΣ", "ας")]                             // final
        [InlineData("ΑΣΑ", "ασα")]                           // medial
        [InlineData("Σ", "σ")]                               // alone
        [InlineData("ΑΣ.", "ας.")]                           // case-ignorable punctuation after it
        [InlineData("ΑΣ ΑΣ", "ας ας")]
        [InlineData("1Σ", "1σ")]                             // no cased letter before
        public void ToLower_PicksTheFinalSigmaFromItsContext(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToLower(text));

        [Theory]
        [InlineData("tr", "i", "İ")]
        [InlineData("az", "i", "İ")]
        [InlineData("tr-TR", "i", "İ")]
        [InlineData("en", "i", "I")]
        [InlineData("tr-Latn", "i", "İ")]
        [InlineData("tr-Cyrl", "i", "I")]                 // a script other than Latin overrides the language
        [InlineData(null, "i", "I")]
        public void ToUpper_TailorsTurkicLanguages(string? language, string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToUpper(text, language));

        [Theory]
        [InlineData("tr", "I", "ı")]
        [InlineData("tr", "İ", "i")]
        [InlineData("en", "I", "i")]
        public void ToLower_TailorsTurkicLanguages(string language, string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToLower(text, language));

        [Theory]
        [InlineData("ß", "Ss")]
        [InlineData("ǆ", "ǅ")]                               // the digraph's title case is neither its upper nor its lower
        [InlineData("ǅ", "ǅ")]
        [InlineData("\uFB01", "Fi")]
        [InlineData("a", "A")]
        [InlineData("\u10D0", "\u10D0")]                    // Georgian: the lower case letter is the title case, not the capital
        public void ToTitle_UsesTheTitleCaseMapping(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToTitle(text));

        [Theory]
        [InlineData("i\u0307", "I")]                         // the dot above an i carries is dropped when it becomes a capital
        [InlineData("i\u0307\u0300", "I\u0300")]
        [InlineData("ab\u0307", "AB\u0307")]                // not after a soft-dotted letter
        public void ToUpper_Lithuanian_DropsADotAboveAfterASoftDottedLetter(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToUpper(text, "lt"));

        [Fact]
        public void ToUpper_OutsideLithuanian_KeepsTheDot() =>
            Assert.Equal("I\u0307", CaseMapper.ToUpper("i\u0307", "en"));

        [Theory]
        [InlineData("I\u0301", "i\u0307\u0301")]           // a capital I over an accent gets the dot an i would have lost
        [InlineData("I", "i")]                               // nothing above it
        [InlineData("\u00CC", "i\u0307\u0300")]
        [InlineData("J\u0303", "j\u0307\u0303")]
        public void ToLower_Lithuanian_KeepsTheDotAbove(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToLower(text, "lt"));

        [Theory]
        [InlineData("I\u0307", "i")]                         // After_I: the combining dot is absorbed
        [InlineData("I", "\u0131")]
        [InlineData("Ia", "\u0131a")]
        public void ToLower_Turkic_TreatsTheDotAfterIAsPartOfTheI(string text, string expected) =>
            Assert.Equal(expected, CaseMapper.ToLower(text, "tr"));

        [Fact]
        public void SourceIndices_AreNull_WhenTheLengthDoesNotChange()
        {
            CaseMapper.ToUpper("abc", null, out var sources);
            Assert.Null(sources);
        }

        [Fact]
        public void SourceIndices_NameTheCharacterEachResultCharacterCameFrom()
        {
            var result = CaseMapper.ToUpper("aßﬁb", null, out var sources);

            Assert.Equal("ASSFIB", result);
            Assert.Equal([0, 1, 1, 2, 2, 3, 4], sources!);       // a, ß -> S S, fi -> F I, b, and the end
        }

        [Fact]
        public void ALoneSurrogate_MapsToItself()
        {
            Assert.Equal("A\uD800B", CaseMapper.ToUpper("a\uD800b"));
        }
    }
}
