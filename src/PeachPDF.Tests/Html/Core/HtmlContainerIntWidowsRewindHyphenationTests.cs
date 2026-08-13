using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// <c>HtmlContainerInt.TryRebuildForBudget</c> rebuilds the <see cref="InlineBreakToken"/> a widows
    /// rewind (<c>HtmlContainerInt.TryRewindForWidows</c>) gives back some of a box's own kept lines to -
    /// css-break-3 §5.4's "give back lines until enough remain after the break" correction. The token it
    /// started from carries <see cref="InlineBreakToken.ConsecutiveHyphenatedLines"/> (issue #724) as of
    /// wherever the <i>original</i> pass actually stopped, which is stale the moment the rewind changes
    /// which line is last-kept: giving back a hyphenated line can leave the new last-kept line's own
    /// trailing run shorter than the stale count said, and giving back a non-hyphenated one can leave it
    /// longer. Exercised directly against hand-built line boxes carrying the same
    /// <see cref="CssRectWord.PreSplitWord"/> linkage <c>TryHyphenateWord</c> sets, mirroring
    /// <see cref="Dom.HyphenateLimitLastEnforcementTests"/>, rather than depending on exact page geometry
    /// to land a widows rewind (this repo's own <c>hyphenate-limit-last</c> fix notes call the geometry for
    /// that combination unusually sensitive to tune).
    /// </summary>
    public class HtmlContainerIntWidowsRewindHyphenationTests
    {
        private static CssRectWord PlainWord(CssBox box, string text) => new(box, text, true, true);

        private static CssRectWord HyphenatedLineEndWord(CssBox box, string prefixText)
        {
            var original = new CssRectWord(box, prefixText + "riginal", true, true);
            return new CssRectWord(box, prefixText, true, false) { PreSplitWord = original };
        }

        [Fact]
        public void GivingBackANonHyphenatedLine_RaisesTheRecomputedCount()
        {
            var box = new CssBox(null, null);

            // Lines 0-2 all end in a hyphenation split; line 3 does not. Laid out in full, the pass ends
            // with ConsecutiveHyphenatedLines back at 0 (line 3 broke the run) - the stale value an
            // un-rewound token would carry forward.
            new CssLineBox(box) { StartOrdinal = 0 }.Words.Add(HyphenatedLineEndWord(box, "a-"));
            new CssLineBox(box) { StartOrdinal = 1 }.Words.Add(HyphenatedLineEndWord(box, "b-"));
            new CssLineBox(box) { StartOrdinal = 2 }.Words.Add(HyphenatedLineEndWord(box, "c-"));
            new CssLineBox(box) { StartOrdinal = 3 }.Words.Add(PlainWord(box, "d"));

            BreakToken original = new InlineBreakToken(box, ResumeSlotIndex: 1, ResumePath: [],
                ResumeWordIndex: 4, CompletedLineCount: 4, ConsecutiveHyphenatedLines: 0);

            // The rewind gives back line 3, keeping lines 0-2 - all three of which end in a hyphen, so the
            // trailing run at the new last-kept line (line 2) is 3, not the stale 0 the original token
            // carried.
            Assert.True(HtmlContainerInt.TryRebuildForBudget(original, box, budget: 3, out var rebuilt));
            var inline = Assert.IsType<InlineBreakToken>(rebuilt);

            Assert.Equal(3, inline.ConsecutiveHyphenatedLines);
        }

        [Fact]
        public void GivingBackAHyphenatedLine_LowersTheRecomputedCount()
        {
            var box = new CssBox(null, null);

            // Line 0 does not end in a hyphen; lines 1-3 all do. Laid out in full, the pass ends with
            // ConsecutiveHyphenatedLines at 3 - the stale value an un-rewound token would carry forward.
            new CssLineBox(box) { StartOrdinal = 0 }.Words.Add(PlainWord(box, "a"));
            new CssLineBox(box) { StartOrdinal = 1 }.Words.Add(HyphenatedLineEndWord(box, "b-"));
            new CssLineBox(box) { StartOrdinal = 2 }.Words.Add(HyphenatedLineEndWord(box, "c-"));
            new CssLineBox(box) { StartOrdinal = 3 }.Words.Add(HyphenatedLineEndWord(box, "d-"));

            BreakToken original = new InlineBreakToken(box, ResumeSlotIndex: 1, ResumePath: [],
                ResumeWordIndex: 4, CompletedLineCount: 4, ConsecutiveHyphenatedLines: 3);

            // The rewind gives back line 3, keeping lines 0-2 - line 2 and line 1 both still end in a
            // hyphen, but line 0 doesn't, so the trailing run at the new last-kept line (line 2) is 2, not
            // the stale 3 the original token carried.
            Assert.True(HtmlContainerInt.TryRebuildForBudget(original, box, budget: 3, out var rebuilt));
            var inline = Assert.IsType<InlineBreakToken>(rebuilt);

            Assert.Equal(2, inline.ConsecutiveHyphenatedLines);
        }

        [Fact]
        public void NoLinesKept_RecomputesToZero()
        {
            var box = new CssBox(null, null);

            new CssLineBox(box) { StartOrdinal = 0 }.Words.Add(HyphenatedLineEndWord(box, "a-"));

            BreakToken original = new InlineBreakToken(box, ResumeSlotIndex: 1, ResumePath: [],
                ResumeWordIndex: 1, CompletedLineCount: 1, ConsecutiveHyphenatedLines: 1);

            // budget: 0 gives back every line this box had - nothing is kept, so there is no trailing run
            // to count regardless of how hyphenated the given-back line was.
            Assert.True(HtmlContainerInt.TryRebuildForBudget(original, box, budget: 0, out var rebuilt));
            var inline = Assert.IsType<InlineBreakToken>(rebuilt);

            Assert.Equal(0, inline.ConsecutiveHyphenatedLines);
        }
    }
}
