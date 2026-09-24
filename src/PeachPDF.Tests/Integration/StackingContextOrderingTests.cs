using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the actual paint ORDER stacking-context content ends up in, not just that painting
    /// completes without throwing or that the page has "some" content - per this repo's painting-test
    /// convention (see <c>BorderStylePaintIntegrationTests</c>), and specifically to close the gap left
    /// by <c>StackingContextPaintRegressionTests</c>'s existing <c>PageHasContent</c>-only assertions,
    /// which did not actually verify that z-indexed/opacity/transform content painted at all, let alone
    /// in the right order - see <c>StackingOrder.Flatten</c>/<c>DomUtils.IsStackingContextBox</c> for
    /// the algorithm these tests exercise.
    /// </summary>
    public class StackingContextOrderingTests
    {
        [Fact]
        public async Task TwoZIndexedSiblings_BothPaintInZIndexOrder()
        {
            // Regression: the old algorithm dropped any box that established its own stacking context
            // (position:relative;z-index here) entirely - it and its subtree never painted at all.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='back' style='position:relative;z-index:1;width:50px;height:50px;background:rgb(255,0,0);'></div>" +
                "<div id='front' style='position:relative;z-index:2;width:50px;height:50px;background:rgb(0,0,255);'></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var backIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(255, 0, 0));
            var frontIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 0, 255));

            Assert.True(backIndex >= 0, "z-index:1 sibling never painted");
            Assert.True(frontIndex >= 0, "z-index:2 sibling never painted");
            Assert.True(backIndex < frontIndex, "lower z-index sibling must paint before the higher one");
        }

        [Fact]
        public async Task DeeplyNestedZIndexedBox_PaintsAfterNegativeZIndexSibling_NotDuringNaturalTraversal()
        {
            // Regression: an out-of-flow stacking-context descendant nested a few plain <div>s deep used
            // to be discovered via the ordinary parent-to-child Paint() cascade before its true ancestor
            // stacking context ever reached its own z-index layer - painting as if z-index had no effect.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='neg' style='position:relative;z-index:-1;width:50px;height:50px;background:rgb(0,128,0);'></div>" +
                "<div><div><div>" +
                "<div id='deep' style='position:absolute;z-index:5;top:0;left:0;width:50px;height:50px;background:rgb(255,165,0);'></div>" +
                "</div></div></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var negIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 128, 0));
            var deepIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(255, 165, 0));

            Assert.True(negIndex >= 0, "z-index:-1 sibling never painted");
            Assert.True(deepIndex >= 0, "deeply-nested z-index:5 box never painted");
            Assert.True(negIndex < deepIndex,
                "the negative-z-index sibling must paint before the deeply-nested positive-z-index box");
        }

        [Fact]
        public async Task ZIndexedBoxNestedInPlainAbsoluteWrapper_EscapesToCompeteAtTrueAncestorLevel()
        {
            // Full-fidelity case: position:relative;z-index nested inside a plain position:absolute
            // wrapper (no z-index of its own, so the wrapper does NOT establish a stacking context)
            // must still compete for z-order at the wrapper's true enclosing stacking context, not be
            // trapped painting only whenever the wrapper itself happens to paint. The wrapper is
            // position:absolute (bucket 3) so, if the nested box were trapped inside it, it would only
            // ever paint after the position:relative sibling below (bucket 1) - regardless of its own
            // z-index. Escaping to the true root-level z-index:-1 layer must paint it FIRST instead.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='sibling' style='position:relative;width:50px;height:50px;background:rgb(128,0,128);'></div>" +
                "<div style='position:absolute;top:0;left:0;width:100px;height:100px;'>" +
                "<span id='nested' style='position:relative;z-index:-1;width:50px;height:50px;background:rgb(139,69,19);'>x</span>" +
                "</div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var siblingIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(128, 0, 128));
            var nestedIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(139, 69, 19));

            Assert.True(siblingIndex >= 0, "sibling never painted");
            Assert.True(nestedIndex >= 0, "nested z-index:-1 box never painted");
            Assert.True(nestedIndex < siblingIndex,
                "the nested z-index:-1 box must escape the plain absolute wrapper to paint before the sibling");
        }

        [Fact]
        public async Task OpacityBox_PaintsBeforeLaterPositiveZIndexSibling_AfterEarlierNegativeZIndexSibling()
        {
            // Opacity < 1 now establishes a stacking context (z-index:auto -> stack level 0), so an
            // opacity box with no explicit z-index must still paint between a negative- and a positive-
            // z-index sibling, exactly like any other stack-level-0 content.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='neg' style='position:relative;z-index:-1;width:50px;height:50px;background:rgb(0,128,0);'></div>" +
                "<div id='op' style='opacity:0.5;width:50px;height:50px;background:rgb(100,100,100);'></div>" +
                "<div id='pos' style='position:relative;z-index:1;width:50px;height:50px;background:rgb(0,0,255);'></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var negIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 128, 0));
            var opIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(100, 100, 100));
            var posIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 0, 255));

            Assert.True(negIndex >= 0 && opIndex >= 0 && posIndex >= 0, "one of the three siblings never painted");
            Assert.True(negIndex < opIndex, "the opacity box must paint after the negative-z-index sibling");
            Assert.True(opIndex < posIndex, "the opacity box must paint before the positive-z-index sibling");
        }

        [Fact]
        public async Task TransformedBox_PaintsBeforeLaterPositiveZIndexSibling_AfterEarlierNegativeZIndexSibling()
        {
            // Same shape as the opacity case, but for the other newly-recognized trigger: a non-
            // identity transform.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='neg' style='position:relative;z-index:-1;width:50px;height:50px;background:rgb(0,128,0);'></div>" +
                "<div id='tr' style='transform:rotate(10deg);width:50px;height:50px;background:rgb(100,100,100);'></div>" +
                "<div id='pos' style='position:relative;z-index:1;width:50px;height:50px;background:rgb(0,0,255);'></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var negIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 128, 0));
            var trIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(100, 100, 100));
            var posIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(0, 0, 255));

            Assert.True(negIndex >= 0 && trIndex >= 0 && posIndex >= 0, "one of the three siblings never painted");
            Assert.True(negIndex < trIndex, "the transformed box must paint after the negative-z-index sibling");
            Assert.True(trIndex < posIndex, "the transformed box must paint before the positive-z-index sibling");
        }

        [Fact]
        public async Task PositionedAutoSiblings_PaintInTreeOrderAcrossPositionSchemes()
        {
            // CSS 2.1 Appendix E step 8 puts every positioned descendant with stack level 0 in one
            // tree-order bucket. Separate relative/absolute/fixed paint passes reverse that order and
            // let an earlier fixed box cover a later absolute sibling (the Acid2 eye-row failure).
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='fixed' style='position:fixed;top:0;left:0;width:20px;height:20px;background:rgb(10,20,30);'></div>" +
                "<div id='absolute' style='position:absolute;top:0;left:0;width:20px;height:20px;background:rgb(40,50,60);'></div>" +
                "<div id='sticky' style='position:sticky;top:0;width:20px;height:20px;background:rgb(55,65,75);'></div>" +
                "<div id='relative' style='position:relative;width:20px;height:20px;background:rgb(70,80,90);'></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var fixedIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(10, 20, 30));
            var absoluteIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(40, 50, 60));
            var stickyIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(55, 65, 75));
            var relativeIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(70, 80, 90));

            Assert.True(fixedIndex >= 0 && absoluteIndex >= 0 && stickyIndex >= 0 && relativeIndex >= 0,
                "one of the positioned siblings never painted");
            Assert.True(fixedIndex < absoluteIndex && absoluteIndex < stickyIndex && stickyIndex < relativeIndex,
                $"stack-level-0 positioned siblings must paint in tree order, regardless of positioning scheme " +
                $"(fixed={fixedIndex}, absolute={absoluteIndex}, sticky={stickyIndex}, relative={relativeIndex})");
        }

        [Fact]
        public async Task HoistedBox_PastTwoNestedOverflowHiddenAncestors_GetsBothAncestorsClipsApplied()
        {
            // Regression: RenderUtils.ClipGraphicsByOverflow, called naturally inside a box's own
            // PaintImpCore, only ever finds and pushes the NEAREST overflow:hidden ancestor along its
            // own containing-block chain, then stops - so a box hoisted past TWO nested overflow:hidden
            // wrappers (w1 outside, w2 inside) only ever got w2's clip applied via its own natural
            // paint call; w1's clip normally comes "for free" from w1's own still-active Paint() call
            // during ordinary nested painting, which never happens for hoisted content. A single
            // intermediate overflow:hidden wrapper is already handled correctly without this fix - this
            // scenario specifically needs two nested ones to exercise the gap. w1 (100x80) is smaller
            // than w2 (200x200) in both dimensions, so if only w2's clip were applied (the pre-fix
            // bug), the narrowest clip actually pushed would be bounded by 200/200, not w1's tighter
            // 100/80. Both wrappers are position:relative so they are on h's containing-block chain -
            // CSS Overflow 3 §3 clips only descendants whose containing block chain passes through the
            // clipping box, and an absolutely positioned box's containing block is its nearest positioned
            // ancestor.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='w1' style='position:relative;overflow:hidden;width:100px;height:80px;'>" +
                "<div id='w2' style='position:relative;overflow:hidden;width:200px;height:200px;'>" +
                "<div id='h' style='position:absolute;top:0;left:0;z-index:5;width:300px;height:300px;background:rgb(200,0,0);'></div>" +
                "</div></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var drawIndex = g.Log.FindIndex(e =>
                e is TestRecordingGraphics.DrawRectCall r && r.Color == RColor.FromArgb(200, 0, 0));
            Assert.True(drawIndex >= 0, "hoisted box never painted");

            var pushesBeforeDraw = g.Log.Take(drawIndex).OfType<TestRecordingGraphics.PushClipCall>().ToList();

            Assert.True(pushesBeforeDraw.Count >= 2,
                $"expected at least 2 overflow-clip pushes (one per nested overflow:hidden ancestor) " +
                $"before the hoisted box painted, got {pushesBeforeDraw.Count}");

            var narrowestWidth = pushesBeforeDraw.Min(p => p.Rect.Width);
            var narrowestHeight = pushesBeforeDraw.Min(p => p.Rect.Height);

            Assert.True(narrowestWidth <= 100.5,
                $"expected the cumulative clip to be bounded by w1's 100px width, got {narrowestWidth}");
            Assert.True(narrowestHeight <= 80.5,
                $"expected the cumulative clip to be bounded by w1's 80px height, got {narrowestHeight}");
        }

        [Fact]
        public async Task HoistedBox_PastSingleOverflowHiddenAncestor_GetsItsClipApplied()
        {
            // Baseline/non-regression companion to the two-ancestor case above: a single intermediate
            // overflow:hidden wrapper between a hoisted box and its true stacking context is already
            // handled correctly by the hoisted box's own natural ClipGraphicsByOverflow call (it finds
            // the nearest overflow:hidden ancestor on its containing-block chain regardless of hoisting)
            // - confirm this still works. w1 is positioned, so it is h's containing block.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='w1' style='position:relative;overflow:hidden;width:100px;height:80px;'>" +
                "<div id='h' style='position:absolute;top:0;left:0;z-index:5;width:300px;height:300px;background:rgb(200,0,0);'></div>" +
                "</div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var drawIndex = g.Log.FindIndex(e =>
                e is TestRecordingGraphics.DrawRectCall r && r.Color == RColor.FromArgb(200, 0, 0));
            Assert.True(drawIndex >= 0, "hoisted box never painted");

            var pushesBeforeDraw = g.Log.Take(drawIndex).OfType<TestRecordingGraphics.PushClipCall>().ToList();
            Assert.Contains(pushesBeforeDraw, p => p.Rect.Width <= 100.5 && p.Rect.Height <= 80.5);
        }

        [Fact]
        public async Task HoistedFloatBox_NestedInPlainWrappers_Paints()
        {
            // A float is out-of-flow (NeedsStackingHoist) but not itself a stacking context, so it
            // must still be hoisted through plain wrapper divs like any other out-of-flow content,
            // and paints via PaintImpCore's dedicated float bucket.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div><div><div>" +
                "<div id='f' style='float:left;width:50px;height:50px;background:rgb(10,20,30);'></div>" +
                "</div></div></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            Assert.Contains(rects, r => r.Color == RColor.FromArgb(10, 20, 30));
        }

        [Fact]
        public async Task HoistedFixedBox_NestedInPlainWrappers_Paints()
        {
            // position:fixed is unconditionally a stacking context, so it gets hoisted through plain
            // wrapper divs like any other stacking-context-establishing content, and paints via
            // PaintImpCore's dedicated fixed bucket.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div><div><div>" +
                "<div id='fx' style='position:fixed;top:0;left:0;width:50px;height:50px;background:rgb(11,22,33);'></div>" +
                "</div></div></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            Assert.Contains(rects, r => r.Color == RColor.FromArgb(11, 22, 33));
        }

        [Fact]
        public async Task PositionedFloat_PaintsInPositionedPass_AfterPositionedAncestorBackground()
        {
            // CSS 2.1 Appendix E step 5 paints only NON-positioned floats; a float that is also
            // position:relative is a positioned descendant (step 8, tree order). Painting it in the
            // float pass put it before its own position:relative parent (itself a step-8 box of the
            // same stacking context), whose opaque background then covered it - a sidebar
            // `#contentleft { float: left; position: relative }` inside a positioned, white
            // `#contentcontainer` rendered blank.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div style='position:relative;height:100px;background:rgb(1,2,3);'>" +
                "<div style='float:left;position:relative;width:50px;height:50px;background:rgb(4,5,6);'></div>" +
                "</div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var parentIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(1, 2, 3));
            var floatIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(4, 5, 6));

            Assert.True(parentIndex >= 0, "positioned parent never painted");
            Assert.True(floatIndex >= 0, "positioned float never painted");
            Assert.True(parentIndex < floatIndex,
                $"the positioned float must paint after its positioned parent's background (parent={parentIndex}, float={floatIndex})");
        }

        [Fact]
        public async Task PositionedFloat_PaintsAfterLaterPlainFloatSibling_InPositionedPass()
        {
            // Within one stacking context, a positioned float belongs to step 8 and a plain float to
            // step 5 - so the plain float paints first even though it comes later in tree order.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div style='float:left;position:relative;width:50px;height:50px;background:rgb(7,8,9);'></div>" +
                "<div style='float:left;width:50px;height:50px;background:rgb(10,11,12);'></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var positionedIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(7, 8, 9));
            var plainIndex = rects.FindIndex(r => r.Color == RColor.FromArgb(10, 11, 12));

            Assert.True(positionedIndex >= 0 && plainIndex >= 0, "one of the floats never painted");
            Assert.True(plainIndex < positionedIndex,
                $"the plain float (step 5) must paint before the positioned float (step 8) (plain={plainIndex}, positioned={positionedIndex})");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static CssBox? FindById(CssBox root, string id) =>
            PeachPDF.Html.Core.Utils.DomUtils.GetBoxById(root, id);

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }
    }
}
