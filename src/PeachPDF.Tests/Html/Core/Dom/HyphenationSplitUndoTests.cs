using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <c>CssLayoutEngine.TryHyphenateWord</c> mutates the owning box's <see cref="CssBox.Words"/> list
    /// in place - splitting one word into a prefix/suffix pair - and a resumed fragmentainer pass
    /// re-walks that same, already-mutated list. <c>CreateLineBoxes.UndoAbandonedHyphenationSplits</c>
    /// (issue #344) undoes a split whose prefix ends up part of a line the pass discards at a break, so
    /// the resumed pass decides afresh against the width it will actually have, rather than re-flowing a
    /// stale prefix/suffix pair where the whole original word would have fit. These tests exercise that
    /// mechanism directly, against hand-built words carrying the same linkage
    /// <c>TryHyphenateWord</c> sets, rather than depending on a specific page geometry to trigger it.
    /// </summary>
    public class HyphenationSplitUndoTests
    {
        [Fact]
        public void DiscardedLine_PrefixIsFirstMemberOfSplit_RestoresOriginalWordInOwnerBoxWords()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(owner, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(owner, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;

            owner.Words.Add(prefix);
            owner.Words.Add(suffix);

            var line = new CssLineBox(owner);
            line.Words.Add(prefix);

            CssLayoutEngine.UndoAbandonedHyphenationSplits(line);

            Assert.Single(owner.Words);
            Assert.Same(original, owner.Words[0]);

            // Never positioned this pass - FragmentEmitter must not read its (meaningless) Top/Left as
            // a real placement and claim it into the fragment that is about to close.
            Assert.True(original.AwaitsTheNextFragmentainer);
        }

        [Fact]
        public void DiscardedLine_SplitSurroundedByOtherWords_OnlyTheSplitPairIsMerged()
        {
            var owner = new CssBox(null, null);
            var before = new CssRectWord(owner, "before", true, true);
            var original = new CssRectWord(owner, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(owner, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(owner, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;
            var after = new CssRectWord(owner, "after", true, true);

            owner.Words.Add(before);
            owner.Words.Add(prefix);
            owner.Words.Add(suffix);
            owner.Words.Add(after);

            var line = new CssLineBox(owner);
            line.Words.Add(before);
            line.Words.Add(prefix);

            CssLayoutEngine.UndoAbandonedHyphenationSplits(line);

            Assert.Equal([before, original, after], owner.Words);
        }

        [Fact]
        public void DiscardedLine_OrdinaryWord_IsLeftUntouched()
        {
            var owner = new CssBox(null, null);
            var word = new CssRectWord(owner, "plain", true, true);
            owner.Words.Add(word);

            var line = new CssLineBox(owner);
            line.Words.Add(word);

            CssLayoutEngine.UndoAbandonedHyphenationSplits(line);

            Assert.Single(owner.Words);
            Assert.Same(word, owner.Words[0]);
        }

        // The legitimate case (not #344's bug): the prefix already committed to an earlier, kept line -
        // it is never a member of the line being discarded - and only the suffix, alone, starts the
        // fragmentainer that got thrown away. That is an ordinary hyphen straddling the break, and must
        // survive unchanged: the suffix carries no PreSplitWord/HyphenationSuffix of its own, so the
        // pattern match in UndoAbandonedHyphenationSplits never matches it.
        [Fact]
        public void DiscardedLine_SuffixAloneWithoutItsPrefix_IsLeftUntouched()
        {
            var owner = new CssBox(null, null);
            var original = new CssRectWord(owner, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(owner, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(owner, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;

            owner.Words.Add(prefix);
            owner.Words.Add(suffix);

            // prefix already committed to an earlier, kept line - not part of the discarded one.
            var keptLine = new CssLineBox(owner);
            keptLine.Words.Add(prefix);

            var discardedLine = new CssLineBox(owner);
            discardedLine.Words.Add(suffix);

            CssLayoutEngine.UndoAbandonedHyphenationSplits(discardedLine);

            Assert.Equal([prefix, suffix], owner.Words);
        }

        [Fact]
        public void DiscardedLine_Empty_DoesNothing()
        {
            var owner = new CssBox(null, null);
            var line = new CssLineBox(owner);

            CssLayoutEngine.UndoAbandonedHyphenationSplits(line);

            Assert.Empty(owner.Words);
        }
    }
}
