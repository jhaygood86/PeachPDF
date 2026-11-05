using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF;
using PeachBrackets.Svg;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// Selectors 4's <c>+</c> and <c>~</c> are defined over the DOM, so an inline <c>&lt;svg&gt;</c> is the sibling of the element
    /// before it even when layout wrapped it (with its inline neighbours) in an anonymous block box.
    /// </summary>
    public class InlineSvgSiblingCombinatorTests
    {
        private static async Task<SvgRectElement> BuildRectAsync(string css, string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var svgBox = Assert.IsType<CssBoxSvg>(DomUtils.GetBoxByTagName(container.Root!, "svg"));
            return Assert.IsType<SvgRectElement>(svgBox.Document!.Children[0]);
        }

        private const string Svg = "<svg width=\"20\" height=\"20\"><rect class=\"x\" width=\"10\" height=\"10\"/></svg>";
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);

        [Fact]
        public async Task AdjacentSibling_InlineSvgAfterABlock_Matches()
        {
            var rect = await BuildRectAsync("a + svg .x { fill: #ff0000 }", "<div><a style=\"display:block\">x</a>" + Svg + "</div>");

            Assert.Equal(Red, rect.Fill.PaintColor);
        }

        [Fact]
        public async Task GeneralSibling_InlineSvgAfterABlockAndText_Matches()
        {
            var rect = await BuildRectAsync("a ~ svg .x { fill: #ff0000 }", "<div><a style=\"display:block\">x</a>text " + Svg + "</div>");

            Assert.Equal(Red, rect.Fill.PaintColor);
        }

        [Fact]
        public async Task AdjacentSibling_WithADifferentPrecedingElement_DoesNotMatch()
        {
            var rect = await BuildRectAsync("b + svg .x { fill: #ff0000 }", "<div><a style=\"display:block\">x</a>" + Svg + "</div>");

            Assert.NotEqual(Red, rect.Fill.PaintColor);
        }
        [Fact]
        public async Task SelectorView_SkipsAnonymousBoxes()
        {
            var html = "<!DOCTYPE html><html><head></head><body><div id=\"d\" lang=\"fr\"><a style=\"display:block\">x</a>" + Svg + "</div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);
            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var svgBox = DomUtils.GetBoxByTagName(container.Root!, "svg")!;
            Assert.Null(svgBox.ParentBox!.HtmlTag); // layout wrapped it in an anonymous block

            ICssDomNode node = svgBox;
            var parent = node.Parent!;
            Assert.Equal("div", parent.TagName);
            Assert.Equal("fr", parent.GetAttribute("lang"));
            Assert.Equal(System.StringComparison.OrdinalIgnoreCase, parent.NameComparison);
            Assert.False(parent.IsRoot);
            Assert.False(parent.IsEmpty);
            Assert.Equal("body", parent.Parent!.TagName);

            parent.CustomProperties = new() { ["--x"] = "1" };
            Assert.Equal("1", parent.CustomProperties!["--x"]);

            Assert.Equal(new[] { "a", "svg" }, parent.Children.Select(c => c.TagName).ToArray());
            Assert.Contains(node, parent.Children);

            Assert.Same(parent, node.Parent);
        }
    }
}
