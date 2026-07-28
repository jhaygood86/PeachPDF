using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The resumption record. A token says <i>where</i> layout stopped and nothing about geometry — the
    /// box tree still holds the coordinates — so these pin its shape rather than any placement.
    /// </summary>
    public class BreakTokenTests
    {
        [Fact]
        public void BreakBefore_CarriesNoChildToken()
        {
            var box = new CssBox(null, null);

            var token = new BlockBreakToken(box, ResumeSlotIndex: 1, ResumeChildIndex: 3, ChildToken: null, IsBreakBefore: true, ResumeTopOverride: null);

            // A break *before* a child means the child was never entered, so there is nothing inside it
            // to resume - this is what makes "no fragment in the earlier fragmentainer" structural.
            Assert.True(token.IsBreakBefore);
            Assert.Null(token.ChildToken);
            Assert.Equal(3, token.ResumeChildIndex);
            Assert.Same(box, token.Box);
        }

        [Fact]
        public void BreakInside_CarriesTheChildsOwnToken()
        {
            var parent = new CssBox(null, null);
            var child = new CssBox(null, null);

            var childToken = new BlockBreakToken(child, ResumeSlotIndex: 1, ResumeChildIndex: 1, ChildToken: null, IsBreakBefore: true, ResumeTopOverride: null);
            var token = new BlockBreakToken(parent, ResumeSlotIndex: 1, ResumeChildIndex: 0, ChildToken: childToken, IsBreakBefore: false, ResumeTopOverride: null);

            Assert.False(token.IsBreakBefore);
            Assert.Same(childToken, token.ChildToken);
        }

        [Fact]
        public void Chain_NestsOneLinkPerAncestorOnThePathToTheContextRoot()
        {
            var root = new CssBox(null, null);
            var middle = new CssBox(null, null);
            var leaf = new CssBox(null, null);

            var leafToken = new InlineBreakToken(leaf, ResumeSlotIndex: 1, ResumePath: [0, 2], ResumeWordIndex: 5, CompletedLineCount: 4);
            var middleToken = new BlockBreakToken(middle, ResumeSlotIndex: 1, ResumeChildIndex: 1, ChildToken: leafToken, IsBreakBefore: false, ResumeTopOverride: null);
            var rootToken = new BlockBreakToken(root, ResumeSlotIndex: 1, ResumeChildIndex: 2, ChildToken: middleToken, IsBreakBefore: false, ResumeTopOverride: null);

            // Walking the chain down from the root is exactly how a resumed pass re-enters each ancestor
            // mid-flight while leaving boxes off the path alone.
            var boxes = new List<CssBox>();
            for (BreakToken? t = rootToken; t is not null; t = t is BlockBreakToken b ? b.ChildToken : null)
                boxes.Add(t.Box);

            Assert.Equal([root, middle, leaf], boxes);
        }

        [Fact]
        public void ResumeTopOverride_IsCarriedForTheAdjustedTargetPathsThatComputeIt()
        {
            var box = new CssBox(null, null);

            var token = new BlockBreakToken(box, ResumeSlotIndex: 1, ResumeChildIndex: 0, ChildToken: null, IsBreakBefore: true, ResumeTopOverride: 1234.5);

            // Margin truncation and the keep-with-next pull have already worked out where the box goes;
            // the resumed pass must use that value rather than re-deriving it.
            Assert.Equal(1234.5, token.ResumeTopOverride);
        }

        [Fact]
        public void InlineToken_RecordsThePathTheFlowWalkMustDescendAgain()
        {
            var box = new CssBox(null, null);

            var token = new InlineBreakToken(box, ResumeSlotIndex: 1, ResumePath: [1, 0, 4], ResumeWordIndex: 2, CompletedLineCount: 7);

            Assert.Equal([1, 0, 4], token.ResumePath);
            Assert.Equal(2, token.ResumeWordIndex);
            Assert.Equal(7, token.CompletedLineCount);
        }

        /// <summary>
        /// A token's equality is what stops a run that gets nowhere, so a record carrying a collection has
        /// to compare it by contents. The compiler's own equality compares an
        /// <c>IReadOnlyList&lt;int&gt;</c> by reference, and two passes that stopped at the same word build
        /// their own list each.
        /// </summary>
        [Fact]
        public void InlineToken_WithTheSamePathBuiltTwice_ComparesEqual()
        {
            var box = new CssBox(null, null);

            // Deliberately not a collection expression on both sides: the compiler serves an empty one
            // from a cached singleton, which is exactly the accident that hid this.
            var first = new InlineBreakToken(box, 1, new List<int> { 1, 0, 4 }, ResumeWordIndex: 2, CompletedLineCount: 7);
            var second = new InlineBreakToken(box, 1, new List<int> { 1, 0, 4 }, ResumeWordIndex: 2, CompletedLineCount: 7);

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void InlineToken_WithADifferentPath_ComparesUnequal()
        {
            var box = new CssBox(null, null);

            var first = new InlineBreakToken(box, 1, new List<int> { 1, 0, 4 }, ResumeWordIndex: 2, CompletedLineCount: 7);
            var second = new InlineBreakToken(box, 1, new List<int> { 1, 0, 5 }, ResumeWordIndex: 2, CompletedLineCount: 7);

            Assert.NotEqual(first, second);
        }

        /// <summary>
        /// And the rest of its own fields still separate two records — a hand-written <c>Equals</c> that
        /// forgets one is how a resumed flow silently compares equal to a different resumption point.
        /// </summary>
        [Theory]
        [InlineData(2, 7, 0, 3, 7, 0)]
        [InlineData(2, 7, 0, 2, 8, 0)]
        [InlineData(2, 7, 0, 2, 7, 1)]
        public void InlineToken_DiffersInAnyOfItsOwnFields(
            int word, int completed, int kept, int otherWord, int otherCompleted, int otherKept)
        {
            var box = new CssBox(null, null);

            Assert.NotEqual(
                new InlineBreakToken(box, 1, new List<int> { 0 }, word, completed, kept),
                new InlineBreakToken(box, 1, new List<int> { 0 }, otherWord, otherCompleted, otherKept));
        }
    }
}
