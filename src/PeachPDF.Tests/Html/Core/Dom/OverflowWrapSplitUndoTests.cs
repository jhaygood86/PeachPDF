using PeachPDF.Html.Core.Dom;
using PeachPDF.Text.Shaping.Arabic;
using PeachPDF.Text.Shaping.Use;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Emergency wrapping temporarily replaces one word in its owner's word list with linked fragments.
    /// A line discarded at a fragmentainer boundary must put the original word back so the resumed pass
    /// can choose a split for its own available width.
    /// </summary>
    public class OverflowWrapSplitUndoTests
    {
        [Fact]
        public void DiscardedLine_PrefixRestoresOriginalWordAndDefersItsFragmentClaim()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "abcdefgh", true, true);
            var (prefix, suffix) = original.SplitForOverflowWrap(3);
            owner.Words.Add(prefix);
            owner.Words.Add(suffix);

            var line = new CssLineBox(owner);
            line.Words.Add(prefix);

            CssLayoutEngine.UndoAbandonedOverflowWrapSplits(line);

            Assert.Single(owner.Words);
            Assert.Same(original, owner.Words[0]);
            Assert.True(original.AwaitsTheNextFragmentainer);
        }

        [Fact]
        public void DiscardedLine_SplitSurroundedByOtherWords_OnlyMergesTheLinkedPair()
        {
            var owner = new CssBox(null, null);
            var before = new CssRectWord(owner, "before", false, true);
            var original = new CssRectWord(owner, "abcdefgh", true, true);
            var (prefix, suffix) = original.SplitForOverflowWrap(3);
            var after = new CssRectWord(owner, "after", true, false);
            owner.Words.Add(before);
            owner.Words.Add(prefix);
            owner.Words.Add(suffix);
            owner.Words.Add(after);

            var line = new CssLineBox(owner);
            line.Words.Add(before);
            line.Words.Add(prefix);

            CssLayoutEngine.UndoAbandonedOverflowWrapSplits(line);

            Assert.Equal([before, original, after], owner.Words);
        }

        [Fact]
        public void DiscardedLine_SuffixWithoutItsCommittedPrefix_IsLeftUntouched()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "abcdefgh", false, false);
            var (prefix, suffix) = original.SplitForOverflowWrap(3);
            owner.Words.Add(prefix);
            owner.Words.Add(suffix);

            var discardedLine = new CssLineBox(owner);
            discardedLine.Words.Add(suffix);

            CssLayoutEngine.UndoAbandonedOverflowWrapSplits(discardedLine);

            Assert.Equal([prefix, suffix], owner.Words);
        }

        [Fact]
        public void DiscardedLine_NonAdjacentLinkedSuffix_IsLeftUntouched()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "abcdefgh", false, false);
            var (prefix, suffix) = original.SplitForOverflowWrap(3);
            var intervening = new CssRectWord(owner, "intervening", true, true);
            owner.Words.Add(prefix);
            owner.Words.Add(intervening);
            owner.Words.Add(suffix);

            var discardedLine = new CssLineBox(owner);
            discardedLine.Words.Add(prefix);

            CssLayoutEngine.UndoAbandonedOverflowWrapSplits(discardedLine);

            Assert.Equal([prefix, intervening, suffix], owner.Words);
            Assert.False(original.AwaitsTheNextFragmentainer);
        }

        [Fact]
        public void Split_PreservesCachedAnywhereMinContentContribution()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "abcdefgh", false, false)
            {
                OverflowWrapMinWidth = 7.5
            };

            var (prefix, suffix) = original.SplitForOverflowWrap(3);

            Assert.Equal(7.5, prefix.OverflowWrapMinWidth);
            Assert.Equal(7.5, suffix.OverflowWrapMinWidth);
        }

        [Fact]
        public void Split_PreservesAndSlicesTextShapingMetadata()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "abcdef", false, false, "ABCDEF",
                [ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina,
                    ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina],
                [UseCategory.B, UseCategory.H, UseCategory.B, UseCategory.B, UseCategory.H, UseCategory.B])
            {
                FirstLineText = "ABCDEF",
                HyphenationCandidates = [1, 3, 5]
            };
            original.MarkDisplayOrderReversed();

            var (prefix, suffix) = original.SplitForOverflowWrap(3);

            Assert.Equal("ABC", prefix.FirstLineText);
            Assert.Equal("DEF", suffix.FirstLineText);
            Assert.True(prefix.DisplayOrderReversed);
            Assert.True(suffix.DisplayOrderReversed);
            Assert.Equal([1], prefix.HyphenationCandidates);
            Assert.Equal([2], suffix.HyphenationCandidates);
            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina],
                prefix.EffectiveJoiningForms);
            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina],
                suffix.EffectiveJoiningForms);
            Assert.Equal([UseCategory.B, UseCategory.H, UseCategory.B], prefix.EffectiveUseCategories);
            Assert.Equal([UseCategory.B, UseCategory.H, UseCategory.B], suffix.EffectiveUseCategories);
        }
    }
}
