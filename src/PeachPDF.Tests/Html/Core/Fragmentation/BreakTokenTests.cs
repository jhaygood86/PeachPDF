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
    }
}
