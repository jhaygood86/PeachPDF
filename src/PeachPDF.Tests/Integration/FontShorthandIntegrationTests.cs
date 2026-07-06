using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the CSS `font` shorthand at the cascade level. `font` is expanded into its
    /// longhands (font-style/variant/weight/stretch/size/line-height/family) entirely by Layer A's
    /// FontProperty (a real ShorthandProperty - see CSS/PropertyTests/FontProperty.cs for parser-level
    /// coverage) before DomParser's cascade ever sees it; these tests confirm that end-to-end pipeline,
    /// including the calc()-in-font-size case a since-removed regex-based CssUtils.SetFontPropertyValue
    /// used to mangle (it searched for the first length-shaped substring rather than parsing the whole
    /// shorthand grammar, so "font: bold calc(12px + 4px)/1.4 Arial" corrupted font-size, dropped
    /// line-height, and injected leftover expression text into font-family).
    /// </summary>
    public class FontShorthandIntegrationTests
    {
        [Fact]
        public async Task FontShorthand_FullForm_ResolvesAllLonghands()
        {
            var root = await BuildBoxTree(FontHtml("italic bold 16px/1.4 Arial"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("italic", el!.FontStyle);
            Assert.Equal("bold", el.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
        }

        [Fact]
        public async Task FontShorthand_CalcFontSize_FoldsCorrectly()
        {
            // The specific case the old regex-based parser mangled: font-size folded to "12px" (dropping
            // the "+ 4px" term), line-height silently discarded, and "+ 4px)/1.4 Arial" leaking into
            // font-family instead. FontProperty's real converter resolves calc() the same as any other
            // property.
            var root = await BuildBoxTree(FontHtml("bold calc(12px + 4px)/1.4 Arial"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("bold", el!.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
            Assert.DoesNotContain("+", el.FontFamily);
            Assert.DoesNotContain("calc", el.FontFamily);
        }

        [Fact]
        public async Task FontShorthand_WithVar_ResolvesAfterSubstitution()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--fs: 16px; font: bold var(--fs)/1.4 Arial"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("bold", el!.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string FontHtml(string fontValue) => $"""
            <!DOCTYPE html><html><body>
            <div id="el" style="font: {fontValue}"></div>
            </body></html>
            """;

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
