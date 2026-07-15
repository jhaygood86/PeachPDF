using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    public class OrphansWidowsIntegrationTests
    {
        [Fact]
        public async Task Orphans_DefaultsToTwo()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("2", box.Orphans);
        }

        [Fact]
        public async Task Widows_DefaultsToTwo()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("2", box.Widows);
        }

        [Fact]
        public async Task Orphans_ParsesExplicitValue()
        {
            var box = await FindByIdAsync("<p id='p' style='orphans:3'>text</p>");
            Assert.Equal("3", box.Orphans);
        }

        [Fact]
        public async Task Widows_ParsesExplicitValue()
        {
            var box = await FindByIdAsync("<p id='p' style='widows:1'>text</p>");
            Assert.Equal("1", box.Widows);
        }

        [Fact]
        public async Task Orphans_RejectsZero()
        {
            // orphans/widows must be >= 1 per spec; an invalid value leaves the property at its default.
            var box = await FindByIdAsync("<p id='p' style='orphans:0'>text</p>");
            Assert.Equal("2", box.Orphans);
        }

        [Fact]
        public async Task Widows_IsInherited()
        {
            var html = Wrap("<div style='widows:4'><p id='p'>text</p></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("4", box.Widows);
        }

        [Fact]
        public async Task Orphans_IsInherited()
        {
            var html = Wrap("<div style='orphans:5'><p id='p'>text</p></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("5", box.Orphans);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindById(root, "p")!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

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
