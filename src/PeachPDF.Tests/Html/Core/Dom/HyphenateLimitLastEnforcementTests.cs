using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using System.Linq;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <c>hyphenate-limit-last</c> (CSS Text 4 §6.3.5, issue #723): whether the last line before a
    /// column/page/spread break - or, under <c>always</c>, before any of them - may end in a hyphen.
    /// <c>CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak</c> is the rewind
    /// <c>CreateLineBoxes</c> runs the moment a fragmentainer break is discovered: it undoes a forbidden
    /// split on the line that break just kept, so the whole original word moves into the fragmentainer
    /// the break resumes in instead - the same "whole word moves on" outcome an ordinary overflowing
    /// (never-hyphenated) word already gets at a boundary. Exercised directly against hand-built words
    /// carrying the same linkage <c>TryHyphenateWord</c> sets, mirroring
    /// <see cref="HyphenationSplitUndoTests"/>, rather than depending on exact page geometry to trigger it.
    /// </summary>
    public class HyphenateLimitLastEnforcementTests
    {
        /// <summary>
        /// A split whose prefix follows a filler word on <c>keptLine</c> - i.e. the split was made
        /// against a line that was already partly full, so moving the whole word to a fresh, full-width
        /// line on the next fragmentainer genuinely gains it room. This is the shape the undo is meant to
        /// act on; <see cref="BuildSplitAsSoleWordOnItsLine"/> is the other one, where it must not.
        /// </summary>
        private static (CssBox blockBox, CssRectWord prefix, CssRectWord suffix, CssLineBox keptLine,
            CssLineBox discardedLine, InlineBreakToken token) BuildSplitAtABreak(HyphenateLimitLast limit)
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue(
                    limit.ToString().ToLowerInvariant(), limit)
            };

            var filler = new CssRectWord(blockBox, "x", true, true);
            var original = new CssRectWord(blockBox, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(blockBox, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(blockBox, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;
            suffix.HyphenationPrefix = prefix;

            blockBox.Words.Add(filler);
            blockBox.Words.Add(prefix);
            blockBox.Words.Add(suffix);

            var keptLine = new CssLineBox(blockBox);
            keptLine.Words.Add(filler);
            keptLine.Words.Add(prefix);

            // The suffix opens its own (about-to-be-discarded) line - mirroring CreateLineBoxes, which
            // has already removed the discarded line from blockBox.LineBoxes by the time this rewind runs.
            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(suffix);
            blockBox.LineBoxes.Remove(discardedLine);

            // Nonzero so AssertForbiddingValueUndoesTheSplit can confirm the undo resets the carried count
            // to 0 rather than merely leaving it untouched - keptLine (the line being un-hyphenated) is
            // exactly the line this count's most recent member counted, per InlineBreakToken.
            // ConsecutiveHyphenatedLines's own contract.
            var token = new InlineBreakToken(blockBox, ResumeSlotIndex: 1, ResumePath: [], ResumeWordIndex: 8,
                CompletedLineCount: 3, ConsecutiveHyphenatedLines: 2);

            return (blockBox, prefix, suffix, keptLine, discardedLine, token);
        }

        /// <summary>
        /// A split whose prefix is the <i>only</i> word on <c>keptLine</c> - already a fresh, full-width
        /// line, the same width the next fragmentainer's opening line offers. Undoing this split would
        /// gain the word nothing and recreate the identical shape there, forever - the livelock
        /// <c>EnforceHyphenateLimitLastBeforeBreak</c>'s <c>keptLine.Words.Count &lt;= 1</c> guard exists
        /// to decline.
        /// </summary>
        private static (CssBox blockBox, CssRectWord prefix, CssRectWord suffix, CssLineBox keptLine,
            CssLineBox discardedLine, InlineBreakToken token) BuildSplitAsSoleWordOnItsLine(HyphenateLimitLast limit)
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue(
                    limit.ToString().ToLowerInvariant(), limit)
            };

            var original = new CssRectWord(blockBox, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(blockBox, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(blockBox, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;
            suffix.HyphenationPrefix = prefix;

            blockBox.Words.Add(prefix);
            blockBox.Words.Add(suffix);

            var keptLine = new CssLineBox(blockBox);
            keptLine.Words.Add(prefix);

            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(suffix);
            blockBox.LineBoxes.Remove(discardedLine);

            var token = new InlineBreakToken(blockBox, ResumeSlotIndex: 1, ResumePath: [], ResumeWordIndex: 7,
                CompletedLineCount: 3);

            return (blockBox, prefix, suffix, keptLine, discardedLine, token);
        }

        private static FragmentainerContext ColumnBreakContext() =>
            new(new HtmlContainerInt(new PdfSharpAdapter()), new CssBox(null, null), 0, (0, 100));

        [Fact]
        public void None_LeavesTheSplitAndTheTokenUntouched()
        {
            var (blockBox, _, _, _, discardedLine, token) = BuildSplitAtABreak(HyphenateLimitLast.None);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        [Fact]
        public void Always_UndoesTheSplitAtAPageBreak() => AssertForbiddingValueUndoesTheSplit(HyphenateLimitLast.Always);

        [Fact]
        public void Page_UndoesTheSplitAtAPageBreak() => AssertForbiddingValueUndoesTheSplit(HyphenateLimitLast.Page);

        [Fact]
        public void Spread_UndoesTheSplitAtAPageBreak() => AssertForbiddingValueUndoesTheSplit(HyphenateLimitLast.Spread);

        // `[Theory]`/`[InlineData]` can't carry `HyphenateLimitLast` itself - it's internal, and a public
        // theory method's parameter type must be at least as accessible as the method - hence three plain
        // facts sharing this helper rather than one parameterized test.
        private static void AssertForbiddingValueUndoesTheSplit(HyphenateLimitLast limit)
        {
            var (blockBox, prefix, _, keptLine, discardedLine, token) = BuildSplitAtABreak(limit);
            var original = prefix.PreSplitWord;

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(2, blockBox.Words.Count);
            Assert.Same(original, blockBox.Words[1]);
            Assert.DoesNotContain(prefix, keptLine.Words);
            Assert.True(original!.AwaitsTheNextFragmentainer);

            // One lower than the suffix's own ordinal: the owner list is one entry shorter after the
            // merge, so the restored whole word is counted where the prefix used to be.
            Assert.Equal(token.ResumeWordIndex - 1, result.ResumeWordIndex);

            // keptLine no longer ends in a hyphen, so the trailing run hyphenate-limit-lines was counting
            // is broken wherever it stood - the resumed pass must start back at 0, not inherit the
            // now-stale count (issue #724's ConsecutiveHyphenatedLines carry, interacting with this undo).
            Assert.Equal(0, result.ConsecutiveHyphenatedLines);
        }

        [Fact]
        public void Column_DoesNotForbidAtAPageBreak_LeavesTheSplitAlone()
        {
            var (blockBox, _, _, _, discardedLine, token) = BuildSplitAtABreak(HyphenateLimitLast.Column);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        [Fact]
        public void Column_ForbidsAtAColumnBreak_UndoesTheSplit()
        {
            var (blockBox, prefix, _, keptLine, discardedLine, token) = BuildSplitAtABreak(HyphenateLimitLast.Column);
            var original = prefix.PreSplitWord;

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(
                blockBox, discardedLine, ColumnBreakContext(), token, completedLines: 0);

            Assert.Equal(2, blockBox.Words.Count);
            Assert.Same(original, blockBox.Words[1]);
            Assert.DoesNotContain(prefix, keptLine.Words);
            Assert.Equal(token.ResumeWordIndex - 1, result.ResumeWordIndex);
        }

        [Fact]
        public void Page_DoesNotForbidAtAColumnBreak_LeavesTheSplitAlone()
        {
            var (blockBox, _, _, _, discardedLine, token) = BuildSplitAtABreak(HyphenateLimitLast.Page);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(
                blockBox, discardedLine, ColumnBreakContext(), token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        [Fact]
        public void Spread_TreatedLikePage_DoesNotForbidAtAColumnBreak()
        {
            var (blockBox, _, _, _, discardedLine, token) = BuildSplitAtABreak(HyphenateLimitLast.Spread);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(
                blockBox, discardedLine, ColumnBreakContext(), token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        // The livelock guard: when the prefix is the *only* word on its line, that line is already a
        // fresh, full-width one, so undoing the split cannot gain the word more room - it would just
        // hyphenate again on the next fragmentainer's identically-wide opening line. Declining leaves the
        // hyphen in place rather than deferring forever.
        [Fact]
        public void SoleWordOnItsLine_DeclinesTheUndoRegardlessOfValue()
        {
            var (blockBox, prefix, suffix, keptLine, discardedLine, token) =
                BuildSplitAsSoleWordOnItsLine(HyphenateLimitLast.Always);

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal([prefix, suffix], blockBox.Words);
            Assert.Contains(prefix, keptLine.Words);
            Assert.Equal(token, result);
        }

        [Fact]
        public void DiscardedLineFirstWordCarriesNoHyphenationPrefixLink_LeavesEverythingAlone()
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue("always", HyphenateLimitLast.Always)
            };
            var plain = new CssRectWord(blockBox, "plain", true, true);
            blockBox.Words.Add(plain);

            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(plain);
            blockBox.LineBoxes.Remove(discardedLine);

            var token = new InlineBreakToken(blockBox, 1, [], 5, 2);

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Same(plain, Assert.Single(blockBox.Words));
            Assert.Equal(token, result);
        }

        // The HyphenationPrefix link is set, but the word it points to isn't itself a genuine
        // TryHyphenateWord prefix (no PreSplitWord/HyphenationSuffix of its own) - a second structural
        // invariant defended independently of the first, since a future caller could set one link without
        // the other.
        [Fact]
        public void HyphenationPrefixLinkTargetIsNotAGenuinePrefix_Defensive_LeavesEverythingAlone()
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue("always", HyphenateLimitLast.Always)
            };
            var notReallyAPrefix = new CssRectWord(blockBox, "an-", true, false);
            var suffix = new CssRectWord(blockBox, "tidisestablishmentarianism", false, true)
            {
                HyphenationPrefix = notReallyAPrefix
            };

            blockBox.Words.Add(notReallyAPrefix);
            blockBox.Words.Add(suffix);

            var keptLine = new CssLineBox(blockBox);
            keptLine.Words.Add(notReallyAPrefix);

            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(suffix);
            blockBox.LineBoxes.Remove(discardedLine);

            var token = new InlineBreakToken(blockBox, 1, [], 7, 3);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        // The prefix came off keptLine.Words cleanly, but it is no longer present in its own owner box's
        // Words list - a third structural invariant, independent of the first two, defended the same
        // "restore and decline" way.
        [Fact]
        public void PrefixNotInOwnerWords_Defensive_RestoresItToTheKeptLine()
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue("always", HyphenateLimitLast.Always)
            };
            var filler = new CssRectWord(blockBox, "x", true, true);
            var original = new CssRectWord(blockBox, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(blockBox, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(blockBox, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;
            suffix.HyphenationPrefix = prefix;

            // prefix deliberately left out of blockBox.Words - only the kept line names it.
            blockBox.Words.Add(filler);
            blockBox.Words.Add(suffix);

            var keptLine = new CssLineBox(blockBox);
            keptLine.Words.Add(filler);
            keptLine.Words.Add(prefix);

            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(suffix);
            blockBox.LineBoxes.Remove(discardedLine);

            var token = new InlineBreakToken(blockBox, 1, [], 7, 3);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Contains(prefix, keptLine.Words);
            Assert.Equal(token, result);
        }

        // A structural invariant the method relies on (the prefix a discarded line's suffix points back
        // to is still on the line CreateLineBoxes just kept) has somehow already broken - covered as its
        // own line-box shape (two other words on keptLine, neither of them the prefix) so it is reached
        // instead of the sole-word-on-its-line guard above, which would otherwise intercept first.
        [Fact]
        public void PrefixNoLongerOnTheKeptLine_Defensive_LeavesEverythingAlone()
        {
            var (blockBox, prefix, _, keptLine, discardedLine, token) =
                BuildSplitAtABreak(HyphenateLimitLast.Always);
            keptLine.Words.Remove(prefix);
            keptLine.Words.Add(new CssRectWord(blockBox, "y", true, true));
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }

        // The pass-boundary guard: when the current pass's own seed line (resuming a split's suffix) is
        // itself what gets discarded before completing any line of its own, blockBox.LineBoxes[^1] names
        // a line an EARLIER pass already finalized - possibly already emitted into a frozen fragment. This
        // method has no HtmlContainerInt.InvalidateEmittedFragmentsFor machinery (unlike TakeEarlyBreak/
        // TryRewindForWidows, which call it before touching geometry that old), so it must decline rather
        // than mutate a line paint may never re-read. completedLines (CreateLineBoxes's own record of how
        // many lines existed when this pass started, per blockBox.DiscardLineBoxesFrom) is what lets the
        // method tell the two shapes apart from a single line-list snapshot.
        [Fact]
        public void KeptLineBelongsToAnEarlierPass_DeclinesTheUndo()
        {
            var (blockBox, prefix, suffix, keptLine, discardedLine, token) =
                BuildSplitAtABreak(HyphenateLimitLast.Always);
            var wordsBefore = blockBox.Words.ToList();

            // keptLine sits at index 0 of blockBox.LineBoxes (the only line built before discardedLine).
            // completedLines: 1 says this pass began with 1 line already finalized by an earlier pass -
            // i.e. keptLine predates the current pass, even though it is still the list's last entry.
            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(
                blockBox, discardedLine, null, token, completedLines: 1);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Contains(prefix, keptLine.Words);
            Assert.Equal(token, result);
        }

        // The same guard also covers the degenerate case DiscardedLineFirstWordCarriesNoHyphenationPrefixLink
        // and friends sidestep: no kept line exists at all (blockBox.LineBoxes is empty once discardedLine
        // is removed), which -1 &lt; completedLines (0) catches without a separate null check.
        [Fact]
        public void NoKeptLineExists_DeclinesTheUndo()
        {
            var blockBox = new CssBox(null, null)
            {
                HyphenateLimitLast = CssProperty<HyphenateLimitLast>.FromValue("always", HyphenateLimitLast.Always)
            };
            var original = new CssRectWord(blockBox, "antidisestablishmentarianism", true, true);
            var prefix = new CssRectWord(blockBox, "an-", true, false) { PreSplitWord = original };
            var suffix = new CssRectWord(blockBox, "tidisestablishmentarianism", false, true);
            prefix.HyphenationSuffix = suffix;
            suffix.HyphenationPrefix = prefix;

            blockBox.Words.Add(prefix);
            blockBox.Words.Add(suffix);

            // No kept line is ever created - discardedLine is the only line, and it is removed before the
            // call, mirroring CreateLineBoxes when the very first line of a fresh (non-resumed) pass is
            // itself the one that straddles.
            var discardedLine = new CssLineBox(blockBox);
            discardedLine.Words.Add(suffix);
            blockBox.LineBoxes.Remove(discardedLine);

            var token = new InlineBreakToken(blockBox, 1, [], 1, 0);
            var wordsBefore = blockBox.Words.ToList();

            var result = CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak(
                blockBox, discardedLine, null, token, completedLines: 0);

            Assert.Equal(wordsBefore, blockBox.Words);
            Assert.Equal(token, result);
        }
    }
}
