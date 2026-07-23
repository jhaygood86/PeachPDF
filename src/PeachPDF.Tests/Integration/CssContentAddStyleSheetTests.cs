using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tests for <see cref="PeachPdfCssContent.AddStyleSheet"/>: additional stylesheets are merged into
    /// the underlying style data and take precedence in source order (later sheets win), and the lazily
    /// built selector index is rebuilt so the added rules actually match.
    /// </summary>
    public class CssContentAddStyleSheetTests
    {
        private const string Html =
            "<html><body><p id=\"el\" class=\"x\">text</p></body></html>";

        [Fact]
        public async Task AddStyleSheet_AddedRuleApplies()
        {
            var generator = new PdfGenerator();
            var cssContent = await generator.ParseStyleSheet(string.Empty);
            await cssContent.AddStyleSheet(".x { color: rgb(255, 0, 0); }");

            var el = await BuildBoxTree(Html, cssContent);
            Assert.Equal("rgb(255, 0, 0)", el.Color);
        }

        [Fact]
        public async Task AddStyleSheet_LaterSheetWins()
        {
            var generator = new PdfGenerator();
            var cssContent = await generator.ParseStyleSheet(string.Empty);
            await cssContent.AddStyleSheet(".x { color: rgb(255, 0, 0); }");
            await cssContent.AddStyleSheet(".x { color: rgb(0, 128, 0); }");

            var el = await BuildBoxTree(Html, cssContent);
            Assert.Equal("rgb(0, 128, 0)", el.Color);
        }

        [Fact]
        public async Task AddStyleSheet_DoesNotLeakIntoTheSharedDefault()
        {
            // A second style content parsed from the same generator must not see rules added to the first
            // (ParseStyleSheet clones the shared default before merging).
            var generator = new PdfGenerator();

            var first = await generator.ParseStyleSheet(string.Empty);
            await first.AddStyleSheet(".x { color: rgb(255, 0, 0); }");

            var second = await generator.ParseStyleSheet(string.Empty);

            var el = await BuildBoxTree(Html, second);
            Assert.NotEqual("rgb(255, 0, 0)", el.Color);
        }

        [Fact]
        public async Task AddStyleSheet_WithoutDefault_StillApplies()
        {
            var generator = new PdfGenerator();
            var cssContent = await generator.ParseStyleSheet(string.Empty, combineWithDefault: false);
            await cssContent.AddStyleSheet(".x { color: rgb(0, 0, 255); }");

            var el = await BuildBoxTree(Html, cssContent);
            Assert.Equal("rgb(0, 0, 255)", el.Color);
        }

        private static async Task<CssBox> BuildBoxTree(string html, PeachPdfCssContent cssContent)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, cssContent.CssData);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var el = FindById(container.Root!, "el");
            Assert.NotNull(el);
            return el!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id", "") == id)
            {
                return box;
            }

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
