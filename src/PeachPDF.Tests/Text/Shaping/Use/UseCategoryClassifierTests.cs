using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Use
{
    /// <summary>
    /// Coverage for <see cref="UseCategoryClassifier"/> against real Devanagari codepoints (issue
    /// #533, Phase 5b), and real Bengali/Gujarati/Tamil codepoints (issue #533, Phase 5c). Expected
    /// categories were derived by hand-running HarfBuzz's own `gen-use-table.py` predicates against
    /// the literal Unicode 17.0.0 IndicSyllabicCategory.txt/IndicPositionalCategory.txt lines for each
    /// script's own block - not asserted from a black-box reference shaper, since this port has none
    /// available in-repo (see this feature's own recent-fixes entries for that research, including the
    /// full per-block enumeration that verified Gujarati/Tamil need no new classifier code at all).
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

        [Fact]
        public void BengaliAnji_IsConsonantPlaceholder() =>
            // U+0980 BENGALI ANJI - the only Bengali/Devanagari/Gujarati/Tamil codepoint with
            // Indic_Syllabic_Category=Consonant_Placeholder (is_BASE_OTHER in real HarfBuzz).
            Assert.Equal(UseCategory.GB, UseCategoryClassifier.Classify(0x0980));

        [Fact]
        public void BengaliSandhiMark_IsSyllableModifierAboveBase() =>
            // U+09FE BENGALI SANDHI MARK - Indic_Syllabic_Category=Syllable_Modifier,
            // Indic_Positional_Category=Top, resolving to FMAbv per HarfBuzz's own
            // use_positions['FM'] = {'Abv': [Top], ...} mapping.
            Assert.Equal(UseCategory.FMAbv, UseCategoryClassifier.Classify(0x09FE));

        [Fact]
        public void BengaliVedicAnusvaraLetter_IsBaseNotVowelModifier() =>
            // U+09FC BENGALI LETTER VEDIC ANUSVARA - General_Category=Lo (a full letter, unlike every
            // other Bindu codepoint in these four scripts, which are all combining marks) - real
            // HarfBuzz's is_BASE also lists Bindu in its own Lo-gated clause, so this resolves to B,
            // not VMAbv/VMBlw/etc. the way a combining-mark Bindu would.
            Assert.Equal(UseCategory.B, UseCategoryClassifier.Classify(0x09FC));

        [Fact]
        public void BengaliConsonantDead_FallsBackToOther() =>
            // U+09CE BENGALI LETTER KHANDA TA - Indic_Syllabic_Category=Consonant_Dead, which real
            // HarfBuzz's own is_OTHER explicitly includes (not is_BASE) - already-correct catch-all
            // behavior needing no new classifier branch.
            Assert.Equal(UseCategory.O, UseCategoryClassifier.Classify(0x09CE));

        [Theory]
        [InlineData(0x0995)] // KA - Consonant
        [InlineData(0x0985)] // Independent vowel A
        [InlineData(0x09E6)] // Digit ZERO
        [InlineData(0x09BD)] // AVAGRAHA (Lo + Avagraha)
        public void BengaliBaseCategories(int codepoint) =>
            Assert.Equal(UseCategory.B, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void BengaliNukta_IsConsonantModifierBelow() =>
            Assert.Equal(UseCategory.CMBlw, UseCategoryClassifier.Classify(0x09BC));

        [Fact]
        public void BengaliVirama_IsHalant() =>
            Assert.Equal(UseCategory.H, UseCategoryClassifier.Classify(0x09CD));

        [Theory]
        [InlineData(0x09BF, (byte)UseCategory.VPre)] // VOWEL SIGN I - pre-base (Left)
        [InlineData(0x09BE, (byte)UseCategory.VPst)] // VOWEL SIGN AA - post-base (Right)
        [InlineData(0x09CB, (byte)UseCategory.VPre)] // VOWEL SIGN O - Left_And_Right maps to VPre
        [InlineData(0x09C1, (byte)UseCategory.VBlw)] // VOWEL SIGN U - below-base (Bottom)
        public void BengaliDependentVowelSigns_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0981, (byte)UseCategory.VMAbv)] // SIGN CANDRABINDU (Bindu, Top)
        [InlineData(0x0982, (byte)UseCategory.VMPst)] // SIGN ANUSVARA (Bindu, Right)
        [InlineData(0x0983, (byte)UseCategory.VMPst)] // SIGN VISARGA (Right)
        public void BengaliVowelModifiers_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0A95)] // GUJARATI LETTER KA - Consonant
        [InlineData(0x0A85)] // GUJARATI LETTER A - independent vowel
        [InlineData(0x0ABD)] // GUJARATI SIGN AVAGRAHA
        public void GujaratiBaseCategories_NeedNoNewClassifierCode(int codepoint) =>
            Assert.Equal(UseCategory.B, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void GujaratiVirama_IsHalant() =>
            Assert.Equal(UseCategory.H, UseCategoryClassifier.Classify(0x0ACD));

        [Theory]
        [InlineData(0x0ABC, (byte)UseCategory.CMBlw)] // SIGN NUKTA (Bottom)
        [InlineData(0x0AFB, (byte)UseCategory.CMAbv)] // SIGN SHADDA (Gemination_Mark, Top)
        public void GujaratiConsonantModifiers_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0ABE, (byte)UseCategory.VPst)] // VOWEL SIGN AA (Right)
        [InlineData(0x0ABF, (byte)UseCategory.VPre)] // VOWEL SIGN I (Left)
        [InlineData(0x0AC7, (byte)UseCategory.VAbv)] // VOWEL SIGN E (Top)
        public void GujaratiDependentVowelSigns_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0A82, (byte)UseCategory.VMAbv)] // SIGN ANUSVARA (Bindu, Top)
        [InlineData(0x0A83, (byte)UseCategory.VMPst)] // SIGN VISARGA (Right)
        [InlineData(0x0AFA, (byte)UseCategory.VMAbv)] // SIGN SUKUN (Cantillation_Mark, Top)
        public void GujaratiVowelModifiers_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x0B95)] // TAMIL LETTER KA - Consonant
        [InlineData(0x0B85)] // TAMIL LETTER A - independent vowel
        [InlineData(0x0BE6)] // TAMIL DIGIT ZERO
        public void TamilBaseCategories_NeedNoNewClassifierCode(int codepoint) =>
            Assert.Equal(UseCategory.B, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void TamilVirama_IsHalant() =>
            // Indic_Positional_Category=Top for Tamil's own virama, unlike Devanagari's/Bengali's/
            // Gujarati's own Bottom - is_HALANT never consults position at all, so this still resolves
            // to plain H.
            Assert.Equal(UseCategory.H, UseCategoryClassifier.Classify(0x0BCD));

        [Fact]
        public void TamilVisarga_FallsBackToOther() =>
            // U+0B83 TAMIL SIGN VISARGA - General_Category=Lo, Indic_Syllabic_Category=Modifying_Letter
            // (unlike Devanagari's/Bengali's/Gujarati's own combining-mark Visarga) - real HarfBuzz's
            // own is_OTHER explicitly includes Modifying_Letter, so this is already-correct catch-all
            // behavior needing no new classifier branch.
            Assert.Equal(UseCategory.O, UseCategoryClassifier.Classify(0x0B83));

        [Theory]
        [InlineData(0x0BBE, (byte)UseCategory.VPst)] // VOWEL SIGN AA (Right)
        [InlineData(0x0BC6, (byte)UseCategory.VPre)] // VOWEL SIGN E (Left)
        [InlineData(0x0BCA, (byte)UseCategory.VPre)] // VOWEL SIGN O (Left_And_Right maps to VPre)
        public void TamilDependentVowelSigns_ResolvePositionCorrectly(int codepoint, byte expected) =>
            Assert.Equal((UseCategory)expected, UseCategoryClassifier.Classify(codepoint));

        [Fact]
        public void TamilAnusvara_IsVowelModifierAboveBase() =>
            Assert.Equal(UseCategory.VMAbv, UseCategoryClassifier.Classify(0x0B82));
    }
}
