using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tests for the media-type and author-style-suppression cascade knobs that back the CLI's
    /// <c>--media</c> and <c>--no-author-style</c> options: <see cref="HtmlContainerInt.Media"/> selects
    /// which <c>@media</c> rules match (default <c>print</c>), and
    /// <see cref="HtmlContainerInt.IgnoreAuthorStyleSheets"/> skips the document's own
    /// <c>&lt;style&gt;</c>/<c>&lt;link&gt;</c> author style sheets.
    /// </summary>
    public class MediaTypeConfigTests
    {
        private const string MediaHtml =
            "<html><head><style>" +
            "@media screen { #el { color: rgb(0, 0, 255); } }" +
            "@media print { #el { color: rgb(255, 0, 0); } }" +
            "</style></head><body><p id=\"el\">text</p></body></html>";

        private const string AuthorStyleHtml =
            "<html><head><style>#el { color: rgb(255, 0, 0); }</style></head>" +
            "<body><p id=\"el\">text</p></body></html>";

        [Fact]
        public async Task Media_DefaultPrint_MatchesPrintRule()
        {
            var el = await BuildBoxTree(MediaHtml, media: "print", ignoreAuthorStyles: false);
            Assert.Equal("rgb(255, 0, 0)", el.Color);
        }

        [Fact]
        public async Task Media_Screen_MatchesScreenRule()
        {
            var el = await BuildBoxTree(MediaHtml, media: "screen", ignoreAuthorStyles: false);
            Assert.Equal("rgb(0, 0, 255)", el.Color);
        }

        [Fact]
        public async Task AuthorStyles_Applied_WhenNotIgnored()
        {
            var el = await BuildBoxTree(AuthorStyleHtml, media: "print", ignoreAuthorStyles: false);
            Assert.Equal("rgb(255, 0, 0)", el.Color);
        }

        [Fact]
        public async Task AuthorStyles_Ignored_WhenFlagSet()
        {
            var el = await BuildBoxTree(AuthorStyleHtml, media: "print", ignoreAuthorStyles: true);
            Assert.NotEqual("rgb(255, 0, 0)", el.Color);
        }

        private static async Task<CssBox> BuildBoxTree(string html, string media, bool ignoreAuthorStyles)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter)
            {
                Media = media,
                IgnoreAuthorStyleSheets = ignoreAuthorStyles,
            };
            await container.SetHtml(html, null);

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
