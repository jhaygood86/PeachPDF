using PeachDrawing.Text.Unicode;
using PeachDrawing.Text.Internal.Text.Shaping.Khmer;
using Xunit;

namespace PeachDrawing.Text.Tests.Text.Shaping.Khmer
{
    /// <summary>
    /// Coverage for <see cref="KhmerCategoryClassifier"/> - one codepoint at a time, cross-checked
    /// against the Unicode Character Database's own <c>Indic_Syllabic_Category</c>/
    /// <c>Indic_Positional_Category</c> data plus HarfBuzz's own hand-curated overrides (see that
    /// class's own remarks for exactly which UCD data each case reflects).
    /// </summary>
    public class KhmerCategoryClassifierTests
    {
        [Theory]
        [InlineData(0x1780, KhmerCategory.C)] // KA
        [InlineData(0x17A2, KhmerCategory.C)] // QA (last ordinary consonant)
        [InlineData(0x179A, KhmerCategory.Ra)] // RO
        [InlineData(0x17A3, KhmerCategory.V)] // INDEPENDENT VOWEL QAQ
        [InlineData(0x17B3, KhmerCategory.V)] // INDEPENDENT VOWEL QAU
        [InlineData(0x17D2, KhmerCategory.H)] // COENG
        public void ConsonantsAndCoeng(int codepoint, KhmerCategory expected) =>
            Assert.Equal(expected, KhmerCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x17C1, KhmerCategory.VPre)] // VOWEL SIGN E (Left)
        [InlineData(0x17C2, KhmerCategory.VPre)] // VOWEL SIGN AE (Left)
        [InlineData(0x17C3, KhmerCategory.VPre)] // VOWEL SIGN AI (Left)
        [InlineData(0x17B7, KhmerCategory.VAbv)] // VOWEL SIGN I (Top)
        [InlineData(0x17BA, KhmerCategory.VAbv)] // VOWEL SIGN YY (Top)
        [InlineData(0x17BB, KhmerCategory.VBlw)] // VOWEL SIGN U (Bottom)
        [InlineData(0x17BD, KhmerCategory.VBlw)] // VOWEL SIGN UA (Bottom)
        [InlineData(0x17B6, KhmerCategory.VPst)] // VOWEL SIGN AA (Right)
        [InlineData(0x17C0, KhmerCategory.VPst)] // VOWEL SIGN IE (Left_And_Right)
        [InlineData(0x17BE, KhmerCategory.VAbv)] // VOWEL SIGN OE (Top_And_Left)
        [InlineData(0x17BF, KhmerCategory.VPst)] // VOWEL SIGN YA (Top_And_Left_And_Right)
        public void DependentVowelSigns_ResolveByPositionalCategory(int codepoint, KhmerCategory expected) =>
            Assert.Equal(expected, KhmerCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x17CC, KhmerCategory.Robatic)] // SIGN ROBAT
        [InlineData(0x17C9, KhmerCategory.Robatic)] // SIGN MUUSIKATOAN
        [InlineData(0x17CA, KhmerCategory.Robatic)] // SIGN TRIISAP
        [InlineData(0x17C6, KhmerCategory.Xgroup)] // SIGN NIKAHIT
        [InlineData(0x17CB, KhmerCategory.Xgroup)] // SIGN BANTOC
        [InlineData(0x17CD, KhmerCategory.Xgroup)] // SIGN TOANDAKHIAT
        [InlineData(0x17CE, KhmerCategory.Xgroup)] // SIGN KAKABAT
        [InlineData(0x17CF, KhmerCategory.Xgroup)] // SIGN AHSDA
        [InlineData(0x17D0, KhmerCategory.Xgroup)] // SIGN SAMYOK SANNYA
        [InlineData(0x17D1, KhmerCategory.Xgroup)] // SIGN VIRIAM
        [InlineData(0x17C7, KhmerCategory.Ygroup)] // SIGN REAHMUK
        [InlineData(0x17C8, KhmerCategory.Ygroup)] // SIGN YUUKALEAPINTU
        [InlineData(0x17D3, KhmerCategory.Ygroup)] // SIGN BATHAMASAT
        [InlineData(0x17DD, KhmerCategory.Ygroup)] // SIGN ATTHACAN
        public void HarfBuzzCuratedOverrides(int codepoint, KhmerCategory expected) =>
            Assert.Equal(expected, KhmerCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x17E0, KhmerCategory.Placeholder)] // DIGIT ZERO
        [InlineData(0x17E9, KhmerCategory.Placeholder)] // DIGIT NINE
        [InlineData(0x17D9, KhmerCategory.Placeholder)] // SIGN PHNAEK MUAN
        [InlineData(0x25CC, KhmerCategory.DottedCircle)]
        [InlineData(0x200D, KhmerCategory.ZWJ)]
        [InlineData(0x200C, KhmerCategory.ZWNJ)]
        public void PlaceholdersAndJoiners(int codepoint, KhmerCategory expected) =>
            Assert.Equal(expected, KhmerCategoryClassifier.Classify(codepoint));

        [Theory]
        [InlineData(0x17DC)] // SIGN AVAKRAHASANYA (Avagraha -> Symbol, no Khmer grammar role)
        [InlineData(0x0041)] // Latin 'A'
        [InlineData(0x17D4)] // KHAN (punctuation, unassigned ISC)
        public void UnassignedOrForeignCodepoints_ClassifyAsOther(int codepoint) =>
            Assert.Equal(KhmerCategory.Other, KhmerCategoryClassifier.Classify(codepoint));
    }
}
