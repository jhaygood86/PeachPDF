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

            Assert.Equal("body", parent.HtmlTag!.Name);
        }

        [Fact]
        public async Task FindParent_NullBox_ReturnsRoot()
        {
            var root = await Render("<div></div>");

            var parent = DomUtils.FindParent(root, "div", null);

            Assert.Same(root, parent);
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

            var layers = DomUtils.GetBoxesByLayers([a, b]).ToList();

            Assert.Equal(2, layers.Count);
            Assert.Contains(a, layers[1]);
            Assert.Contains(b, layers[0]);
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
