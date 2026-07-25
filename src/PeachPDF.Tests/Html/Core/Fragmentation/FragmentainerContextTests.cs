using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The fragmentainer a layout pass targets. These pin the two things the rest of the fragmentation
    /// work stands on: that the context is a plain cursor over the existing page grid (so no second,
    /// drifting notion of where a page starts is introduced), and that
    /// <see cref="FragmentainerContext.ResumeContentTop"/> is the band top itself.
    /// </summary>
    public class FragmentainerContextTests
    {
        private const double BandHeight = 800;
        private const double MarginTop = 70;

        private static HtmlContainerInt CreateContainer(double bandHeight = BandHeight) =>
            new(new PdfSharpAdapter())
            {
                PageSize = new RSize(500, bandHeight),
                MarginTop = MarginTop,
            };

        private static FragmentainerContext CreateContext(HtmlContainerInt container, int slot = 0) =>
            new(container, new CssBox(null, null), slot, generation: 1, incomingToken: null);

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        public void BandGeometry_IsTheExistingPageGrid_NotASecondSourceOfTruth(int slot)
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot);

            Assert.Equal(container.PageTopOf(slot), context.BandTop, 9);
            Assert.Equal(container.PageBottomOf(slot), context.BandBottom, 9);
            Assert.Equal(container.PageBandHeightOf(slot), context.BandHeight, 9);
        }

        [Fact]
        public void ResumeContentTop_IsTheBandTopItself_NotOnePastIt()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 3);

            // The retired CssRect.BreakPage relocated to NextPageTopOf(Top) + 1, leaving every
            // continuation line one unit below its fragmentainer's content edge. css-break-3 §2 puts a
            // fragment at the content edge, so the nudge is gone.
            Assert.Equal(container.PageTopOf(3), context.ResumeContentTop, 9);
        }

        [Fact]
        public void ResumeContentTop_LandsExactlyOnABoundary_AndTheGridStillAttributesItForward()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 2);

            // Dropping the +1 puts resumed content exactly on a band top, which is the case the nudge
            // used to hide: the grid must attribute that value to the band it starts, not the one before.
            Assert.Equal(2, container.PageIndexOf(context.ResumeContentTop));
            Assert.Equal(2, container.PageIndexOf(context.ResumeContentTop + HtmlContainerInt.PageBoundaryEpsilon));
        }

        [Fact]
        public void IsFragmenting_IsFalse_WhenThePageHeightIsTheMeasurementSentinel()
        {
            var container = CreateContainer(bandHeight: double.MaxValue);

            Assert.False(CreateContext(container).IsFragmenting);
        }

        [Fact]
        public void IsFragmenting_IsFalse_ForAZeroHeightPage()
        {
            var container = CreateContainer(bandHeight: 0);

            Assert.False(CreateContext(container).IsFragmenting);
        }

        [Fact]
        public void IsFragmenting_FollowsSuppressWordPageBreaks()
        {
            var container = CreateContainer();
            var context = CreateContext(container);

            Assert.True(context.IsFragmenting);

            container.SuppressWordPageBreaks = true;
            Assert.False(context.IsFragmenting);

            container.SuppressWordPageBreaks = false;
            Assert.True(context.IsFragmenting);
        }

        [Fact]
        public void EnterMonolithic_SuppressesFragmenting_AndRestoresOnExit()
        {
            var context = CreateContext(CreateContainer());

            Assert.True(context.IsFragmenting);

            var outer = context.EnterMonolithic();
            Assert.False(context.IsFragmenting);

            // Nested monolithic content composes: the inner scope restores to "still monolithic".
            var inner = context.EnterMonolithic();
            Assert.False(context.IsFragmenting);
            context.ExitMonolithic(inner);
            Assert.False(context.IsFragmenting);

            context.ExitMonolithic(outer);
            Assert.True(context.IsFragmenting);
        }

        [Fact]
        public void RecordBreak_AndNoteProgress_AreTheDriversTwoSignals()
        {
            var container = CreateContainer();
            var root = new CssBox(null, null);
            var context = new FragmentainerContext(container, root, slotIndex: 0, generation: 1, incomingToken: null);

            Assert.Null(context.OutgoingToken);
            Assert.False(context.MadeProgress);

            context.NoteProgress();
            Assert.True(context.MadeProgress);

            var token = new BlockBreakToken(root, ResumeSlotIndex: 1, ResumeChildIndex: 2, ChildToken: null, IsBreakBefore: true, ResumeTopOverride: null);
            context.RecordBreak(token);
            Assert.Same(token, context.OutgoingToken);
        }

        [Fact]
        public void ContainerIsFragmenting_IsFalse_OutsideALayoutPass()
        {
            var container = CreateContainer();

            // No LayoutDocument pass is running, so there is no fragmentainer to break against.
            Assert.Null(container.CurrentFragmentainer);
            Assert.False(container.IsFragmenting);
        }
    }
}
