using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for a <c>&lt;style&gt;</c> element whose CSS contains a <c>&lt;</c> character (e.g. an
    /// <c>@property</c> <c>syntax: "&lt;color&gt;"</c> descriptor, or a <c>content: "&lt;"</c> value). The HTML
    /// tokenizer splits such raw text into several data tokens at each <c>&lt;</c>; those fragments are one
    /// stylesheet and must be concatenated before parsing, otherwise a declaration is split mid-value and every
    /// rule after the <c>&lt;</c> silently fails to apply.
    /// </summary>
    public class StyleElementTextConcatenationTests
    {
        [Fact]
        public async Task RuleAfterLessThanInDeclaration_StillApplies()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #a { --x: "<"; }
                  #b { color: green; }
                </style></head><body>
                <div id="a">a</div><div id="b">b</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var b = FindById(root, "b")!;

            Assert.NotNull(b);
            // Without concatenation, "#b { color: green }" lands in a mis-parsed fragment starting at '<' and
            // never applies, leaving the default black.
            Assert.Equal("rgb(0, 128, 0)", b.Color);
        }

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
