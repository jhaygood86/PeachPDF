using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.TestRecordingGraphics;

namespace PeachPDF.Tests.Html.Core.Utils
{
    using DomUtils = PeachPDF.Html.Core.Utils.DomUtils;

    /// <summary>
    /// <see cref="RenderUtils.PushAncestorOverflowClips"/> re-applies an ancestor's <c>overflow: hidden</c>
    /// clip for a stacking-hoisted participant, which paints outside that ancestor's own nested paint
    /// call. The ancestor can be shown at more than one place in the document (a hoisted participant
    /// inside a repeated table header), so its live <see cref="Dom.CssBox"/> geometry only ever reflects
    /// whichever place last positioned it — the clip has to come from the ancestor's own fragment for the
    /// page being painted instead (#345).
    /// </summary>
    public class PushAncestorOverflowClipsTests
    {
        [Fact]
        public async Task PushesClip_FromAncestorsOwnFragmentRect_NotFromItsLiveBoxBounds()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='h' style='overflow:hidden;width:50pt;height:20pt'>" +
                "<div id='hoisted' style='position:relative;z-index:0'><span>TEXT</span></div></div>"));

            var hBox = DomUtils.GetBoxById(root, "h")!;
            var liveBounds = hBox.Bounds;

            // Simulate the repeated-header shape: an ancestor whose fragment on the page being painted
            // sits somewhere quite different from wherever its live box happens to be positioned right
            // now (e.g. a shared source subtree a later page's layout pass has since moved on).
            var movedRect = hBox.Bounds;
            movedRect.Offset(0, 1000);
            var movedAncestor = FragmentPaintHarness.FragmentOf(container, hBox) with { Rect = movedRect };

            var recording = new TestRecordingGraphics();
            RenderUtils.PushAncestorOverflowClips(recording, [movedAncestor]);

            var pushed = Assert.Single(recording.Log.OfType<PushClipCall>());
            var expected = RenderUtils.PaddingEdgeOf(hBox, movedRect);

            Assert.Equal(expected.Top, pushed.Rect.Top, 3);
            Assert.Equal(expected.Left, pushed.Rect.Left, 3);

            // Sanity: the fragment we passed really does disagree with the live box, so this actually
            // exercises the fragment-vs-live-box distinction rather than the two happening to coincide.
            Assert.NotEqual(liveBounds.Top, movedRect.Top);
        }

        [Fact]
        public async Task PushesNoClip_WhenAncestorIsNotOverflowHidden()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='plain' style='width:50pt;height:20pt'>" +
                "<div id='hoisted' style='position:relative;z-index:0'><span>TEXT</span></div></div>"));

            var plainBox = DomUtils.GetBoxById(root, "plain")!;
            var ancestor = FragmentPaintHarness.FragmentOf(container, plainBox);

            var recording = new TestRecordingGraphics();
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, [ancestor]);

            Assert.Equal(0, pushed);
            Assert.Empty(recording.Log.OfType<PushClipCall>());
        }

        [Fact]
        public async Task Flatten_SameAncestorBoxShownAtDifferentFragmentRects_CapturesEachTreesOwnInstance()
        {
            // The same live CssBox standing in for two different pages' own fragment of it — exactly
            // the shape a repeated table header's shared source subtree produces. StackingOrder's own
            // discovery walk (SearchForHoistableDescendants), not just RenderUtils in isolation, must
            // record each tree's own ancestor fragment rather than always the same one.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='h' style='overflow:hidden;width:50pt;height:20pt'>" +
                "<div id='hoisted' style='position:relative;z-index:0'><span>TEXT</span></div></div>"));

            var hBox = DomUtils.GetBoxById(root, "h")!;
            var rootFragmentA = FragmentPaintHarness.FragmentOf(container, root);

            var movedRect = FragmentPaintHarness.FragmentOf(container, hBox).Rect;
            movedRect.Offset(0, 1000);
            var rootFragmentB = WithReplacedRect(rootFragmentA, hBox, movedRect);

            var hoistedBox = DomUtils.GetBoxById(root, "hoisted")!;
            var participantA = StackingOrder.Flatten(rootFragmentA).Single(p => p.Box == hoistedBox);
            var participantB = StackingOrder.Flatten(rootFragmentB).Single(p => p.Box == hoistedBox);

            var ancestorA = participantA.ClipAncestors.Single(a => a.Box == hBox);
            var ancestorB = participantB.ClipAncestors.Single(a => a.Box == hBox);

            Assert.NotEqual(ancestorA.Rect.Top, ancestorB.Rect.Top);
            Assert.Equal(movedRect.Top, ancestorB.Rect.Top, 3);
        }

        private static BoxFragment WithReplacedRect(BoxFragment fragment, CssBox target, RRect newRect) =>
            ReferenceEquals(fragment.Box, target)
                ? fragment with { Rect = newRect }
                : fragment with
                {
                    Children = fragment.Children
                        .Select(child => WithReplacedRect(child, target, newRect))
                        .ToList()
                };
    }
}
