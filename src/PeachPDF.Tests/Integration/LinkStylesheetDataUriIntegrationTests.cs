using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for two bugs found while implementing &lt;object&gt; (Acid2 compliance work) in the
    /// same &lt;link&gt;-stylesheet-loading path: (1) `rel` must be matched as a space-separated set of
    /// HTML link types (e.g. `rel="appendix stylesheet"`), not an exact string, and (2) the default
    /// <see cref="Network.DataUriNetworkLoader"/> must correctly decode a non-base64, percent-encoded
    /// `data:text/css,...` stylesheet and report its MIME type so <see cref="Handlers.StylesheetLoadHandler"/>'s
    /// content-type sniff accepts it - both are exercised directly by the real Acid2 test's
    /// "preferred stylesheet" `&lt;link&gt;`.
    /// </summary>
    public class LinkStylesheetDataUriIntegrationTests
    {
        // The base rule and the override are both class selectors of equal specificity - mirrors the
        // real Acid2 fixture's `.picture { background: red }` (main stylesheet) overridden by
        // `.picture { background: none }` (the linked "preferred stylesheet") - so which one wins is
        // a genuine test of load-order/cascade, not just of selector specificity (an inline `style`
        // attribute would always win regardless of whether the link loaded at all).

        [Fact]
        public async Task MultiTokenRelWithDataUriCss_IsLoadedAndApplied()
        {
            // ".target { background: blue; }" as a plain (non-base64) percent-encoded data: URI.
            const string css = "%2Etarget%20%7B%20background%3A%20blue%3B%20%7D";
            var html = "<!DOCTYPE html><html><head>"
                + "<style>.target { background: red; width: 10px; height: 10px; }</style>"
                + "<link rel=\"appendix stylesheet\" href=\"data:text/css," + css + "\" />"
                + "</head><body><div id=\"target\" class=\"target\"></div></body></html>";

            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;

            Assert.Equal("rgb(0, 0, 255)", target.BackgroundColor);
        }

        [Fact]
        public async Task ExactRelStylesheet_StillWorks_WithDataUriCss()
        {
            const string css = "%2Etarget%20%7B%20background%3A%20blue%3B%20%7D";
            var html = "<!DOCTYPE html><html><head>"
                + "<style>.target { background: red; width: 10px; height: 10px; }</style>"
                + "<link rel=\"stylesheet\" href=\"data:text/css," + css + "\" />"
                + "</head><body><div id=\"target\" class=\"target\"></div></body></html>";

            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;

            Assert.Equal("rgb(0, 0, 255)", target.BackgroundColor);
        }

        [Fact]
        public async Task NonStylesheetRelToken_IsNotLoadedAsStylesheet()
        {
            const string css = "%2Etarget%20%7B%20background%3A%20blue%3B%20%7D";
            var html = "<!DOCTYPE html><html><head>"
                + "<style>.target { background: red; width: 10px; height: 10px; }</style>"
                + "<link rel=\"icon\" href=\"data:text/css," + css + "\" />"
                + "</head><body><div id=\"target\" class=\"target\"></div></body></html>";

            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;

            Assert.Equal("rgb(255, 0, 0)", target.BackgroundColor);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
