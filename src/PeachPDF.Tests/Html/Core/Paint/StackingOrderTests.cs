using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Paint;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Paint
{
    using DomUtils = PeachPDF.Html.Core.Utils.DomUtils;

    /// <summary>
    /// Paint order within a stacking context (CSS 2.1 Appendix E): which fragments a context claims,
    /// and how they group into z-index layers.
    /// </summary>
    public class StackingOrderTests
    {
        [Fact]
        public async Task ByLayers_GroupsByZIndexInOrder()
        {
            var (root, container) = await RenderWithContainer(
                "<div><span id='a' style='position: relative; z-index: 2;'>A</span>" +
                "<span id='b' style='position: relative; z-index: 1;'>B</span></div>");
            var a = DomUtils.GetBoxById(root, "a")!;
            var b = DomUtils.GetBoxById(root, "b")!;

            var layers = StackingOrder.ByLayers(
            [
                new StackingOrder.StackingParticipant(FragmentPaintHarness.FragmentOf(container, a), []),
                new StackingOrder.StackingParticipant(FragmentPaintHarness.FragmentOf(container, b), [])
            ]).ToList();

            Assert.Equal(2, layers.Count);
            Assert.Contains(layers[1], p => p.Box == a);
            Assert.Contains(layers[0], p => p.Box == b);
        }

        [Fact]
        public async Task Flatten_DeeplyNestedStackingContextBox_IsDiscoveredByItsEnclosingContext()
        {
            // Regression: the old algorithm only ever yielded a child when it was NOT itself a
            // stacking-context box, so a position:relative;z-index descendant - however deep, through
            // any number of plain wrapper divs - was silently dropped and never painted at all.
            var (root, container) = await RenderWithContainer(
                "<div><div><div>" +
                "<span id='inner' style='position: relative; z-index: 1;'>Text</span>" +
                "</div></div></div>");
            var inner = DomUtils.GetBoxById(root, "inner")!;

            var flattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, root)).ToList();

            Assert.Contains(flattened, p => p.Box == inner);
        }

        [Fact]
        public async Task Flatten_OutOfFlowDescendantOfOpacityBox_StaysWithinIt()
        {
            // Opacity now establishes a stacking context, so an out-of-flow descendant of an
            // opacity box must be claimed by the opacity box's own Flatten call, not hoisted past it
            // to an outer ancestor (which would bypass the opacity compositing group entirely and
            // paint the descendant at full alpha, and at the wrong z-order timing).
            var (root, container) = await RenderWithContainer(
                "<div id='op' style='opacity: 0.5;'>" +
                "<div id='abschild' style='position: absolute; top: 0; left: 0;'>x</div>" +
                "</div>");
            var op = DomUtils.GetBoxById(root, "op")!;
            var abschild = DomUtils.GetBoxById(root, "abschild")!;

            var rootFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, root)).ToList();
            Assert.Contains(rootFlattened, p => p.Box == op);
            Assert.DoesNotContain(rootFlattened, p => p.Box == abschild);

            var opFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, op)).ToList();
            Assert.Contains(opFlattened, p => p.Box == abschild);
        }

        [Fact]
        public async Task Flatten_OutOfFlowDescendantOfTransformedBox_StaysWithinIt()
        {
            var (root, container) = await RenderWithContainer(
                "<div id='tr' style='transform: rotate(10deg);'>" +
                "<div id='abschild' style='position: absolute; top: 0; left: 0;'>x</div>" +
                "</div>");
            var tr = DomUtils.GetBoxById(root, "tr")!;
            var abschild = DomUtils.GetBoxById(root, "abschild")!;

            var rootFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, root)).ToList();
            Assert.Contains(rootFlattened, p => p.Box == tr);
            Assert.DoesNotContain(rootFlattened, p => p.Box == abschild);

            var trFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, tr)).ToList();
            Assert.Contains(trFlattened, p => p.Box == abschild);
        }

        [Fact]
        public async Task Flatten_ZIndexedBoxNestedInPlainOutOfFlowWrapper_EscapesToTrueAncestor()
        {
            // Full-fidelity case: a position:relative;z-index box nested inside a plain
            // position:absolute wrapper (no z-index of its own, so it does NOT establish a stacking
            // context) must still be discoverable by the wrapper's true enclosing stacking context -
            // it must not be trapped competing only within the absolute wrapper's own local scope.
            var (root, container) = await RenderWithContainer(
                "<div style='position: absolute; top: 0; left: 0;'>" +
                "<span id='nested' style='position: relative; z-index: -1;'>Text</span>" +
                "</div>");
            var nested = DomUtils.GetBoxById(root, "nested")!;

            var rootFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, root)).ToList();

            Assert.Contains(rootFlattened, p => p.Box == nested);
        }

        [Fact]
        public async Task Flatten_PlainChildOfPlainWrapper_StaysNestedNotHoisted()
        {
            // A plain (non-out-of-flow, non-stacking-context) child of a plain, non-stacking-context
            // wrapper must be discovered via the wrapper's own Flatten call, not hoisted straight to a
            // distant ancestor - the wrapper's own paint call is what keeps e.g. an overflow:hidden
            // clip on the wrapper wrapped around this child. `root` here is a synthetic box above the
            // real <html>/<body> elements (see DomParser.GenerateCssTree), so `wrapper` itself is a few
            // plain levels below `root` too - the point under test is that `plainChild` never leaks
            // into any ancestor's flatten result above its own direct parent.
            var (root, container) = await RenderWithContainer(
                "<div id='wrapper'><span id='plainChild'>Text</span></div>");
            var wrapper = DomUtils.GetBoxById(root, "wrapper")!;
            var plainChild = DomUtils.GetBoxById(root, "plainChild")!;

            var rootFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, root)).ToList();
            Assert.DoesNotContain(rootFlattened, p => p.Box == wrapper);
            Assert.DoesNotContain(rootFlattened, p => p.Box == plainChild);

            var wrapperFlattened = StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, wrapper)).ToList();
            Assert.Contains(wrapperFlattened, p => p.Box == plainChild);
        }

        [Theory]
        // Genuinely inline-level content is in the inline pass.
        [InlineData("<span id='t'>Text</span>", true)]
        // A block wrapper whose entire content is inline joins the inline pass too (Acid2's #eyes-a).
        [InlineData("<div id='t'><span>Text</span></div>", true)]
        // A block with a block child does not.
        [InlineData("<div id='t'><div>Text</div></div>", false)]
        // An empty block box is not vacuously inline.
        [InlineData("<div id='t' style='height:5pt'></div>", false)]
        public async Task ActsAsInline_ClassifiesBoxesForTheInlinePass(string html, bool expected)
        {
            var (root, _) = await RenderWithContainer(html);

            Assert.Equal(expected, StackingOrder.ActsAsInline(DomUtils.GetBoxById(root, "t")!));
        }

        /// <summary>
        /// Renders and returns the container too: the flatten walks the fragment tree, so a box has to
        /// be resolved to its fragment first.
        /// </summary>
        private static async Task<(CssBox Root, HtmlContainerInt Container)> RenderWithContainer(string bodyHtml)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
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
