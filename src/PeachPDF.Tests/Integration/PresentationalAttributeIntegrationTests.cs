using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Deprecated presentational HTML attributes (<c>align</c>, <c>nowrap</c>) that <c>DomParser</c>
    /// translates directly into typed <c>CssProperty</c> values rather than going through the CSS
    /// cascade. See <see cref="BorderStylePaintIntegrationTests"/> for the sibling <c>border</c>
    /// attribute coverage.
    /// </summary>
    public class PresentationalAttributeIntegrationTests
    {
        [Fact]
        public async Task ImgAlignLeft_SetsFloatLeft()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='left' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(Floating.Left, img.Float.Value);
        }

        [Fact]
        public async Task ImgAlignRight_SetsFloatRight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='right' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(Floating.Right, img.Float.Value);
        }

        [Fact]
        public async Task AlignAttributeLeft_OnNonImgElement_SetsTextAlignLeft()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='left'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Left, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeCenter_OnNonImgElement_SetsTextAlignCenter()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='center'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Center, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeRight_OnNonImgElement_SetsTextAlignRight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='right'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Right, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeJustify_OnNonImgElement_SetsTextAlignJustify()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='justify'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Justify, div.TextAlign.Value);
        }

        [Fact]
        public async Task NowrapAttribute_SetsWhiteSpaceNoWrap()
        {
            var (root, _) = await BuildAndLayout(Wrap("<td id='d' nowrap='nowrap'>x</td>"));
            var td = FindById(root, "d")!;

            Assert.Equal(Whitespace.NoWrap, td.WhiteSpace.Value);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
