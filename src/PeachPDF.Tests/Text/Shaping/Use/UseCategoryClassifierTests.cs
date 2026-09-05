using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Use
{
    /// <summary>
    /// Coverage for <see cref="UseCategoryClassifier"/> against real Devanagari codepoints (issue
    /// #533, Phase 5b). Expected categories were derived by hand-running HarfBuzz's own
    /// `gen-use-table.py` predicates against the literal Unicode 17.0.0
    /// IndicSyllabicCategory.txt/IndicPositionalCategory.txt lines for the Devanagari block - not
    /// asserted from a black-box reference shaper, since this port has none available in-repo (see
    /// this feature's own recent-fixes entry for that research).
    /// </summary>
    public class UseCategoryClassifierTests
    {
        [Theory]
        [InlineData(0x0915)] // KA - Consonant
        [InlineData(0x0958)] // QA - Consonant (nukta-consonant)
        [InlineData(0x0904)] // Independent vowel SHORT A
        [InlineData(0x0966)] // Digit ZERO - Number maps to B, not a separate numeral category
        [InlineData(0x093D)] // AVAGRAHA (Lo + Avagraha)
        public void BaseCategories(int codepoint) =>
            Assert.Equal(UseCategory.B, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void Nukta_IsConsonantModifierBelow() =>
            Assert.Equal(UseCategory.CMBlw, UseCategoryClassifier.Classify(0x093C));

        [Fact]
        public void Virama_IsHalant() =>
            Assert.Equal(UseCategory.H, UseCategoryClassifier.Classify(0x094D));

        // UseCategory can't be a [Theory] parameter type directly (CS0051 - an internal type in a
        // public method signature), so expected values travel as their underlying byte and are cast
        // back for the assertion.
        [Theory]
        [InlineData(0x093F, (byte)UseCategory.VPre)] // VOWEL SIGN I - the canonical pre-base matra
        [InlineData(0x094E, (byte)UseCategory.VPre)] // VOWEL SIGN PRISHTHAMATRA E - the other pre-base matra
        [InlineData(0x093E, (byte)UseCategory.VPst)] // VOWEL SIGN AA - post-base (right side)
        [InlineData(0x0940, (byte)UseCategory.VPst)] // VOWEL SIGN II
        [InlineData(0x0945, (byte)UseCategory.VAbv)] // VOWEL SIGN CANDRA E - above-base
        [InlineData(0x0941, (byte)UseCategory.VBlw)] // VOWEL SIGN U - below-base
        public void DependentVowelSigns_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0900, (byte)UseCategory.VMAbv)] // SIGN INVERTED CANDRABINDU
        [InlineData(0x0901, (byte)UseCategory.VMAbv)] // SIGN CANDRABINDU
        [InlineData(0x0902, (byte)UseCategory.VMAbv)] // SIGN ANUSVARA
        [InlineData(0x0903, (byte)UseCategory.VMPst)] // SIGN VISARGA
        [InlineData(0x0951, (byte)UseCategory.VMAbv)] // STRESS SIGN UDATTA
        [InlineData(0x0952, (byte)UseCategory.VMBlw)] // STRESS SIGN ANUDATTA
        public void VowelModifiers_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0950)] // OM - unlisted, General_Category Lo but Indic_Syllabic_Category Other
        [InlineData(0x0964)] // DANDA - unlisted, General_Category Po
        [InlineData(0x0965)] // DOUBLE DANDA
        public void PunctuationAndSymbols_AreOther(int codepoint) =>
            Assert.Equal(UseCategory.O, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void ZeroWidthJoiner_IsCGJ() =>
            Assert.Equal(UseCategory.CGJ, UseCategoryClassifier.Classify(0x200D));

        [Fact]
        public void ZeroWidthNonJoiner_IsZWNJ() =>
            Assert.Equal(UseCategory.ZWNJ, UseCategoryClassifier.Classify(0x200C));

        [Fact]
        public void LatinLetter_FallsBackToOther()
        {
            // A foreign codepoint reaching this classifier at all is already an edge case (see the
            // classifier's own remarks) - it must degrade to a safe, non-participating "other"
            // category rather than being misclassified as Indic structure.
            Assert.Equal(UseCategory.O, UseCategoryClassifier.Classify('A'));
        }
    }
}
