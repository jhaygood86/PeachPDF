using PeachDrawing.Text.Unicode;
using System.Collections.Generic;
using PeachDrawing.Text.Internal.Text.Shaping.Khmer;
using Xunit;

namespace PeachDrawing.Text.Tests.Text.Shaping.Khmer
{
    /// <summary>
    /// Coverage for <see cref="KhmerSyllableScanner"/>'s grammar (ported from
    /// <c>hb-ot-shaper-khmer-machine.rl</c> - see that class's own remarks) - operates directly on
    /// hand-built <see cref="KhmerCategory"/> sequences (bypassing <see cref="KhmerCategoryClassifier"/>
    /// entirely) so each test isolates one grammar shape.
    /// </summary>
    public class KhmerSyllableScannerTests
    {
        private static List<KhmerSyllable> Scan(params KhmerCategory[] categories) =>
            KhmerSyllableScanner.Scan(categories);

        [Fact]
        public void BareConsonant_IsOneConsonantSyllableOfLengthOne()
        {
            var syllables = Scan(KhmerCategory.C);

            Assert.Equal([new KhmerSyllable(0, 1, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void CoengRo_StaysInTheSameSyllableAsItsBase()
        {
            // KA + COENG + RO (ក្រ) - C H Ra.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra);

            Assert.Equal([new KhmerSyllable(0, 3, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void CoengSubjoinedConsonant_StaysInTheSameSyllableAsItsBase()
        {
            // KA + COENG + SA (ក្ស) - C H C - the (H.cn)* loop inside broken_cluster.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.H, KhmerCategory.C);

            Assert.Equal([new KhmerSyllable(0, 3, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void MultipleSubjoinedConsonants_AllStayInTheSameSyllable()
        {
            // Base + COENG + C + COENG + C - the (H.cn)* loop repeating twice.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.H, KhmerCategory.C, KhmerCategory.H, KhmerCategory.C);

            Assert.Equal([new KhmerSyllable(0, 5, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Theory]
        [InlineData(KhmerCategory.VPre)]
        [InlineData(KhmerCategory.VAbv)]
        [InlineData(KhmerCategory.VBlw)]
        [InlineData(KhmerCategory.VPst)]
        public void DependentVowelSign_StaysInTheSameSyllableAsItsBase(KhmerCategory vowel)
        {
            var syllables = Scan(KhmerCategory.C, vowel);

            Assert.Equal([new KhmerSyllable(0, 2, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void TrailingXgroupSign_StaysInTheSameSyllable()
        {
            var syllables = Scan(KhmerCategory.C, KhmerCategory.Xgroup);

            Assert.Equal([new KhmerSyllable(0, 2, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void TrailingYgroupSign_StaysInTheSameSyllable()
        {
            var syllables = Scan(KhmerCategory.C, KhmerCategory.Ygroup);

            Assert.Equal([new KhmerSyllable(0, 2, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void JoinerBeforeXgroupSign_IsConsumedTransparently()
        {
            // xgroup = (joiner* . Xgroup)* - a ZWJ directly before an Xgroup sign is part of the group.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.ZWJ, KhmerCategory.Xgroup);

            Assert.Equal([new KhmerSyllable(0, 3, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void RobatViaJoiner_IsPartOfTheLeadingConsonantUnit()
        {
            // cn = c . ((ZWJ|ZWNJ)? . Robatic)? - consumed entirely by the prefix, never reaching
            // broken_cluster at all.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.ZWJ, KhmerCategory.Robatic);

            Assert.Equal([new KhmerSyllable(0, 3, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void BareRobat_IsPartOfTheLeadingConsonantUnit()
        {
            var syllables = Scan(KhmerCategory.C, KhmerCategory.Robatic);

            Assert.Equal([new KhmerSyllable(0, 2, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void TwoBareConsonants_AreTwoSeparateSyllables()
        {
            var syllables = Scan(KhmerCategory.C, KhmerCategory.C);

            Assert.Equal(
            [
                new KhmerSyllable(0, 1, KhmerSyllableType.ConsonantSyllable),
                new KhmerSyllable(1, 1, KhmerSyllableType.ConsonantSyllable),
            ], syllables);
        }

        [Fact]
        public void LeadingBareCoeng_IsABrokenCluster()
        {
            var syllables = Scan(KhmerCategory.H);

            Assert.Equal([new KhmerSyllable(0, 1, KhmerSyllableType.BrokenCluster)], syllables);
        }

        [Fact]
        public void LeadingDependentVowel_IsABrokenCluster()
        {
            var syllables = Scan(KhmerCategory.VAbv);

            Assert.Equal([new KhmerSyllable(0, 1, KhmerSyllableType.BrokenCluster)], syllables);
        }

        [Fact]
        public void ForeignCharacter_IsItsOwnNonKhmerCluster()
        {
            var syllables = Scan(KhmerCategory.Other);

            Assert.Equal([new KhmerSyllable(0, 1, KhmerSyllableType.NonKhmerCluster)], syllables);
        }

        [Fact]
        public void ConsonantThenForeignCharacter_AreTwoSeparateSyllables()
        {
            var syllables = Scan(KhmerCategory.C, KhmerCategory.Other);

            Assert.Equal(
            [
                new KhmerSyllable(0, 1, KhmerSyllableType.ConsonantSyllable),
                new KhmerSyllable(1, 1, KhmerSyllableType.NonKhmerCluster),
            ], syllables);
        }

        [Fact]
        public void JoinerBeforeAboveBaseVowelInMatraGroup_IsConsumedTransparently()
        {
            // matra_group's own "(joiner? . VAbv)?" - a ZWJ directly before an above-base vowel sign
            // is part of the same optional group, not a syllable boundary.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.ZWJ, KhmerCategory.VAbv);

            Assert.Equal([new KhmerSyllable(0, 3, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void PostMatraCoengConsonant_IsConsumedBySyllableTailsOwnTrailingPair()
        {
            // syllable_tail's own trailing "(H . c)?" - a coeng+consonant pair appearing AFTER the
            // matra_group's own vowel-sign content (so broken_cluster's leading "(H.cn)*" loop, which
            // runs before matra_group is even reached, never gets a chance to claim it first).
            var syllables = Scan(KhmerCategory.C, KhmerCategory.VAbv, KhmerCategory.H, KhmerCategory.C);

            Assert.Equal([new KhmerSyllable(0, 4, KhmerSyllableType.ConsonantSyllable)], syllables);
        }

        [Fact]
        public void LeadingBareRobat_IsABrokenClusterOfLengthOne()
        {
            // broken_cluster's own leading "Robatic?" - a Robat/register-shifter with no base
            // consonant before it at all (malformed, but handled the same way a leading bare coeng is).
            var syllables = Scan(KhmerCategory.Robatic);

            Assert.Equal([new KhmerSyllable(0, 1, KhmerSyllableType.BrokenCluster)], syllables);
        }

        [Fact]
        public void CoengRoFollowedByPreBaseVowel_AllStayInOneSyllable()
        {
            // KA + COENG + RO + VOWEL SIGN E - C H Ra VPre, the exact sequence
            // KhmerReordererTests/the characterization tests verify the reordered glyph order for.
            var syllables = Scan(KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra, KhmerCategory.VPre);

            Assert.Equal([new KhmerSyllable(0, 4, KhmerSyllableType.ConsonantSyllable)], syllables);
        }
    }
}
