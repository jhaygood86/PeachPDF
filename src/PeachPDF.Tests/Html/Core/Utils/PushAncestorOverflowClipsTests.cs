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
            RenderUtils.PushAncestorOverflowClips(recording, DomUtils.GetBoxById(root, "hoisted")!, [movedAncestor]);

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
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, DomUtils.GetBoxById(root, "hoisted")!, [ancestor]);

            Assert.Equal(0, pushed);
            Assert.Empty(recording.Log.OfType<PushClipCall>());
        }

        // CSS Overflow 3 §3: an overflow clip only reaches descendants whose containing block chain
        // passes through the clipping box. An absolutely positioned box's containing block is its
        // nearest positioned ancestor, so a non-positioned overflow:hidden box in between does not clip it.
        private const string AbsposPastNonPositionedClip =
            "<div id='cb' style='position:relative;width:200pt;height:100pt'>" +
            "<div id='h' style='overflow:hidden;width:100pt;height:5pt'>" +
            "<div id='abs' style='position:absolute;top:0;left:0;width:80pt;height:40pt'></div></div></div>";

        [Fact]
        public async Task PushesNoClip_WhenHoistedAbsposBoxsContainingBlockIsAboveTheClippingAncestor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(AbsposPastNonPositionedClip));

            var ancestor = FragmentPaintHarness.FragmentOf(container, DomUtils.GetBoxById(root, "h")!);

            var recording = new TestRecordingGraphics();
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, DomUtils.GetBoxById(root, "abs")!, [ancestor]);

            Assert.Equal(0, pushed);
            Assert.Empty(recording.Log.OfType<PushClipCall>());
        }

        [Fact]
        public async Task AbsposFragment_HasNoOverflowClip_FromANonPositionedClippingAncestorBelowItsContainingBlock()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(AbsposPastNonPositionedClip));

            var absFragment = FragmentPaintHarness.FragmentOf(container, DomUtils.GetBoxById(root, "abs")!);

            Assert.Null(absFragment.OverflowClip);
        }

        [Fact]
        public async Task AbsposFragment_IsClipped_ByAPositionedClippingContainingBlock()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='h' style='position:relative;overflow:hidden;width:100pt;height:5pt'>" +
                "<div><div id='abs' style='position:absolute;top:0;left:0;width:80pt;height:40pt'></div></div></div>"));

            var hBox = DomUtils.GetBoxById(root, "h")!;
            var absBox = DomUtils.GetBoxById(root, "abs")!;
            var absFragment = FragmentPaintHarness.FragmentOf(container, absBox);

            Assert.NotNull(absFragment.OverflowClip);
            Assert.Equal(5, absFragment.OverflowClip!.Value.Height, 3);

            var recording = new TestRecordingGraphics();
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, absBox, [FragmentPaintHarness.FragmentOf(container, hBox)]);

            Assert.Equal(1, pushed);
        }

        [Fact]
        public async Task StaticChildOfAbsposBox_IsNotClipped_ByTheNonPositionedAncestorItsParentEscapes()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='position:relative;width:200pt;height:100pt'>" +
                "<div style='overflow:hidden;width:100pt;height:5pt'>" +
                "<div style='position:absolute;top:0;left:0;width:80pt;height:40pt'>" +
                "<div id='inner' style='height:30pt'></div></div></div></div>"));

            var innerFragment = FragmentPaintHarness.FragmentOf(container, DomUtils.GetBoxById(root, "inner")!);

            Assert.Null(innerFragment.OverflowClip);
        }

        // css-position-3 §2.1: a fixed box's containing block is the viewport (the page), unless an
        // ancestor with transform/perspective/filter/backdrop-filter forms it instead.
        [Fact]
        public async Task FixedFragment_HasNoOverflowClip_WithoutATransformedAncestor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='h' style='position:relative;overflow:hidden;width:100pt;height:5pt'>" +
                "<div id='fixed' style='position:fixed;top:0;left:0;width:80pt;height:40pt'></div></div>"));

            var fixedBox = DomUtils.GetBoxById(root, "fixed")!;
            var fixedFragment = FragmentPaintHarness.FragmentOf(container, fixedBox);

            Assert.Null(fixedFragment.OverflowClip);
            Assert.False(DomUtils.IsOnClippingChainOf(fixedBox, DomUtils.GetBoxById(root, "h")!));
        }

        [Theory]
        [InlineData("transform:translate(1pt,0)")]
        [InlineData("filter:opacity(0.9)")]
        [InlineData("perspective:100pt")]
        public async Task OutOfFlowFragment_IsClipped_ByANonPositionedClippingAncestorThatFormsItsContainingBlock(string effect)
        {
            foreach (var position in new[] { "absolute", "fixed" })
            {
                var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                    "<div style='position:relative;width:200pt;height:100pt'>" +
                    $"<div id='h' style='overflow:hidden;{effect};width:100pt;height:5pt'>" +
                    $"<div id='oof' style='position:{position};top:0;left:0;width:80pt;height:40pt'></div></div></div>"));

                var oofBox = DomUtils.GetBoxById(root, "oof")!;
                var fragment = FragmentPaintHarness.FragmentOf(container, oofBox);

                Assert.NotNull(fragment.OverflowClip);
                Assert.Equal(5, fragment.OverflowClip!.Value.Height, 3);
                Assert.True(DomUtils.IsOnClippingChainOf(oofBox, DomUtils.GetBoxById(root, "h")!));
            }
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

        [Fact]
        public async Task HoistedBoxInARepeatingTableHeader_IsStillClipped_ByAnOverflowAncestorOfTheTable()
        {
            // A repeating thead is detached from its table (ParentBox null, DomParentBox the table), so
            // the ordinary containing-block chain of anything inside it stops at the thead - the walk
            // must continue at the table, whose overflow:hidden ancestor still clips the header.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='h' style='overflow:hidden;width:50pt;height:10pt'><table>" +
                "<thead><tr><td><div id='hoisted' style='position:relative;z-index:0;height:40pt'>X</div></td></tr></thead>" +
                "<tbody><tr><td>body</td></tr></tbody></table></div>"));

            var hBox = DomUtils.GetBoxById(root, "h")!;

            // GetBoxById walks CssBox.Boxes, which doesn't reach into the detached thead.
            var participant = TryFind(FragmentPaintHarness.FragmentOf(container, root), "hoisted")
                              ?? throw new Xunit.Sdk.XunitException("hoisted participant not found");
            var hoistedBox = participant.Box;

            Assert.True(DomUtils.IsOnClippingChainOf(hoistedBox, hBox));
            Assert.Contains(participant.ClipAncestors, a => a.Box == hBox);

            var recording = new TestRecordingGraphics();
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, hoistedBox, participant.ClipAncestors);

            Assert.True(pushed >= 1);
            Assert.Contains(recording.Log.OfType<PushClipCall>(), p => p.Rect.Height <= 10.5);
        }

        [Fact]
        public async Task HoistedBoxInAnOverflowHiddenCaption_IsStillClippedByTheCaption()
        {
            // CssBox.ContainingBlock skips a table-caption, so an in-flow step must walk every ancestor,
            // not the containing-block chain, or the caption's own clip is dropped.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><caption id='cap' style='overflow:hidden;height:5pt'>" +
                "<div id='rel' style='position:relative;z-index:1;height:40pt'>X</div></caption>" +
                "<tr><td>body</td></tr></table>"));

            var capBox = DomUtils.GetBoxById(root, "cap")!;
            var participant = TryFind(FragmentPaintHarness.FragmentOf(container, root), "rel")
                              ?? throw new Xunit.Sdk.XunitException("hoisted participant not found");

            Assert.True(DomUtils.IsOnClippingChainOf(participant.Box, capBox));

            var recording = new TestRecordingGraphics();
            var pushed = RenderUtils.PushAncestorOverflowClips(recording, participant.Box, participant.ClipAncestors);

            Assert.True(pushed >= 1);
        }

        // overflow does not apply to a non-atomic inline or a table row, so a positioned one of those
        // that is an abspos box's containing block must not clip it.
        [Theory]
        [InlineData("<p><span id='cb' style='position:relative;overflow:hidden'>text " +
                    "<span id='abs' style='position:absolute;display:block;top:0;left:0;width:80pt;height:40pt'></span></span></p>")]
        [InlineData("<table><tr id='cb' style='position:relative;overflow:hidden'><td>" +
                    "<div id='abs' style='position:absolute;top:0;left:0;width:80pt;height:40pt'></div></td></tr></table>")]
        public async Task AbsposFragment_IsNotClipped_ByAContainingBlockOverflowDoesNotApplyTo(string html)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));

            var absBox = DomUtils.GetBoxById(root, "abs")!;
            var cbBox = DomUtils.GetBoxById(root, "cb")!;

            Assert.Same(cbBox, DomUtils.ClippingContainingBlockOf(absBox));
            Assert.Null(FragmentPaintHarness.FragmentOf(container, absBox).OverflowClip);
            Assert.False(DomUtils.ClipsItsOverflow(cbBox));
        }

        private static StackingOrder.StackingParticipant? TryFind(BoxFragment fragment, string id)
        {
            foreach (var participant in StackingOrder.Flatten(fragment))
                if (participant.Box.HtmlTag?.TryGetAttribute("id") == id) return participant;

            foreach (var child in fragment.Children)
                if (TryFind(child, id) is { } found) return found;

            return null;
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
