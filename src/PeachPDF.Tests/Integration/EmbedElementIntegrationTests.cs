using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>&lt;embed&gt;</c> is a void element (it never contains the content after it) and a replaced
    /// element: an image or SVG <c>src</c> is drawn, sized by <c>width</c>/<c>height</c>; any other
    /// resource still reserves its specified box.
    /// </summary>
    public class EmbedElementIntegrationTests
    {
        private const string PngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private const string SvgDataUri =
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='80' height='40'%3E%3Crect width='80' height='40' fill='%232a9d8f'/%3E%3C/svg%3E";

        [Theory]
        [InlineData(PngDataUri)]
        [InlineData(SvgDataUri)]
        public async Task ImageSrc_IsReplacedAndSizedByAttributes(string src)
        {
            var (root, _) = await BuildAndLayout($"<p>x <embed id='e' width='80' height='40' src=\"{src}\"> after</p>");

            var e = Assert.IsType<CssBoxEmbed>(FindById(root, "e"));
            Assert.True(e.IsReplaced);
            Assert.Empty(e.Boxes);
            Assert.Equal(60, e.ActualRight - e.Location.X, 1);
            Assert.Equal(30, e.ActualBottom - e.Location.Y, 1);
        }

        [Fact]
        public async Task FollowingContent_IsASiblingNotAChild()
        {
            var (root, _) = await BuildAndLayout($"<p id='p'><embed id='e' width='80' height='40' src=\"{PngDataUri}\"> after</p>");

            var e = FindById(root, "e")!;
            Assert.Empty(e.Boxes);
            var p = FindById(root, "p")!;
            Assert.Contains(p.Boxes, b => b != e && (b.Words.Count > 0 || b.Boxes.Count > 0));
        }

        [Fact]
        public async Task NoSrc_ReservesWidthAndHeight()
        {
            var (root, _) = await BuildAndLayout("<p>x <embed id='e' width='80' height='40' style='background:#ccc'> after</p>");

            var e = Assert.IsType<CssBoxEmbed>(FindById(root, "e"));
            Assert.False(e.IsReplaced);
            Assert.Equal(60, e.ActualRight - e.Location.X, 1);
            Assert.Equal(30, e.ActualBottom - e.Location.Y, 1);
        }

        [Fact]
        public async Task PluginType_DoesNotLoadSrc_AndReservesBox()
        {
            var (root, _) = await BuildAndLayout($"<p><embed id='e' type='application/x-shockwave-flash' width='80' height='40' src=\"{PngDataUri}\"></p>");

            var e = Assert.IsType<CssBoxEmbed>(FindById(root, "e"));
            Assert.False(e.IsReplaced);
            Assert.Equal(60, e.ActualRight - e.Location.X, 1);
        }

        [Fact]
        public async Task NonImageDataUri_IsNotReplaced()
        {
            var (root, _) = await BuildAndLayout("<p><embed id='e' width='80' height='40' src='data:text/plain,hi'></p>");

            Assert.False(Assert.IsType<CssBoxEmbed>(FindById(root, "e")).IsReplaced);
        }

        [Fact]
        public async Task Wbr_DoesNotSwallowFollowingContent()
        {
            var (root, _) = await BuildAndLayout("<p id='p'>a<wbr id='w'>b</p>");

            Assert.Empty(FindById(root, "w")!.Boxes);
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string body)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body>{body}</body></html>", null);

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
            if (box.HtmlTag?.TryGetAttribute("id", "") == id)
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
