using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Utils
{
    using DomUtils = PeachPDF.Html.Core.Utils.DomUtils;

    public class DomUtilsTests
    {
        [Fact]
        public async Task GetBoxById_FindsMatchingElement()
        {
            var root = await Render("<div id='outer'><span id='inner'>Text</span></div>");

            var found = DomUtils.GetBoxById(root, "inner");

            Assert.NotNull(found);
            Assert.Equal("span", found!.HtmlTag!.Name);
        }

        [Fact]
        public async Task GetBoxById_UnknownId_ReturnsNull()
        {
            var root = await Render("<div id='outer'></div>");

            Assert.Null(DomUtils.GetBoxById(root, "missing"));
        }

        [Fact]
        public async Task GetBoxById_NullOrEmptyId_ReturnsNull()
        {
            var root = await Render("<div id='outer'></div>");

            Assert.Null(DomUtils.GetBoxById(root, null));
            Assert.Null(DomUtils.GetBoxById(root, string.Empty));
        }

        [Fact]
        public async Task GetBoxByTagName_FindsFirstMatchingTag()
        {
            var root = await Render("<div><p>First</p><p>Second</p></div>");

            var found = DomUtils.GetBoxByTagName(root, "p");

            Assert.NotNull(found);
            Assert.Equal("p", found!.HtmlTag!.Name);
        }

        [Fact]
        public async Task GetBoxByTagName_NoMatch_ReturnsNull()
        {
            var root = await Render("<div></div>");

            Assert.Null(DomUtils.GetBoxByTagName(root, "table"));
        }

        [Fact]
        public async Task FindParent_ReturnsParentOfAncestorMatchingTagName()
        {
            // FindParent walks up from `box` looking for an ancestor tagged `tagName`, then returns
            // *that ancestor's own parent* (not the matched ancestor itself) -- so searching for
            // "div" from a <span> nested one level inside a <div> returns the div's parent (<body>).
            var root = await Render("<div><span id='inner'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            var parent = DomUtils.FindParent(root, "div", span);

            Assert.Equal("body", parent!.HtmlTag!.Name);
        }

        [Fact]
        public async Task FindParent_NullBox_ReturnsNull()
        {
            // A null `box` means the walk-up reached the top of the tree without ever finding a
            // matching ancestor - i.e. no open element with this tag name exists at all (e.g. a stray
            // closing tag with no matching open element). This must be distinguishable from "found a
            // match whose parent happens to be null" so the caller (HtmlParser.CloseElement) can treat
            // it as a no-op instead of incorrectly jumping to the document root.
            var root = await Render("<div></div>");

            var parent = DomUtils.FindParent(root, "div", null);

            Assert.Null(parent);
        }

        [Fact]
        public async Task GetPreviousSibling_ReturnsPrecedingBox()
        {
            var root = await Render("<div><p id='a'>A</p><p id='b'>B</p></div>");
            var b = DomUtils.GetBoxById(root, "b")!;

            var previous = DomUtils.GetPreviousSibling(b);

            Assert.NotNull(previous);
            Assert.Equal("a", previous!.HtmlTag!.TryGetAttribute("id"));
        }

        [Fact]
        public async Task GetPreviousSibling_FirstChild_ReturnsNull()
        {
            var root = await Render("<div><p id='a'>A</p></div>");
            var a = DomUtils.GetBoxById(root, "a")!;

            Assert.Null(DomUtils.GetPreviousSibling(a));
        }

        [Fact]
        public async Task GetFollowingSiblings_ReturnsMatchingLaterSiblings()
        {
            var root = await Render("<div><p id='a'>A</p><p id='b'>B</p><p id='c'>C</p></div>");
            var a = DomUtils.GetBoxById(root, "a")!;

            var following = DomUtils.GetFollowingSiblings(a, _ => true, isConsecutive: false).ToList();

            Assert.Equal(2, following.Count);
        }

        [Fact]
        public async Task ContainsInlinesOnly_AllInlineChildren_ReturnsTrue()
        {
            var root = await Render("<div><span>A</span><span>B</span></div>");
            var div = DomUtils.GetBoxByTagName(root, "div")!;

            Assert.True(DomUtils.ContainsInlinesOnly(div));
        }

        [Fact]
        public async Task ContainsInlinesOnly_HasBlockChild_ReturnsFalse()
        {
            var root = await Render("<div><p>Block</p></div>");
            var div = DomUtils.GetBoxByTagName(root, "div")!;

            Assert.False(DomUtils.ContainsInlinesOnly(div));
        }

        [Fact]
        public async Task GetNearestParentElementBox_SkipsAnonymousBoxes_ReturnsTaggedAncestor()
        {
            var root = await Render("<div id='outer'><span id='inner'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            var parent = DomUtils.GetNearestParentElementBox(span);

            Assert.NotNull(parent);
            Assert.Equal("outer", parent!.HtmlTag!.TryGetAttribute("id"));
        }

        [Fact]
        public async Task GetAllLinkBoxes_CollectsClickableVisibleBoxes()
        {
            var root = await Render("<div><a href='#'>Link</a><span>Not a link</span></div>");

            var links = new System.Collections.Generic.List<CssBox>();
            DomUtils.GetAllLinkBoxes(root, links);

            Assert.Contains(links, b => b.HtmlTag?.Name == "a");
        }

        [Fact]
        public async Task IsStackingContextBox_Root_ReturnsTrue()
        {
            var root = await Render("<div></div>");

            Assert.True(DomUtils.IsStackingContextBox(root));
        }

        [Fact]
        public async Task IsStackingContextBox_RelativeWithZIndex_ReturnsTrue()
        {
            var root = await Render("<div><span id='inner' style='position: relative; z-index: 1;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_StaticPosition_ReturnsFalse()
        {
            var root = await Render("<div><span id='inner'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_Fixed_ReturnsTrue()
        {
            var root = await Render("<div><span id='inner' style='position: fixed;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_Sticky_ReturnsTrue()
        {
            var root = await Render("<div><span id='inner' style='position: sticky;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_RelativeWithoutZIndex_ReturnsFalse()
        {
            // Per spec, position:relative/absolute alone does not establish a stacking context - only
            // an explicit z-index (auto excluded) does. The element still participates in its nearest
            // ancestor's stacking context, just without becoming its own atomic unit.
            var root = await Render("<div><span id='inner' style='position: relative;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_OpacityLessThanOne_ReturnsTrue()
        {
            var root = await Render("<div><span id='inner' style='opacity: 0.5;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_FullyOpaqueExplicitOpacity_ReturnsFalse()
        {
            var root = await Render("<div><span id='inner' style='opacity: 1;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_Transformed_ReturnsTrue()
        {
            var root = await Render("<div><span id='inner' style='transform: rotate(10deg);'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_TransformNone_ReturnsFalse()
        {
            var root = await Render("<div><span id='inner' style='transform: none;'>Text</span></div>");
            var span = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(span));
        }

        [Fact]
        public async Task IsStackingContextBox_FlexItemWithZIndex_ReturnsTrue()
        {
            // A flex item establishes a stacking context from z-index alone (CSS Flexbox §z-order) -
            // unlike a plain block/inline child, it doesn't need `position` set at all.
            var root = await Render(
                "<div style='display: flex;'><div id='inner' style='z-index: 1;'>Text</div></div>");
            var item = DomUtils.GetBoxById(root, "inner")!;

            Assert.True(DomUtils.IsStackingContextBox(item));
        }

        [Fact]
        public async Task IsStackingContextBox_FlexItemWithoutZIndex_ReturnsFalse()
        {
            var root = await Render(
                "<div style='display: flex;'><div id='inner'>Text</div></div>");
            var item = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(item));
        }

        [Fact]
        public async Task IsStackingContextBox_ZIndexOutsideFlexContainer_ReturnsFalse()
        {
            // z-index on a non-positioned, non-flex-item block has no effect - it must not be
            // mistaken for the flex-item stacking-context rule.
            var root = await Render("<div id='inner' style='z-index: 1;'>Text</div>");
            var div = DomUtils.GetBoxById(root, "inner")!;

            Assert.False(DomUtils.IsStackingContextBox(div));
        }

        [Fact]
        public async Task IsProperTableChild_TableRow_ReturnsTrue()
        {
            var root = await Render("<table><tr id='row'><td>Cell</td></tr></table>");
            var row = DomUtils.GetBoxById(root, "row")!;

            Assert.True(DomUtils.IsProperTableChild(row));
        }

        [Fact]
        public async Task IsProperTableChild_NonTableBox_ReturnsFalse()
        {
            var root = await Render("<div id='plain'></div>");
            var div = DomUtils.GetBoxById(root, "plain")!;

            Assert.False(DomUtils.IsProperTableChild(div));
        }

        [Fact]
        public async Task GetBoxesByLayers_GroupsByZIndexInOrder()
        {
            var root = await Render(
                "<div><span id='a' style='position: relative; z-index: 2;'>A</span>" +
                "<span id='b' style='position: relative; z-index: 1;'>B</span></div>");
            var a = DomUtils.GetBoxById(root, "a")!;
            var b = DomUtils.GetBoxById(root, "b")!;

            var layers = DomUtils.GetBoxesByLayers(
                [new DomUtils.StackingParticipant(a, []), new DomUtils.StackingParticipant(b, [])]).ToList();

            Assert.Equal(2, layers.Count);
            Assert.Contains(layers[1], p => p.Box == a);
            Assert.Contains(layers[0], p => p.Box == b);
        }

        [Fact]
        public async Task FlattenStackingContext_DeeplyNestedStackingContextBox_IsDiscoveredByItsEnclosingContext()
        {
            // Regression: the old algorithm only ever yielded a child when it was NOT itself a
            // stacking-context box, so a position:relative;z-index descendant - however deep, through
            // any number of plain wrapper divs - was silently dropped and never painted at all.
            var root = await Render(
                "<div><div><div>" +
                "<span id='inner' style='position: relative; z-index: 1;'>Text</span>" +
                "</div></div></div>");
            var inner = DomUtils.GetBoxById(root, "inner")!;

            var flattened = DomUtils.FlattenStackingContext(root).ToList();

            Assert.Contains(flattened, p => p.Box == inner);
        }

        [Fact]
        public async Task FlattenStackingContext_OutOfFlowDescendantOfOpacityBox_StaysWithinIt()
        {
            // Opacity now establishes a stacking context, so an out-of-flow descendant of an
            // opacity box must be claimed by the opacity box's own FlattenStackingContext call, not
            // hoisted past it to an outer ancestor (which would bypass the opacity compositing group
            // entirely and paint the descendant at full alpha, and at the wrong z-order timing).
            var root = await Render(
                "<div id='op' style='opacity: 0.5;'>" +
                "<div id='abschild' style='position: absolute; top: 0; left: 0;'>x</div>" +
                "</div>");
            var op = DomUtils.GetBoxById(root, "op")!;
            var abschild = DomUtils.GetBoxById(root, "abschild")!;

            var rootFlattened = DomUtils.FlattenStackingContext(root).ToList();
            Assert.Contains(rootFlattened, p => p.Box == op);
            Assert.DoesNotContain(rootFlattened, p => p.Box == abschild);

            var opFlattened = DomUtils.FlattenStackingContext(op).ToList();
            Assert.Contains(opFlattened, p => p.Box == abschild);
        }

        [Fact]
        public async Task FlattenStackingContext_OutOfFlowDescendantOfTransformedBox_StaysWithinIt()
        {
            var root = await Render(
                "<div id='tr' style='transform: rotate(10deg);'>" +
                "<div id='abschild' style='position: absolute; top: 0; left: 0;'>x</div>" +
                "</div>");
            var tr = DomUtils.GetBoxById(root, "tr")!;
            var abschild = DomUtils.GetBoxById(root, "abschild")!;

            var rootFlattened = DomUtils.FlattenStackingContext(root).ToList();
            Assert.Contains(rootFlattened, p => p.Box == tr);
            Assert.DoesNotContain(rootFlattened, p => p.Box == abschild);

            var trFlattened = DomUtils.FlattenStackingContext(tr).ToList();
            Assert.Contains(trFlattened, p => p.Box == abschild);
        }

        [Fact]
        public async Task FlattenStackingContext_ZIndexedBoxNestedInPlainOutOfFlowWrapper_EscapesToTrueAncestor()
        {
            // Full-fidelity case: a position:relative;z-index box nested inside a plain
            // position:absolute wrapper (no z-index of its own, so it does NOT establish a stacking
            // context) must still be discoverable by the wrapper's true enclosing stacking context -
            // it must not be trapped competing only within the absolute wrapper's own local scope.
            var root = await Render(
                "<div style='position: absolute; top: 0; left: 0;'>" +
                "<span id='nested' style='position: relative; z-index: -1;'>Text</span>" +
                "</div>");
            var nested = DomUtils.GetBoxById(root, "nested")!;

            var rootFlattened = DomUtils.FlattenStackingContext(root).ToList();

            Assert.Contains(rootFlattened, p => p.Box == nested);
        }

        [Fact]
        public async Task FlattenStackingContext_PlainChildOfPlainWrapper_StaysNestedNotHoisted()
        {
            // A plain (non-out-of-flow, non-stacking-context) child of a plain, non-stacking-context
            // wrapper must be discovered via the wrapper's own FlattenStackingContext call, not
            // hoisted straight to a distant ancestor - the wrapper's own Paint() call is what keeps
            // e.g. an overflow:hidden clip on the wrapper wrapped around this child. `root` here is a
            // synthetic box above the real <html>/<body> elements (see DomParser.GenerateCssTree), so
            // `wrapper` itself is a few plain levels below `root` too - the point under test is that
            // `plainChild` never leaks into any ancestor's flatten result above its own direct parent.
            var root = await Render(
                "<div id='wrapper'><span id='plainChild'>Text</span></div>");
            var wrapper = DomUtils.GetBoxById(root, "wrapper")!;
            var plainChild = DomUtils.GetBoxById(root, "plainChild")!;

            var rootFlattened = DomUtils.FlattenStackingContext(root).ToList();
            Assert.DoesNotContain(rootFlattened, p => p.Box == wrapper);
            Assert.DoesNotContain(rootFlattened, p => p.Box == plainChild);

            var wrapperFlattened = DomUtils.FlattenStackingContext(wrapper).ToList();
            Assert.Contains(wrapperFlattened, p => p.Box == plainChild);
        }

        // --- Helper ---

        private static async Task<CssBox> Render(string bodyHtml)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
