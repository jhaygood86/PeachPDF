using System.Collections.Generic;
using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Use
{
    /// <summary>
    /// Coverage for <see cref="UseSyllableScanner"/>'s reduced Devanagari grammar (issue #533, Phase
    /// 5b) - operates directly on hand-built <see cref="UseCategory"/> sequences (bypassing
    /// <see cref="UseCategoryClassifier"/> entirely) so each test isolates one grammar shape.
    /// </summary>
    public class UseSyllableScannerTests
    {
        private static List<UseSyllable> Scan(params UseCategory[] categories) =>
            UseSyllableScanner.Scan(categories);

        [Fact]
        public void SimpleConsonantVowel_IsOneStandardCluster()
        {
            // KA + VOWEL SIGN AA ("का") - B VPst.
            var syllables = Scan(UseCategory.B, UseCategory.VPst);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 2, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void BareBase_IsOneStandardClusterOfLengthOne()
        {
            var syllables = Scan(UseCategory.B);

            Assert.Equal([new UseSyllable(0, 1, UseSyllableType.StandardCluster)], syllables);
        }

        [Fact]
        public void MultiConsonantConjunct_StaysOneStandardCluster()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN I ("क्षि") - B H B VPre. The (H B) repeat inside
            // consonant_modifiers is what keeps a full conjunct as one syllable rather than two.
            var syllables = Scan(UseCategory.B, UseCategory.H, UseCategory.B, UseCategory.VPre);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 4, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void TrailingWordFinalVirama_IsConsumedByTheSameSyllable()
        {
            // A word-final halant, matching dependent_vowels' own "| H" alternative.
            var syllables = Scan(UseCategory.B, UseCategory.H);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 2, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void StackedVowelModifiers_StayInTheSameSyllable()
        {
            var syllables = Scan(UseCategory.B, UseCategory.VMAbv, UseCategory.VMPst);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 3, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void LeadingDependentVowelWithNoBase_IsABrokenCluster()
        {
            var syllables = Scan(UseCategory.VPre);

            Assert.Equal([new UseSyllable(0, 1, UseSyllableType.BrokenCluster)], syllables);
        }

        [Fact]
        public void Punctuation_IsASymbolClusterOfLengthOne()
        {
            var syllables = Scan(UseCategory.O);

            Assert.Equal([new UseSyllable(0, 1, UseSyllableType.SymbolCluster)], syllables);
        }

        [Fact]
        public void PunctuationFollowedByAttachingModifier_ExtendsTheSymbolCluster()
        {
            var syllables = Scan(UseCategory.O, UseCategory.VMAbv);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 2, UseSyllableType.SymbolCluster), syllable);
        }

        [Fact]
        public void TwoConsecutiveSyllables_AreScannedSeparately()
        {
            // "कावि" as two independent CV syllables for this test's purposes: B VPst | B VPre.
            var syllables = Scan(UseCategory.B, UseCategory.VPst, UseCategory.B, UseCategory.VPre);

            Assert.Equal(
            [
                new UseSyllable(0, 2, UseSyllableType.StandardCluster),
                new UseSyllable(2, 2, UseSyllableType.StandardCluster),
            ], syllables);
        }

        [Fact]
        public void CombiningGraphemeJoiner_IsAbsorbedIntoThePrecedingSyllable()
        {
            var syllables = Scan(UseCategory.B, UseCategory.CGJ);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 2, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void LeadingCombiningGraphemeJoiner_IsItsOwnNonCluster()
        {
            var syllables = Scan(UseCategory.CGJ, UseCategory.B);

            Assert.Equal(
            [
                new UseSyllable(0, 1, UseSyllableType.NonCluster),
                new UseSyllable(1, 1, UseSyllableType.StandardCluster),
            ], syllables);
        }

        [Fact]
        public void TrailingZeroWidthNonJoiner_IsConsumedByTheSameSyllable()
        {
            var syllables = Scan(UseCategory.B, UseCategory.H, UseCategory.ZWNJ);

            var syllable = Assert.Single(syllables);
            Assert.Equal(new UseSyllable(0, 3, UseSyllableType.StandardCluster), syllable);
        }

        [Fact]
        public void EmptyInput_ProducesNoSyllables() =>
            Assert.Empty(UseSyllableScanner.Scan([]));
    }
}
