using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The <c>type</c> attribute on ol/ul/li is a presentational hint for <c>list-style-type</c>
    /// (HTML Standard rendering, Lists); author CSS still wins.
    /// </summary>
    public class ListTypeAttributeTests
    {
        [Theory]
        [InlineData("1", "3.")]
        [InlineData("a", "c.")]
        [InlineData("A", "C.")]
        [InlineData("i", "iii.")]
        [InlineData("I", "III.")]
        public async Task OlType_NumbersWithMatchingStyle(string type, string expected)
        {
            var root = await BuildAndLayout($"<ol type='{type}' start='3'><li id='a'>a</li></ol>");
            Assert.Equal(expected, MarkerOf(root, "a").Text);
        }

        [Fact]
        public async Task OlType_IsCaseSensitive_AndInvalidValueIgnored()
        {
            var root = await BuildAndLayout("<ol type='B'><li id='a'>a</li></ol><ol type='bogus'><li id='b'>b</li></ol>");
            Assert.Equal("1.", MarkerOf(root, "a").Text);
            Assert.Equal("1.", MarkerOf(root, "b").Text);
        }

        [Theory]
        [InlineData("square")]
        [InlineData("SQUARE")]
        [InlineData("circle")]
        [InlineData("disc")]
        public async Task UlType_SelectsBulletShape(string type)
        {
            var root = await BuildAndLayout($"<ul type='{type}'><li id='a'>a</li></ul>");
            Assert.Equal(type.ToLowerInvariant(), MarkerOf(root, "a").MarkerShape);
        }

        [Fact]
        public async Task LiType_OverridesListForThatItemOnly()
        {
            var root = await BuildAndLayout("<ol type='a'><li id='a'>a</li><li id='b' type='I'>b</li></ol>");
            Assert.Equal("a.", MarkerOf(root, "a").Text);
            Assert.Equal("II.", MarkerOf(root, "b").Text);
        }

        [Fact]
        public async Task AuthorCss_BeatsTheHint()
        {
            var root = await BuildAndLayout(
                "<style>.s { list-style-type: lower-roman }</style>"
                + "<ol type='a' class='s'><li id='a'>a</li></ol>"
                + "<ol type='a'><li id='b' style='list-style-type: upper-roman'>b</li></ol>");
            Assert.Equal("i.", MarkerOf(root, "a").Text);
            Assert.Equal("I.", MarkerOf(root, "b").Text);
        }

        [Fact]
        public async Task TypeOnOtherElements_IsIgnored()
        {
            var root = await BuildAndLayout("<div type='a'><ol><li id='a'>a</li></ol></div>");
            Assert.Equal("1.", MarkerOf(root, "a").Text);
        }

        private static CssBoxMarker MarkerOf(CssBox root, string id) =>
            (CssBoxMarker)FindById(root, id)!.Boxes.Single(b => b.IsMarkerPseudoElement);

        private static async Task<CssBox> BuildAndLayout(string body)
        {
            var renderContext = new HtmlRenderContext();
            renderContext.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body>{body}</body></html>", null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
